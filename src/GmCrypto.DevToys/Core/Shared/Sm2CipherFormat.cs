namespace GmCrypto.DevToys.Core;

/// <summary>
/// SM2 密文顺序转换。Core 固定输出 C1C3C2，部分老旧系统要求 C1C2C3。
/// 布局：C1 = 前 65 字节（未压缩点），C3 = 32 字节 SM3，C2 = 剩余密文，长度与明文一致。
/// </summary>
public static class Sm2CipherFormat
{
    /// <summary>密文最短长度：C1(65) + C3(32)。</summary>
    public const int MinimumLength = 97;

    public static byte[] C1C3C2ToC1C2C3(ReadOnlySpan<byte> c1c3c2)
    {
        EnsureLength(c1c3c2.Length);
        var result = new byte[c1c3c2.Length];
        c1c3c2[..65].CopyTo(result);                                   // C1
        c1c3c2[97..].CopyTo(result.AsSpan(65));                        // C2
        c1c3c2.Slice(65, 32).CopyTo(result.AsSpan(c1c3c2.Length - 32)); // C3
        return result;
    }

    public static byte[] C1C2C3ToC1C3C2(ReadOnlySpan<byte> c1c2c3)
    {
        EnsureLength(c1c2c3.Length);
        var result = new byte[c1c2c3.Length];
        c1c2c3[..65].CopyTo(result);                                      // C1
        c1c2c3.Slice(c1c2c3.Length - 32, 32).CopyTo(result.AsSpan(65));   // C3
        c1c2c3.Slice(65, c1c2c3.Length - 97).CopyTo(result.AsSpan(97));   // C2
        return result;
    }

    private static void EnsureLength(int length)
    {
        if (length < MinimumLength)
        {
            throw new InvalidOperationException($"SM2 密文长度不正确（至少 {MinimumLength} 字节，当前 {length} 字节）。");
        }
    }
}
