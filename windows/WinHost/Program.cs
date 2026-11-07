// Phase 0 驱动：验证「Windows 前台假 Agent 两轮闭环 + 进程树可控」。
//   winhost rounds   —— 用 mock-agent.mjs 正常模式连续跑两轮，证明受监督的启动/退出闭环。
//   winhost hang     —— 用 mock-agent.mjs hang 模式（父子进程都挂住），证明 TerminateJobObject 清整棵进程树。
// 这是文档第 8 节 Job Object 合同的最小可运行证明；不是产品入口，后面 Phase 2 才会接 TS Hub 的账本与对账。
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using WinHost;

Console.OutputEncoding = Encoding.UTF8;

var node = FindNode() ?? throw new Exception("找不到 node：把 node 加入 PATH 再试");
var root = AppContext.BaseDirectory;
var mockAgent = Path.Combine(root, "mock-agent.mjs");
if (!File.Exists(mockAgent)) throw new Exception($"找不到 {mockAgent}");

var action = args.Length == 0 ? "rounds" : args[0];
switch (action)
{
    case "rounds":
        await RunRounds(node, mockAgent);
        break;
    case "hang":
        RunHangCleanup(node, mockAgent);
        break;
    case "selftest":
        Console.WriteLine("WinHost Phase 0 selftest OK: .NET 10 + Job Object P/Invoke 已加载");
        break;
    case "pipe":
        ControlPipeSelftest();
        break;
    case "tray":
        TraySelftest();
        break;
    case "notify":
        NotifySelftest(node);
        break;
    default:
        Console.Error.WriteLine($"未知动作：{action}（rounds | hang | pipe | tray | notify | selftest）");
        Environment.Exit(2);
        break;
}

// --- 两轮闭环 ---
static async Task RunRounds(string node, string mockAgent)
{
    var env = new Dictionary<string, string> { ["MOCK_MODE"] = "normal" };
    for (var round = 1; round <= 2; round++)
    {
        env["MOCK_RUN"] = round.ToString();
        var launchId = DateTime.UtcNow.Ticks;
        using var job = JobProcess.Start("", $"\"{node}\" \"{mockAgent}\"", Path.GetDirectoryName(mockAgent)!, launchId);
        Console.WriteLine($"[round {round}] launchID={job.LaunchId} pid={job.Pid} started");
        var code = job.Wait(30_000);
        if (code != 0)
        {
            Console.Error.WriteLine($"[round {round}] FAIL: 退出码 {code}");
            Environment.Exit(1);
        }
        Console.WriteLine($"[round {round}] 闭环成功：正常退出 pid={job.Pid}");
    }
    Console.WriteLine("Phase 0 两轮闭环 PASS");
}

// --- 树清理 ---
static void RunHangCleanup(string node, string mockAgent)
{
    // 先拿到 hang 模式父进程的 PID，其子进程继承同一个 Job。这里必须自己 find 子进程，证明 kill 的是整棵树。
    var env = new Dictionary<string, string> { ["MOCK_MODE"] = "hang" };
    var launchId = DateTime.UtcNow.Ticks;
    using var job = JobProcess.Start("", $"\"{node}\" \"{mockAgent}\"", Path.GetDirectoryName(mockAgent)!, launchId, env);
    Console.WriteLine($"[hang] launchID={job.LaunchId} pid={job.Pid} started");

    // 等子进程出现（hang 模式会 spawn 一个 node -e setInterval 的子进程）。
    Thread.Sleep(1_000);
    var children = FindDescendants(job.Pid).Where(p => p != job.Pid).ToList();
    Console.WriteLine($"[hang] 进程树内子进程数 = {children.Count}（期望 >= 1）");
    if (children.Count < 1)
    {
        Console.Error.WriteLine("[hang] FAIL: 没观察到子进程，树监督证据不足");
        Environment.Exit(1);
    }

    job.Stop();
    Thread.Sleep(500);
    var parentAlive = IsAlive(job.Pid);
    var anyChildAlive = children.Any(IsAlive);
    Console.WriteLine($"[hang] TerminateJobObject 后：父存活={parentAlive}，子存活={anyChildAlive}");
    if (parentAlive || anyChildAlive)
    {
        Console.Error.WriteLine("[hang] FAIL: 进程树未被清干净");
        Environment.Exit(1);
    }
    Console.WriteLine("Phase 0 进程树清理 PASS（KILL_ON_JOB_CLOSE + TerminateJobObject）");
}

// --- 管理页 / 托盘 ---
static void TraySelftest()
{
    // 只构造页面与托盘标题，不 Show：真实渲染/交互是桌面会话项，真机 NOT_RUN。
    var states = Enum.GetValues<TrayStatus>();
    foreach (var s in states)
    {
        var text = TrayContext.StatusText(s);
        if (string.IsNullOrWhiteSpace(text)) { Console.Error.WriteLine($"[tray] FAIL: 状态 {s} 没有文本"); Environment.Exit(1); }
    }
    Console.WriteLine($"[tray] 四态文本 = {string.Join(" / ", states.Select(TrayContext.StatusText))}");

    // 别人写的字（Agent 标题）当普通文本，绝不解释成标记：状态文本本身不该包含换行等格式控制。
    foreach (var s in states)
    {
        var text = TrayContext.StatusText(s);
        if (text.Contains('\n') || text.Contains('\r') || text.Contains('<')) { Console.Error.WriteLine("[tray] FAIL: 状态文本含格式控制"); Environment.Exit(1); }
    }

    // 托盘图标与页面构造不抛异常；真正的显示与可达性（键盘/Narrator/高对比/高 DPI）真机 NOT_RUN。
    using var icon = TrayContext.CreateIcon(TrayStatus.Running);
    Console.WriteLine($"[tray] 托盘标题 = {icon.Text}（期望 运行中）");
    if (icon.Text != "运行中") { Console.Error.WriteLine("[tray] FAIL: 托盘标题不对"); Environment.Exit(1); }

    var toggled = new List<string>();
    using var page = TrayContext.CreatePage(() => TrayStatus.NeedsDecision, () => toggled.Add("start"), () => toggled.Add("stop"));
    Console.WriteLine($"[tray] 管理页控件数 = {page.Controls.Count}（期望 4）");
    if (page.Controls.Count != 4) { Console.Error.WriteLine("[tray] FAIL: 管理页控件数不对"); Environment.Exit(1); }
    Console.WriteLine("Phase 4 管理页/托盘 PASS（真实显示与可达性：NOT_RUN，需桌面会话）");
}

// --- 通知 ---
static void NotifySelftest(string node)
{
    var uuid = Guid.NewGuid().ToString("N");
    var appId = Notifications.AppId(uuid);
    Console.WriteLine($"[notify] AUMID = {appId}");

    // cardID 是幂等身份，revision 是更新身份；不含 cardID 就不构成可追踪通知（文档 15.2）。
    var toast = new Toast(uuid, 1, "要删吗", "占了 10 GB");
    var args = Notifications.ToastCommand(appId, toast);
    var hasCardId = args.Contains("--card-id") && args.Contains(uuid);
    var hasRevision = args.Contains("--revision") && args.Contains("1");
    var hasPublic = args.Contains(toast.Title) && args.Contains(toast.Summary);
    Console.WriteLine($"[notify] 含 cardID={hasCardId} revision={hasRevision} 公开摘要={hasPublic}（期望全 True）");
    if (!hasCardId || !hasRevision || !hasPublic) { Console.Error.WriteLine("[notify] FAIL: 通知参数缺少幂等身份或公开摘要"); Environment.Exit(1); }

    // 高风险批准不带 bearer token：命令参数里不得出现 token 字样。
    if (args.Any(a => a.Contains("token", StringComparison.OrdinalIgnoreCase))) { Console.Error.WriteLine("[notify] FAIL: 通知参数带 token"); Environment.Exit(1); }

    // 用假 toast 命令自检：参数经 ProcessStartInfo.ArgumentList 逐字传递，不经过 shell，
    // 标题里任何字符（含空格/引号/换行）都是数据，不会被解释成命令。
    var script = Path.Combine(AppContext.BaseDirectory, "toast-standin.mjs");
    if (!File.Exists(script)) { Console.Error.WriteLine($"找不到 {script}"); Environment.Exit(1); }
    var hostile = new Toast(uuid, 1, "标题 with spaces \" quotes", "summary & <not-markup>");
    var psi = new ProcessStartInfo(node) { RedirectStandardOutput = true, UseShellExecute = false };
    psi.ArgumentList.Add(script);
    foreach (var a in Notifications.ToastCommand(appId, hostile)) psi.ArgumentList.Add(a);
    using var proc = Process.Start(psi) ?? throw new Exception("toast-standin 启动失败");
    var printed = proc.StandardOutput.ReadToEnd();
    proc.WaitForExit(5_000);
    var expected = Notifications.ToastCommand(appId, hostile);
    var lines = printed.TrimEnd('\n').Split('\n');
    var intact = lines.Length == expected.Length && lines.Zip(expected).All(p => p.First == p.Second);
    Console.WriteLine($"[notify] 参数逐字传递 = {intact}（期望 True）");
    if (!intact) { Console.Error.WriteLine("[notify] FAIL: 参数没逐字传过去"); Environment.Exit(1); }

    // 注册/激活、后台激活、勿扰、Narrator 需要 Windows App SDK 运行时与桌面会话，真机 NOT_RUN。
    Console.WriteLine("Phase 4 通知骨架 PASS（真实 Toast 注册/激活/勿扰/Narrator：NOT_RUN，需 App SDK + 桌面会话）");
}

// --- 工具 ---
static void ControlPipeSelftest()
{
    // 管道名里必须带上 installUUID（文档 10.1）。这里用固定 UUID 作 selftest。
    var uuid = Guid.NewGuid().ToString("N");
    var pipe = new ControlPipe(uuid, method => method == "ping" ? "pong" : throw new Exception($"没有这个动作：{method}"));

    // SDDL 里必须有对 Network/Anonymous 的 Deny：远端客户端由此被拒（文档 10.1）。
    var sddl = ControlPipe.SecuritySddl();
    var hasNetworkDeny = sddl.Contains("(D;;GA;;;NU)") && sddl.Contains("(D;;GA;;;AN)");
    Console.WriteLine($"[pipe] SDDL 含 Network/Anonymous Deny = {hasNetworkDeny}（期望 True）");
    if (!hasNetworkDeny) { Console.Error.WriteLine("[pipe] FAIL: SDDL 没有拒绝网络/匿名登录"); Environment.Exit(1); }
    Console.WriteLine($"[pipe] SDDL = {sddl}");

    var server = Task.Run(pipe.Run);

    // 当前用户 + 白名单方法：通过。
    var ok = Send(uuid, JsonSerializer.Serialize(new { method = "ping" }));
    Console.WriteLine($"[pipe] 合法方法 ping -> {ok}（期望 ok=true）");
    if (!ok.Contains("\"ok\":true")) { Console.Error.WriteLine("[pipe] FAIL: 合法方法没通过"); Environment.Exit(1); }

    // 白名单外的方法：拒绝。
    var bad = Send(uuid, JsonSerializer.Serialize(new { method = "drop-all" }));
    Console.WriteLine($"[pipe] 白名单外方法 -> {bad.Trim()}（期望 ok=false）");
    if (!bad.Contains("\"ok\":false")) { Console.Error.WriteLine("[pipe] FAIL: 白名单外方法没被拒"); Environment.Exit(1); }

    // 超长消息：服务端读到超限长度直接断开；客户端读到 EOF，即被拒。
    var longMsg = new string('x', ControlPipe.MaxMessageBytes + 100);
    var tooLong = Send(uuid, longMsg);
    Console.WriteLine($"[pipe] 超长消息 -> {(tooLong == "" ? "（EOF，已断开）" : tooLong.Trim())}（期望被拒）");
    if (tooLong.Contains("\"ok\":true")) { Console.Error.WriteLine("[pipe] FAIL: 超长消息被放行了"); Environment.Exit(1); }

    pipe.Stop();
    try { server.Wait(2000); } catch { }

    // 注意：跨账号（非当前用户）的拒绝需要第二个 Windows 账号，本机只有一个，记 NOT_RUN 不做伪证。
    Console.WriteLine("Phase 0 Named Pipe 控制面 PASS（非预期身份拒绝：NOT_RUN，需第二个账号）");
}

static string Send(string uuid, string raw)
{
    using var client = new NamedPipeClientStream(".", ControlPipe.PipeName(uuid), PipeDirection.InOut, PipeOptions.None);
    client.Connect(3000);
    var bytes = Encoding.UTF8.GetBytes(raw);
    // 长度前缀协议：4 字节小端长度 + 正文。
    try
    {
        client.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
        client.Write(bytes, 0, bytes.Length);
        client.Flush();
    }
    catch (IOException)
    {
        // 超长消息时服务端读到超限长度就断开：写正文碰到 broken pipe，即被拒。
        return "";
    }
    using var reader = new StreamReader(client, Encoding.UTF8);
    return reader.ReadLine() ?? "";
}

static string? FindNode()
{
    foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
    {
        var exe = Path.Combine(dir.Trim(), "node.exe");
        if (File.Exists(exe)) return exe;
    }
    return null;
}

static bool IsAlive(int pid)
{
    try { using var _ = Process.GetProcessById(pid); return !_.HasExited; }
    catch { return false; }
}

static List<int> FindDescendants(int rootPid)
{
    var outIds = new List<int>();
    // Phase 0 只粗证「hang 模式下存在父 PID == rootPid 的子 node 进程，Stop 后全部消失」；
    // Job 内精确进程枚举（QueryInformationJobObject）在 Phase 2 引入，届时替换这里。
    foreach (var p in Process.GetProcessesByName("node"))
    {
        try { if (Native.GetParentPid(p.Id) == rootPid) outIds.Add(p.Id); } catch { }
    }
    return outIds;
}
