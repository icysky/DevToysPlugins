using System.Security.Cryptography;
using System.Text;

namespace GmCrypto.DevToys.Core;

/// <summary>
/// Hex 输入输出的统一规则（与 GM 桌面程序保持一致）：
/// 输入大小写不敏感，允许空格 / '-' / ':' / ',' 分隔与 0x 前缀；输出一律小写。
/// </summary>
public static class HexCodec
{
    public static string ToHex(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(bytes).ToLowerInvariant();

    /// <summary>随机字节的 Hex（用于生成 SM4 Key / IV）。</summary>
    public static string RandomHex(int byteCount)
        => ToHex(RandomNumberGenerator.GetBytes(byteCount));

    public static byte[] Parse(string? text, string what)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"请填写{what}。");
        }

        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c) || c == '-' || c == ':' || c == ',')
            {
                continue;
            }

            builder.Append(c);
        }

        string hex = builder.ToString();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            hex = hex[2..];
        }

        if (hex.Length == 0 || hex.Length % 2 != 0)
        {
            throw new InvalidOperationException($"{what}的 Hex 长度必须为偶数。");
        }

        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{what}不是合法的 Hex 字符串。");
        }
    }

    /// <summary>按固定长度解析（SM4 密钥 / IV = 16 字节，SM2 私钥 32、公钥 65）。</summary>
    public static byte[] ParseFixed(string? text, int expectedLength, string what)
    {
        byte[] bytes = Parse(text, what);
        if (bytes.Length != expectedLength)
        {
            throw new InvalidOperationException(
                $"{what}必须是 {expectedLength} 字节（{expectedLength * 2} 位 Hex），当前 {bytes.Length} 字节。");
        }

        return bytes;
    }
}
