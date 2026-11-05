// 本地控制面：Named Pipe 入口（文档 10.1 的最小实现）。
// 合同：
//   - 消息长度、并发连接数、方法白名单都有限制，超限或白名单外一律拒绝。
//   - 管道名前缀包含 installUUID，避免不同安装串管道。
//   - 显式 DACL（拒绝网络/匿名登录）由 Security() 给出 SDDL；真正把 DACL 施加到管道、以及
//     「非预期身份被拒」的验证，需要 CreateNamedPipeW P/Invoke 与第二个 Windows 账号，本机
//     只有一个账号，记 NOT_RUN（见 windows/README.md 的 NOT_RUN 清单）。
// 这是 Phase 0 收尾的可运行证明，不是产品入口：Phase 2 才会把管理请求经此转到 TS Hub 的私有控制通道。
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace WinHost;

/// <summary>一次受监督的管理连接：显式方法白名单、长度与连接数受限。</summary>
[SupportedOSPlatform("windows")]
public sealed class ControlPipe
{
    public const int MaxMessageBytes = 4096;
    public const int MaxInstances = 1;

    // 方法白名单：只认这些；多一分控制面就多一分可被滥用的入口。
    private static readonly HashSet<string> Methods = new(StringComparer.Ordinal) { "ping", "status" };

    private readonly string _pipeName;
    private readonly Func<string, string> _dispatch;
    private readonly CancellationTokenSource _stop = new();

    /// <param name="installUuid">安装身份：管道名前缀的一部分。</param>
    /// <param name="dispatch">白名单方法 -> 响应正文；抛异常当作内部错误返回。</param>
    public ControlPipe(string installUuid, Func<string, string> dispatch)
    {
        _pipeName = PipeName(installUuid);
        _dispatch = dispatch;
    }

    public static string PipeName(string installUuid) => $"agent-autoloop-{installUuid}";

    /// <summary>
    /// 显式 DACL 的 SDDL：只允许当前用户，拒绝 Network 与 Anonymous 登录。
    /// 施加到管道需要 CreateNamedPipeW（P/Invoke 携带 SECURITY_ATTRIBUTES），本 Phase 未落地，记 NOT_RUN。
    /// </summary>
    public static string SecuritySddl()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("拿不到当前用户 SID");
        // GRGW = 读/写（GENERIC_READ | GENERIC_WRITE）；GA = 全部拒绝。
        return $"D:(A;;GRGW;;;{user.Value})(D;;GA;;;NU)(D;;GA;;;AN)";
    }

    /// <summary>监听并串行处理连接，直到 Stop。</summary>
    public async Task Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            // 注意：PipeOptions.Asynchronous 会让 WaitForConnectionAsync 卡住不返回，这里必须用 None。
            var server = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                MaxInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.None);
            try
            {
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                await Handle(server).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // 一个坏连接不影响下一个。
            }
            finally
            {
                server.Dispose();
            }
        }
    }

    public void Stop() => _stop.Cancel();

    private static async Task<int> ReadExact(NamedPipeServerStream server, byte[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            var read = await server.ReadAsync(buffer.AsMemory(total, count - total)).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private async Task Handle(NamedPipeServerStream server)
    {
        // 长度前缀协议：先读 4 字节小端长度，再读正文。长度一超 MaxMessageBytes 立刻拒绝，
        // 不读对端后续字节，也不把内存喂给对端。
        var lengthBytes = new byte[4];
        int got;
        try
        {
            got = await ReadExact(server, lengthBytes, 4).ConfigureAwait(false);
        }
        catch
        {
            return;
        }
        if (got < 4)
        {
            await Write(server, new { ok = false, error = "读不到消息长度" }).ConfigureAwait(false);
            return;
        }
        var length = BitConverter.ToUInt32(lengthBytes, 0);
        if (length > MaxMessageBytes)
        {
            // 消息过长：不写回包，直接断开。对端仍在写正文，写回包会与它互相阻塞。
            server.Dispose();
            return;
        }
        var buffer = new byte[length];
        if (length > 0 && await ReadExact(server, buffer, (int)length).ConfigureAwait(false) < length)
        {
            await Write(server, new { ok = false, error = "消息没读完" }).ConfigureAwait(false);
            return;
        }
        var text = Encoding.UTF8.GetString(buffer).Trim();
        if (string.IsNullOrEmpty(text))
        {
            await Write(server, new { ok = false, error = "空消息" }).ConfigureAwait(false);
            return;
        }

        string? method;
        try
        {
            using var doc = JsonDocument.Parse(text);
            method = doc.RootElement.GetProperty("method").GetString();
        }
        catch
        {
            await Write(server, new { ok = false, error = "消息不是合法的 JSON 或没有 method" }).ConfigureAwait(false);
            return;
        }

        if (method is null || !Methods.Contains(method))
        {
            await Write(server, new { ok = false, error = $"方法不在白名单：{method}" }).ConfigureAwait(false);
            return;
        }

        try
        {
            await Write(server, new { ok = true, result = _dispatch(method) }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            await Write(server, new { ok = false, error = error.Message }).ConfigureAwait(false);
        }
    }

    private async Task Write(NamedPipeServerStream server, object body)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body) + "\n");
        await server.WriteAsync(bytes, _stop.Token).ConfigureAwait(false);
        await server.FlushAsync(_stop.Token).ConfigureAwait(false);
    }
}
