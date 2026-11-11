// 秘密隔离的最小合同：DPAPI CurrentUser 封装（文档 11.1），不选 LocalMachine，不落明文。
// 合同：
//   - Protect 返回的密文里不得出现原文（明文泄露被拒）。
//   - Unprotect 能还原同一用户封装的原文；损坏的密文（坏 key）抛异常，绝不静默返回。
//   - 只用 CRYPTPROTECT_UI_FORBIDDEN 与默认 CurrentUser 作用域；不传 CRYPTPROTECT_LOCAL_MACHINE。
// TS/protocol 层不落明文密钥：这里只给「封装/解封」两个口子，密文离开 WinHost 之前由上层决定。
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace WinHost;

/// <summary>一次 DPAPI CurrentUser 封装：把密钥封成只有当前用户能解的密文。</summary>
public static class SecretStore
{
    /// <summary>用当前用户作用域封装明文；返回密文。原文不出现在密文里。</summary>
    public static byte[] Protect(string plaintext)
    {
        var input = Encoding.UTF8.GetBytes(plaintext);
        var inBlob = new DATA_BLOB { cbData = input.Length, pbData = AllocCopy(input) };
        try
        {
            if (!CryptProtectData(ref inBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, Flags, out var outBlob))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CryptProtectData 失败");
            try
            {
                var bytes = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                LocalFree(outBlob.pbData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inBlob.pbData);
        }
    }

    /// <summary>解封同一用户封装的密文；密文损坏或不是本用户封装的抛异常，绝不静默返回明文。</summary>
    public static string Unprotect(byte[] ciphertext)
    {
        var inBlob = new DATA_BLOB { cbData = ciphertext.Length, pbData = AllocCopy(ciphertext) };
        try
        {
            if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, Flags, out var outBlob))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CryptUnprotectData 失败");
            try
            {
                var bytes = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, bytes, 0, bytes.Length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                LocalFree(outBlob.pbData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inBlob.pbData);
        }
    }

    // UI_FORBIDDEN 且不带 LOCAL_MACHINE：默认就是 CurrentUser 作用域，交互与机器级封装都不参与。
    private const uint CRYPTPROTECT_UI_FORBIDDEN = 0x1;
    private const uint Flags = CRYPTPROTECT_UI_FORBIDDEN;

    private static IntPtr AllocCopy(byte[] bytes)
    {
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return ptr;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved,
        IntPtr pPromptStruct, uint dwFlags, out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved,
        IntPtr pPromptStruct, uint dwFlags, out DATA_BLOB pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
