using System.Text;

namespace GoogleAuthenticator.DevToys.Core;

/// <summary>
/// RFC 4648 Base32 编解码。OTP 密钥几乎都以 Base32（不带填充）形式给出。
/// </summary>
internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>
    /// 去掉空格、连字符、下划线和 <c>=</c> 填充，并统一转成大写。
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new(value.Length);
        foreach (char c in value)
        {
            if (char.IsWhiteSpace(c) || c is '-' or '_' or '=')
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }

    public static bool IsValid(string? value) => TryDecode(value, out _);

    public static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = [];
        string text = Normalize(value);
        if (text.Length == 0)
        {
            return false;
        }

        List<byte> output = new((text.Length * 5 / 8) + 1);
        int buffer = 0;
        int bitsLeft = 0;

        foreach (char c in text)
        {
            int index = Alphabet.IndexOf(c);
            if (index < 0)
            {
                return false;
            }

            buffer = (buffer << 5) | index;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }

        if (output.Count == 0)
        {
            return false;
        }

        bytes = [.. output];
        return true;
    }

    public static byte[] Decode(string? value)
    {
        if (!TryDecode(value, out byte[] bytes))
        {
            throw new FormatException("密钥不是合法的 Base32 字符串，请检查是否包含 0、1、8、9 等非法字符。");
        }

        return bytes;
    }

    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new(((data.Length * 8) + 4) / 5);
        int buffer = 0;
        int bitsLeft = 0;

        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                builder.Append(Alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }

        if (bitsLeft > 0)
        {
            builder.Append(Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        }

        return builder.ToString();
    }
}
