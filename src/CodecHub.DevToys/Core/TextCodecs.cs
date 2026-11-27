using System.Globalization;
using System.Net;
using System.Text;
using static CodecHub.DevToys.Core.CodecPrimitives;

namespace CodecHub.DevToys.Core;

/// <summary>URL / HTML / Unicode / JSON 四种文本表示之间的转换。</summary>
public static class TextCodecs
{
    public static string UrlEncode(string input, bool plusAsSpace)
    {
        string encoded = Uri.EscapeDataString(input);
        return plusAsSpace ? encoded.Replace("%20", "+") : encoded;
    }

    public static string UrlDecode(string input, bool plusAsSpace)
    {
        string text = plusAsSpace ? input.Replace('+', ' ') : input;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '%')
            {
                continue;
            }

            if (i + 2 >= text.Length || !TryParseHexDigits(text, i + 1, 2, out _))
            {
                throw new CodecException($"位置 {i + 1} 的 % 后面不是两位十六进制数。");
            }

            i += 2;
        }

        return Uri.UnescapeDataString(text);
    }

    public static string HtmlEncode(string input) => WebUtility.HtmlEncode(input);

    public static string HtmlDecode(string input) => WebUtility.HtmlDecode(input);

    public static string UnicodeEncode(string input, TextEscapeStyle style)
    {
        var sb = new StringBuilder();
        foreach (Rune rune in input.EnumerateRunes())
        {
            if (style == TextEscapeStyle.CodePoint)
            {
                sb.Append(rune.Value <= 0xFFFF ? $"U+{rune.Value:X4}" : $"U+{rune.Value:X5}");
                sb.Append(' ');
                continue;
            }

            if (rune.Value is >= 0x20 and <= 0x7E && rune.Value != '\\')
            {
                sb.Append(rune.ToString());
                continue;
            }

            if (style == TextEscapeStyle.HexEscape && rune.Value <= 0xFF)
            {
                sb.Append($"\\x{rune.Value:X2}");
                continue;
            }

            string utf16 = rune.ToString();
            foreach (char unit in utf16)
            {
                sb.Append($"\\u{(int)unit:X4}");
            }
        }

        return style == TextEscapeStyle.CodePoint ? sb.ToString().TrimEnd() : sb.ToString();
    }

    public static string UnicodeDecode(string input)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];
            int? value = null;

            if (c == '\\' && i + 1 < input.Length && input[i + 1] == 'u' && TryParseHexDigits(input, i + 2, 4, out int u))
            {
                value = u;
                i += 6;
            }
            else if (c == '\\' && i + 1 < input.Length && input[i + 1] == 'x' && TryParseHexDigits(input, i + 2, 2, out int x))
            {
                value = x;
                i += 4;
            }
            else if (c == '%' && i + 1 < input.Length && input[i + 1] == 'u' && TryParseHexDigits(input, i + 2, 4, out int pu))
            {
                value = pu;
                i += 6;
            }
            else if (c == 'U' && i + 1 < input.Length && input[i + 1] == '+')
            {
                int end = i + 2;
                while (end < input.Length && end < i + 8 && HexValue(input[end]) >= 0)
                {
                    end++;
                }

                if (end == i + 2 || !int.TryParse(input.AsSpan(i + 2, end - i - 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp))
                {
                    throw new CodecException($"位置 {i + 1} 的 U+ 后面不是合法码点。");
                }

                if (cp > 0x10FFFF || cp is >= 0xD800 and <= 0xDFFF)
                {
                    throw new CodecException($"U+{cp:X} 不是合法的 Unicode 码点。");
                }

                sb.Append(char.ConvertFromUtf32(cp));
                i = end;
                continue;
            }
            else
            {
                sb.Append(c);
                i++;
                continue;
            }

            char unit = (char)value!.Value;
            if (char.IsHighSurrogate(unit))
            {
                if (i + 5 < input.Length && input[i] == '\\' && input[i + 1] == 'u' &&
                    TryParseHexDigits(input, i + 2, 4, out int low) && char.IsLowSurrogate((char)low))
                {
                    sb.Append(unit);
                    sb.Append((char)low);
                    i += 6;
                }
                else
                {
                    throw new CodecException("高位代理项后面缺少配对的 \\uDC00~\\uDFFF 低位代理项。");
                }
            }
            else if (char.IsLowSurrogate(unit))
            {
                throw new CodecException("出现了没有配对的低位代理项。");
            }
            else
            {
                sb.Append(unit);
            }
        }

        return sb.ToString();
    }

    public static string JsonEscape(string input)
    {
        var sb = new StringBuilder();
        foreach (char c in input)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append($"\\u{(int)c:X4}");
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    public static string JsonUnescape(string input)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < input.Length)
        {
            if (input[i] != '\\')
            {
                sb.Append(input[i++]);
                continue;
            }

            if (++i >= input.Length)
            {
                throw new CodecException("字符串以孤立的反斜杠结尾。");
            }

            char escape = input[i++];
            switch (escape)
            {
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case '/': sb.Append('/'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (!TryParseHexDigits(input, i, 4, out int value))
                    {
                        throw new CodecException($"位置 {i} 的 \\u 后面不是四位十六进制数。");
                    }

                    char unit = (char)value;
                    i += 4;
                    if (char.IsHighSurrogate(unit))
                    {
                        if (i + 5 < input.Length && input[i] == '\\' && input[i + 1] == 'u' &&
                            TryParseHexDigits(input, i + 2, 4, out int low) && char.IsLowSurrogate((char)low))
                        {
                            sb.Append(unit);
                            sb.Append((char)low);
                            i += 6;
                        }
                        else
                        {
                            throw new CodecException("JSON 中的高位代理项缺少低位代理项。");
                        }
                    }
                    else if (char.IsLowSurrogate(unit))
                    {
                        throw new CodecException("JSON 中出现了没有配对的低位代理项。");
                    }
                    else
                    {
                        sb.Append(unit);
                    }

                    break;
                default:
                    throw new CodecException($"不支持的 JSON 转义 \\{escape}。");
            }
        }

        return sb.ToString();
    }
}
