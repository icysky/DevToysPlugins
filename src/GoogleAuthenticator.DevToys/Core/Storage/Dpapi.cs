using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace GoogleAuthenticator.DevToys.Core.Storage;

/// <summary>
/// Windows DPAPI（CryptProtectData）封装：用当前 Windows 用户的凭据加密本地数据，
/// 无需用户额外设置密码，也无法被其他用户或拷走的文件直接解开。
/// </summary>
internal static class Dpapi
{
    private const int CryptProtectUiForbidden = 0x1;

    public static bool IsSupported => OperatingSystem.IsWindows();

    public static byte[] Protect(byte[] data)
        => Transform(data, protect: true);

    public static byte[] Unprotect(byte[] data)
        => Transform(data, protect: false);

    private static byte[] Transform(byte[] data, bool protect)
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("当前平台不支持 DPAPI。");
        }

        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            return [];
        }

        DataBlob input = default;
        DataBlob output = default;
        IntPtr inputHandle = IntPtr.Zero;

        try
        {
            inputHandle = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, inputHandle, data.Length);
            input.cbData = data.Length;
            input.pbData = inputHandle;

            bool ok = protect
                ? CryptProtectData(ref input, "GoogleAuthenticator.DevToys", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output);

            if (!ok)
            {
                throw new CryptographicException(Marshal.GetLastPInvokeError());
            }

            byte[] result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            if (inputHandle != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(inputHandle);
            }

            if (output.pbData != IntPtr.Zero)
            {
                LocalFree(output.pbData);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CryptProtectData")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CryptUnprotectData")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr handle);
}
