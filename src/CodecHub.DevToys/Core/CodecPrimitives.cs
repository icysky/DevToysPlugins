using System.Globalization;
using System.Numerics;
using System.Text;

namespace CodecHub.DevToys.Core;

/// <summary>字符集、Hex、码点描述等共用原语。DecodeFallback 用异常，避免静默丢数据。</summary>
public static class CodecPrimitives
{
    static CodecPrimitives()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding GetEncoding(CharsetName charset) => charset switch
    {
        CharsetName.Utf8 => new UTF8Encoding(false, true),
        CharsetName.Gb18030 => Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        CharsetName.Utf16Le => new UnicodeEncoding(false, false, true),
        CharsetName.Utf16Be => new UnicodeEncoding(true, false, true),
        CharsetName.Ascii => Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        _ => throw new CodecException($"不支持的字符集：{charset}")
    };

    public static string EncodingName(CharsetName charset) => charset switch
    {
        CharsetName.Utf8 => "UTF-8",
        CharsetName.Gb18030 => "GB18030",
        CharsetName.Utf16Le => "UTF-16LE",
        CharsetName.Utf16Be => "UTF-16BE",
        CharsetName.Ascii => "ASCII",
        _ => charset.ToString()
    };

    /// <summary>显式列出 BOM：严格解码用的 Encoding 不带 BOM 标识，GetPreamble() 会返回空数组。</summary>
    public static byte[] Bom(CharsetName charset) => charset switch
    {
        CharsetName.Utf8 => [0xEF, 0xBB, 0xBF],
        CharsetName.Utf16Le => [0xFF, 0xFE],
        CharsetName.Utf16Be => [0xFE, 0xFF],
        _ => []
    };

    public static byte[] TextToBytes(string text, CharsetName charset, bool includeBom)
    {
        try
        {
            byte[] body = GetEncoding(charset).GetBytes(text);
            byte[] preamble = includeBom ? Bom(charset) : [];
            if (preamble.Length == 0)
            {
                return body;
            }

            byte[] result = new byte[preamble.Length + body.Length];
            preamble.CopyTo(result, 0);
            body.CopyTo(result, preamble.Length);
            return result;
        }
        catch (EncoderFallbackException ex)
        {
            throw new CodecException($"文本无法用 {EncodingName(charset)} 表示：{ex.Message}");
        }
    }

    public static string BytesToText(byte[] bytes, CharsetName charset)
    {
        try
        {
            byte[] preamble = Bom(charset);
            if (preamble.Length > 0 && bytes.Length >= preamble.Length && bytes.AsSpan(0, preamble.Length).SequenceEqual(preamble))
            {
                bytes = bytes[preamble.Length..];
            }

            return GetEncoding(charset).GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new CodecException($"字节无法用 {EncodingName(charset)} 解码：{ex.Message}。可切换字符集或检查字节顺序。");
        }
    }

    public static string HexEncode(byte[] bytes, bool upper = true, string separator = "")
    {
        string hex = Convert.ToHexString(bytes);
        if (!upper)
        {
            hex = hex.ToLowerInvariant();
        }

        if (string.IsNullOrEmpty(separator))
        {
            return hex;
        }

        return string.Join(separator, Enumerable.Range(0, bytes.Length).Select(i => hex.Substring(i * 2, 2)));
    }

    public static byte[] HexDecode(string text)
    {
        string compact = new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (compact.Length % 2 != 0)
        {
            throw new CodecException("Hex 字符数必须是偶数。");
        }

        try
        {
            return Convert.FromHexString(compact);
        }
        catch (FormatException)
        {
            throw new CodecException("Hex 中含有非十六进制字符。");
        }
    }

    /// <summary>给 UI 的多视图用：列出文本对应的 Unicode 码点，过长时截断。</summary>
    public static string DescribeCodePoints(string text)
    {
        var parts = new List<string>();
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (parts.Count >= 400)
            {
                parts.Add("…");
                break;
            }

            string code = rune.Value <= 0xFFFF ? $"U+{rune.Value:X4}" : $"U+{rune.Value:X5}";
            parts.Add($"{code} {rune}");
        }

        return string.Join("  ", parts);
    }

    public static bool TryParseHexDigits(string text, int start, int count, out int value)
    {
        value = 0;
        if (start < 0 || count <= 0 || start + count > text.Length)
        {
            return false;
        }

        for (int i = start; i < start + count; i++)
        {
            int digit = HexValue(text[i]);
            if (digit < 0)
            {
                return false;
            }

            value = (value << 4) | digit;
        }

        return true;
    }

    public static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1
    };

    /// <summary>解析 2~36 进制整数，支持可选正负号与 0x/0b/0o 前缀。</summary>
    public static BigInteger ParseRadix(string text, int radix)
    {
        if (radix is < 2 or > 36)
        {
            throw new CodecException("进制必须在 2~36 之间。");
        }

        string input = text.Trim();
        if (input.Length == 0)
        {
            throw new CodecException("请输入数字。");
        }

        bool negative = false;
        if (input[0] is '+' or '-')
        {
            negative = input[0] == '-';
            input = input[1..];
            if (input.Length == 0)
            {
                throw new CodecException("符号后面缺少数字。");
            }
        }

        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && radix == 16) input = input[2..];
        else if (input.StartsWith("0b", StringComparison.OrdinalIgnoreCase) && radix == 2) input = input[2..];
        else if (input.StartsWith("0o", StringComparison.OrdinalIgnoreCase) && radix == 8) input = input[2..];

        if (input.Length == 0)
        {
            throw new CodecException("前缀后面缺少数字。");
        }

        BigInteger value = BigInteger.Zero;
        foreach (char c in input)
        {
            if (c == '_')
            {
                continue;
            }

            int digit = char.IsAsciiDigit(c) ? c - '0' : char.ToUpperInvariant(c) - 'A' + 10;
            if (digit < 0 || digit >= radix)
            {
                throw new CodecException($"字符“{c}”不是合法的 {radix} 进制数字。");
            }

            value = value * radix + digit;
        }

        return negative ? -value : value;
    }

    public static string FormatRadix(BigInteger value, int radix)
    {
        if (radix is < 2 or > 36)
        {
            throw new CodecException("进制必须在 2~36 之间。");
        }

        if (value.IsZero)
        {
            return "0";
        }

        bool negative = value.Sign < 0;
        BigInteger remaining = BigInteger.Abs(value);
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var chars = new List<char>();
        while (remaining > BigInteger.Zero)
        {
            remaining = BigInteger.DivRem(remaining, radix, out BigInteger remainder);
            chars.Add(digits[(int)remainder]);
        }

        if (negative)
        {
            chars.Add('-');
        }

        chars.Reverse();
        return new string(chars.ToArray());
    }

    public static string TrimForDisplay(string text, int max = 4000)
        => text.Length <= max ? text : text[..max] + "…（已截断）";

    public static string DateTimeText(DateTimeOffset value, bool utc)
    {
        DateTimeOffset shown = utc ? value.ToUniversalTime() : value.ToLocalTime();
        string zone = utc ? "Z" : shown.ToString("zzz", CultureInfo.InvariantCulture);
        return shown.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture) + " " + zone;
    }
}
