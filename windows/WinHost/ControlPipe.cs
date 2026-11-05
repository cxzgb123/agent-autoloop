// 本地控制面：Named Pipe 入口（文档 10.1 的最小实现）。
// 合同：
//   - 消息长度、并发连接数、方法白名单都有限制，超限或白名单外一律拒绝。
//   - 管道名前缀包含 installUUID，避免不同安装串管道。
//   - 显式 DACL（拒绝网络/匿名登录）由 Security() 给出 SDDL；真正把 DACL 施加到管道、以及
//     「非预期身份被拒」的验证，需要 CreateNamedPipeW P/Invoke 与第二个 Windows 账号，本机
//     只有一个账号，记 NOT_RUN（见 windows/README.md 的 NOT_RUN 清单）。
// 这是 Phase 0 收尾的可运行证明，不是产品入口：Phase 2 才会把管理请求经此转到 TS Hub 的私有控制通道。
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

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
    /// 由 CreateNamedPipeW（P/Invoke 携带 SECURITY_ATTRIBUTES）真正施加到管道；「非预期身份被拒」
    /// 仍需第二个 Windows 账号实机验证，本机只有一个，记 NOT_RUN。
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
            NamedPipeServerStream server;
            try
            {
                server = CreateDaclPipe();
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"[pipe] 建管道失败：{error.Message}");
                throw;
            }
            try
            {
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                await Handle(server).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception error)
            {
                // 一个坏连接不影响下一个；但管道本身建不起来要报出来，否则控制面静默失效。
                Console.Error.WriteLine($"[pipe] 连接失败：{error.Message}");
            }
            finally
            {
                server.Dispose();
            }
        }
    }

    public void Stop() => _stop.Cancel();

    // 用 CreateNamedPipeW 携带 SECURITY_ATTRIBUTES 真正施加 SecuritySddl() 的 DACL。
    // .NET 的 NamedPipeServerStreamAcl.Create 在 .NET 10 有 bug（ERROR_INVALID_PARAMETER），
    // 所以这里直接 P/Invoke，再把原始句柄包成 NamedPipeServerStream（文档 10.1）。
    private NamedPipeServerStream CreateDaclPipe()
    {
        var sddl = SecuritySddl();
        var secDesc = IntPtr.Zero;
        var sa = IntPtr.Zero;
        var handle = IntPtr.Zero;
        try
        {
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, ref secDesc, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ConvertStringSecurityDescriptorToSecurityDescriptor 失败");

            sa = Marshal.AllocHGlobal(Marshal.SizeOf<SECURITY_ATTRIBUTES>());
            Marshal.StructureToPtr(new SECURITY_ATTRIBUTES
            {
                nLength = (uint)Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                lpSecurityDescriptor = secDesc,
                bInheritHandle = false,
            }, sa, false);

            // PIPE_ACCESS_DUPLEX，PIPE_TYPE_BYTE + PIPE_READMODE_BYTE + PIPE_WAIT 都是 0，单实例。
            // 不用 FILE_FLAG_OVERLAPPED：同步 NamedPipeServerStream 盖在 overlapped 句柄上会卡在 WaitForConnectionAsync。
            const uint access = 0x00000003;
            handle = CreateNamedPipeW(
                $@"\\.\pipe\{_pipeName}", access,
                0 /* type=byte | readmode=byte | wait */,
                1, 0, 0, 0, sa);
            if (handle == new IntPtr(-1))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateNamedPipeW 失败");

            // 读回管道对象自己的 DACL，证明 deny ACE 真施加到了对象上（不只存在于 SDDL 字符串里）。
            // 读回时 GENERIC_ALL 被映射成 FILE_ALL_ACCESS（FA），所以按「Deny ACE 目标 SID」判断，不锁死掩码。
            var applied = ReadBackDaclSddl(handle);
            if (!applied.Contains("(D;;") || !applied.Contains(";;;NU)") || !applied.Contains(";;;AN)"))
            {
                CloseHandle(handle);
                throw new InvalidOperationException($"管道 DACL 施加失败：读回 SDDL 缺 Network/Anonymous Deny：{applied}");
            }

            var safe = new SafePipeHandle(handle, ownsHandle: true);
            handle = IntPtr.Zero; // 已交给 SafePipeHandle
            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: false, isConnected: false, safe);
        }
        finally
        {
            if (handle != IntPtr.Zero) CloseHandle(handle);
            if (sa != IntPtr.Zero) Marshal.FreeHGlobal(sa);
            if (secDesc != IntPtr.Zero) LocalFree(secDesc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public uint nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    // 从对象句柄读回 DACL 并转成 SDDL 字符串，验证 deny ACE 真落到了对象上。
    private static string ReadBackDaclSddl(IntPtr handle)
    {
        var sdSize = 0u;
        GetKernelObjectSecurity(handle, DACL_SECURITY_INFORMATION, IntPtr.Zero, 0, ref sdSize);
        if (sdSize == 0) return "";
        var sd = Marshal.AllocHGlobal((int)sdSize);
        try
        {
            if (!GetKernelObjectSecurity(handle, DACL_SECURITY_INFORMATION, sd, sdSize, ref sdSize))
                return "";
            if (ConvertSecurityDescriptorToStringSecurityDescriptorW(sd, 1, DACL_SECURITY_INFORMATION, out var sddlPtr, out _))
            {
                try { return Marshal.PtrToStringUni(sddlPtr) ?? ""; } finally { LocalFree(sddlPtr); }
            }
            return "";
        }
        finally
        {
            Marshal.FreeHGlobal(sd);
        }
    }

    private const uint DACL_SECURITY_INFORMATION = 0x00000004;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateNamedPipeW(
        string lpName, uint dwOpenMode, uint dwPipeMode, uint nMaxInstances,
        uint nOutBufferSize, uint nInBufferSize, uint nDefaultTimeOut, IntPtr lpSecurityAttributes);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string stringSecurityDescriptor, uint stringSDRevision, ref IntPtr securityDescriptor, out uint securityDescriptorSize);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertSecurityDescriptorToStringSecurityDescriptorW(
        IntPtr securityDescriptor, uint stringSDRevision, uint securityInformation, out IntPtr stringSecurityDescriptor, out uint stringSecurityDescriptorLen);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetKernelObjectSecurity(IntPtr handle, uint securityInformation, IntPtr securityDescriptor, uint length, ref uint lengthNeeded);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

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
