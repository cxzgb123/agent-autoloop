// Job Object 监督：CreateProcessW(CREATE_SUSPENDED) -> AssignProcessToJobObject(KILL_ON_JOB_CLOSE) -> ResumeThread。
// 任何一步失败都 fail closed：只回收自己拿到的句柄，绝不对任意 PID 执行 taskkill。
// 文档 8.2/8.3：进程身份是 (launchID + 启动时间 + exe 路径指纹)，PID 仅诊断。
// 退出码用自己持有的 hProcess 句柄读（WaitForSingleObject + GetExitCodeProcess），不依赖 Process.GetProcessById，
// 因为用 PID 反查的 Process 对象不持有退出码能力。
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinHost;

/// <summary>一次受监督的进程启动，及其所在的 Job Object。</summary>
public sealed class JobProcess : IDisposable
{
    private readonly IntPtr _job;
    private readonly IntPtr _hProcess;
    private readonly int _pid;
    private readonly long _launchId;

    public long LaunchId => _launchId;
    public int Pid => _pid;
    public bool IsRunning => WaitForSingleObject(_hProcess, 0) == WAIT_TIMEOUT;

    private JobProcess(IntPtr job, IntPtr hProcess, int pid, long launchId)
    {
        _job = job;
        _hProcess = hProcess;
        _pid = pid;
        _launchId = launchId;
    }

    /// <summary>启动并监督一个进程。node 等经 PATH 解析的 exe 用完整命令行；本地 exe 用 applicationName。</summary>
    public static JobProcess Start(string applicationName, string commandLine, string workingDirectory, long launchId, IReadOnlyDictionary<string, string>? extraEnv = null)
    {
        var job = CreateKillOnCloseJob();
        try
        {
            var si = new STARTUPINFOW { cb = (uint)Marshal.SizeOf<STARTUPINFOW>() };
            const uint flags = CREATE_SUSPENDED | CREATE_NEW_PROCESS_GROUP | CREATE_UNICODE_ENVIRONMENT;
            var env = extraEnv is null ? IntPtr.Zero : BuildEnvironment(extraEnv);
            try
            {
                if (!CreateProcessW(
                        applicationName.Length == 0 ? null : applicationName,
                        commandLine,
                        IntPtr.Zero, IntPtr.Zero, false,
                        flags, env, workingDirectory.Length == 0 ? null : workingDirectory,
                        ref si, out var pi))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessW 失败");

                try
                {
                    if (!AssignProcessToJobObject(job, pi.hProcess))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject 失败");
                    // 子进程不继承 Job 句柄（文档 8.2）。
                    if (ResumeThread(pi.hThread) == unchecked((uint)-1))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "ResumeThread 失败");
                }
                catch
                {
                    // 进程还没 ResumeThread，此刻它不会退出；必须显式终止，否则留下 suspended 孤儿（文档 8.4）。
                    try { TerminateProcess(pi.hProcess, 1); } catch { }
                    CloseHandle(pi.hThread);
                    CloseHandle(pi.hProcess);
                    throw;
                }
                CloseHandle(pi.hThread);
                return new JobProcess(job, pi.hProcess, (int)pi.dwProcessId, launchId);
            }
            finally
            {
                if (env != IntPtr.Zero) Marshal.FreeHGlobal(env);
            }
        }
        catch
        {
            CloseHandle(job);
            throw;
        }
    }

    // CREATE_UNICODE_ENVIRONMENT 需要双 NUL 结尾的宽字符环境块：继承当前进程环境再叠加 override。
    private static IntPtr BuildEnvironment(IReadOnlyDictionary<string, string> extra)
    {
        var current = Environment.GetEnvironmentVariables();
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in current)
            if (entry.Key is string k && entry.Value is string v) merged[k] = v;
        foreach (var (k, v) in extra) merged[k] = v;
        var block = string.Join("\0", merged.Select(p => $"{p.Key}={p.Value}")) + "\0\0";
        return Marshal.StringToHGlobalUni(block);
    }

    /// <summary>等进程退出，返回退出码；超时返回 -1。</summary>
    public int Wait(int timeoutMs)
    {
        if (WaitForSingleObject(_hProcess, (uint)timeoutMs) != WAIT_OBJECT_0) return -1;
        return GetExitCodeProcess(_hProcess, out var code) ? (int)code : -1;
    }

    /// <summary>停止整棵进程树。优雅停止不是 SIGTERM 语义，这里直接 TerminateJobObject；有 provider stop 能力时由上层先请求。</summary>
    public void Stop()
    {
        if (!IsRunning) return;
        if (!TerminateJobObject(_job, 1u))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "TerminateJobObject 失败");
        WaitForSingleObject(_hProcess, 5_000);
    }

    public void Dispose()
    {
        // 最后句柄关闭触发 KILL_ON_JOB_CLOSE：Hub 崩溃重建不会杀 agent，WinHost 退出才会（文档 7 第 3 点）。
        CloseHandle(_hProcess);
        CloseHandle(_job);
    }

    private static IntPtr CreateKillOnCloseJob()
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObjectW 失败");
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE },
        };
        var length = (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal((int)length);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(job, JobObjectInfoType.ExtendedLimitInformation, ptr, length))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject(KILL_ON_JOB_CLOSE) 失败");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return job;
    }

    // --- P/Invoke（最小集合）---
    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint CREATE_NEW_PROCESS_GROUP = 0x00000200;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const uint WAIT_TIMEOUT = 0x00000102;
    private const uint WAIT_OBJECT_0 = 0x00000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOW { public uint cb; public IntPtr lpReserved, lpDesktop, lpTitle; public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags; public ushort wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public uint dwProcessId, dwThreadId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION { public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation; public IO_COUNTERS IoInfo; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }

    private enum JobObjectInfoType { ExtendedLimitInformation = 9 }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(
        string? lpApplicationName, string lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles,
        uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFOW lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll")]
    private static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
