// P/Invoke 最小集合：进程树枚举（Phase 0 只用来证明 hang 模式下存在子进程且 Stop 后消失）。
// Phase 2 会用 Job 内进程枚举（QueryInformationJobObject）替换这个粗证。
using System.Runtime.InteropServices;

namespace WinHost;

internal static class Native
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public static int GetParentPid(int pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return -1;
        try
        {
            var pbi = new PROCESS_BASIC_INFORMATION();
            var status = NtQueryInformationProcess(h, 0, ref pbi, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _);
            return status == 0 ? (int)pbi.InheritedFromUniqueProcessId : -1;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr handle, int infoClass, ref PROCESS_BASIC_INFORMATION pbi, int size, out int returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);
}
