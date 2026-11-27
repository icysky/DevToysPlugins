using System.Globalization;
using System.Numerics;
using static CodecHub.DevToys.Core.CodecPrimitives;

namespace CodecHub.DevToys.Core;

/// <summary>模式分发 + 多视图计算。UI 只需把 CodecResult 填进控件。</summary>
public static class CodecEngine
{
    public static CodecResult Run(string input, CodecOptions options)
    {
        try
        {
            CodecResult result = ApplyMode(input, options);
            if (options.FollowUp is null || options.FollowUp == options.Mode)
            {
                return result;
            }

            var followOptions = new CodecOptions
            {
                Mode = options.FollowUp.Value,
                Direction = CodecDirection.Decode,
                Charset = options.Charset,
                PlusAsSpace = options.PlusAsSpace
            };
            string chained = ApplyMode(result.PrimaryText, followOptions).PrimaryText;
            IReadOnlyList<CodecView> views = IsTextual(options.Mode)
                ? TextViews(chained, options.Charset)
                : result.Views;
            return result with { PrimaryText = chained, Views = views };
        }
        catch (CodecException)
        {
            throw;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or InvalidOperationException)
        {
            throw new CodecException(ex.Message);
        }
    }

    public static string ModeTitle(CodecMode mode, CodecDirection direction)
    {
        string name = mode switch
        {
            CodecMode.UrlComponent => "URL 组件",
            CodecMode.HtmlEntity => "HTML 实体",
            CodecMode.UnicodeEscape => "Unicode 转义",
            CodecMode.JsonEscape => "JSON 字符串",
            CodecMode.Base64 => "Base64",
            CodecMode.Base64Url => "Base64url",
            CodecMode.Base32 => "Base32",
            CodecMode.Base58 => "Base58",
            CodecMode.HexBytes => "Hex 字节",
            CodecMode.Charset => "字符集转码",
            CodecMode.Radix => "进制互转",
            CodecMode.TwosComplement => "补码 / 字节序",
            CodecMode.IpAddress => "IPv4 地址",
            CodecMode.UnixTime => "Unix 时间戳",
            CodecMode.GuidFormat => "GUID 格式",
            _ => mode.ToString()
        };

        return direction == CodecDirection.Encode ? $"{name} → 编码" : $"{name} → 解码";
    }

    public static string? ModeHint(CodecMode mode) => mode switch
    {
        CodecMode.UrlComponent => "只编码单个参数值；表单模式把空格写为 +，URL 组件模式写为 %20。",
        CodecMode.HtmlEntity => "HTML 实体：< > & \" ' 与 &#xNN; / &#NNN; 互转。",
        CodecMode.UnicodeEscape => "支持 \\uXXXX、\\xNN、%uXXXX、U+XXXXX，并正确合并 Emoji 代理对。",
        CodecMode.JsonEscape => "JSON 字符串内部转义，支持 \\n、\\t、\\uXXXX 与代理对。",
        CodecMode.Base64 => "标准 Base64；可切换是否保留 = 填充。解码自动忽略换行。",
        CodecMode.Base64Url => "URL 安全字母表（-、_），可切换是否保留 = 填充。",
        CodecMode.Base32 => "RFC 4648 Base32（A~Z、2~7，8 字符一组）。",
        CodecMode.Base58 => "Bitcoin Base58 字母表（不含 0、O、I、l），保留前导零。",
        CodecMode.HexBytes => "文本 ⇄ Hex 字节；可设大写/小写与分隔符。",
        CodecMode.Charset => "按选定字符集在文本与字节之间转换，支持 BOM 与 GB18030。",
        CodecMode.Radix => "2~36 进制互转，支持负数、0x/0b/0o 前缀与 BigInteger 精度。",
        CodecMode.TwosComplement => "定宽补码：8/16/32/64 位，有符号/无符号，大端/小端。",
        CodecMode.IpAddress => "IPv4 ⇄ 32 位整数，区分网络序与主机序。",
        CodecMode.UnixTime => "秒 / 毫秒 / 微秒 / 纳秒与日期互转，可指定 UTC 或本地时区。",
        CodecMode.GuidFormat => "GUID 的 N/D/B/P/X 格式与两种字节序互转。",
        _ => null
    };

    public static bool HasDirection(CodecMode mode) => mode switch
    {
        CodecMode.Radix or CodecMode.GuidFormat => false,
        _ => true
    };

    private static bool IsTextual(CodecMode mode) => mode switch
    {
        CodecMode.Radix or CodecMode.TwosComplement or CodecMode.IpAddress or CodecMode.UnixTime or CodecMode.GuidFormat => false,
        _ => true
    };

    private static CodecResult ApplyMode(string input, CodecOptions options) => options.Mode switch
    {
        CodecMode.UrlComponent => ApplyText(
            () => TextCodecs.UrlEncode(input, options.PlusAsSpace),
            () => TextCodecs.UrlDecode(input, options.PlusAsSpace), options),
        CodecMode.HtmlEntity => ApplyText(
            () => TextCodecs.HtmlEncode(input),
            () => TextCodecs.HtmlDecode(input), options),
        CodecMode.UnicodeEscape => ApplyText(
            () => TextCodecs.UnicodeEncode(input, options.UnicodeStyle),
            () => TextCodecs.UnicodeDecode(input), options),
        CodecMode.JsonEscape => ApplyText(
            () => TextCodecs.JsonEscape(input),
            () => TextCodecs.JsonUnescape(input), options),
        CodecMode.Base64 => ApplyBinary(input,
            s => BinaryCodecs.Base64Encode(TextToBytes(s, options.Charset, false), false, options.Padding),
            s => BinaryCodecs.Base64Decode(s, false), options),
        CodecMode.Base64Url => ApplyBinary(input,
            s => BinaryCodecs.Base64Encode(TextToBytes(s, options.Charset, false), true, options.Padding),
            s => BinaryCodecs.Base64Decode(s, true), options),
        CodecMode.Base32 => ApplyBinary(input,
            s => BinaryCodecs.Base32Encode(TextToBytes(s, options.Charset, false)),
            s => BinaryCodecs.Base32Decode(s), options),
        CodecMode.Base58 => ApplyBinary(input,
            s => BinaryCodecs.Base58Encode(TextToBytes(s, options.Charset, false)),
            s => BinaryCodecs.Base58Decode(s), options),
        CodecMode.HexBytes => ApplyBinary(input,
            s => BinaryCodecs.HexEncode(TextToBytes(s, options.Charset, false), options.UpperCaseHex, options.HexSeparator),
            s => BinaryCodecs.HexDecode(s, options.HexSeparator), options),
        CodecMode.Charset => ApplyCharset(input, options),
        CodecMode.Radix => ApplyRadix(input, options),
        CodecMode.TwosComplement => ApplyTwosComplement(input, options),
        CodecMode.IpAddress => ApplyIp(input, options),
        CodecMode.UnixTime => ApplyTime(input, options),
        CodecMode.GuidFormat => ApplyGuid(input),
        _ => throw new CodecException($"未知模式：{options.Mode}")
    };

    private static CodecResult ApplyText(Func<string> encode, Func<string> decode, CodecOptions options)
    {
        string output = options.Direction == CodecDirection.Encode ? encode() : decode();
        return CodecResult.Of(ModeTitle(options.Mode, options.Direction), TrimForDisplay(output), TextViews(output, options.Charset));
    }

    private static CodecResult ApplyBinary(string input, Func<string, string> encode, Func<string, byte[]> decode, CodecOptions options)
    {
        if (options.Direction == CodecDirection.Encode)
        {
            string output = encode(input);
            return CodecResult.Of(ModeTitle(options.Mode, options.Direction), TrimForDisplay(output),
                BinaryViews(TextToBytes(input, options.Charset, false), options.Charset));
        }

        byte[] decoded = decode(input);
        string text = BytesToTextSafe(decoded, options.Charset, out bool fallback);
        string title = ModeTitle(options.Mode, options.Direction) + (fallback ? "（按 Latin-1 显示）" : string.Empty);
        return CodecResult.Of(title, TrimForDisplay(text), BinaryViews(decoded, options.Charset));
    }

    private static CodecResult ApplyCharset(string input, CodecOptions options)
    {
        if (options.Direction == CodecDirection.Encode)
        {
            byte[] bytes = TextToBytes(input, options.Charset, options.IncludeBom);
            return CodecResult.Of(ModeTitle(options.Mode, options.Direction), HexEncode(bytes, options.UpperCaseHex, options.HexSeparator),
                new CodecView("Base64（UTF-8）", BinaryCodecs.Base64Encode(bytes, false, true)),
                new CodecView("Base64url（无填充）", BinaryCodecs.Base64Encode(bytes, true, false)),
                new CodecView("Unicode 码点", TrimForDisplay(DescribeCodePoints(input))),
                new CodecView("字节数", bytes.Length.ToString(CultureInfo.InvariantCulture)));
        }

        byte[] decoded = BinaryCodecs.HexDecode(input, options.HexSeparator);
        string text = BytesToText(decoded, options.Charset);
        return CodecResult.Of(ModeTitle(options.Mode, options.Direction), TrimForDisplay(text), TextViews(text, options.Charset));
    }

    private static CodecResult ApplyRadix(string input, CodecOptions options)
    {
        BigInteger value = ParseRadix(input, options.FromRadix);
        return CodecResult.Of(
            $"{options.FromRadix} 进制 → {options.ToRadix} 进制",
            FormatRadix(value, options.ToRadix),
            NumberCodecs.RadixViews(value));
    }

    private static CodecResult ApplyTwosComplement(string input, CodecOptions options)
    {
        if (options.Direction == CodecDirection.Encode)
        {
            BigInteger value = ParseRadix(input, options.FromRadix);
            string hex = NumberCodecs.EncodeTwosComplement(value, options.BitWidth, options.Signed, options.ByteOrder);
            return CodecResult.Of(ModeTitle(options.Mode, options.Direction), hex,
                NumberCodecs.TwosComplementViews(value, options.BitWidth, options.Signed, options.ByteOrder));
        }

        BigInteger decoded = NumberCodecs.DecodeTwosComplement(input, options.BitWidth, options.Signed, options.ByteOrder);
        return CodecResult.Of(ModeTitle(options.Mode, options.Direction),
            decoded.ToString(CultureInfo.InvariantCulture),
            NumberCodecs.TwosComplementViews(decoded, options.BitWidth, options.Signed, options.ByteOrder));
    }

    private static CodecResult ApplyIp(string input, CodecOptions options)
    {
        if (options.Direction == CodecDirection.Encode)
        {
            string integer = NumberCodecs.EncodeIp(input);
            return CodecResult.Of(ModeTitle(options.Mode, options.Direction), integer, NumberCodecs.IpViews(input));
        }

        string ipv4 = NumberCodecs.DecodeIp(input);
        return CodecResult.Of(ModeTitle(options.Mode, options.Direction), ipv4, NumberCodecs.IpViews(ipv4));
    }

    private static CodecResult ApplyTime(string input, CodecOptions options)
    {
        if (options.Direction == CodecDirection.Encode)
        {
            DateTimeOffset dto = NumberCodecs.ParseDateTimeOffset(input, options.Utc);
            return CodecResult.Of(ModeTitle(options.Mode, options.Direction),
                NumberCodecs.TimeToEpoch(input, options.TimeUnit, options.Utc), NumberCodecs.TimeViews(dto));
        }

        DateTimeOffset decoded = NumberCodecs.EpochToDateTimeOffset(input, options.TimeUnit);
        return CodecResult.Of(ModeTitle(options.Mode, options.Direction),
            DateTimeText(decoded, options.Utc), NumberCodecs.TimeViews(decoded));
    }

    private static CodecResult ApplyGuid(string input)
    {
        string normalized = NumberCodecs.NormalizeGuid(input);
        return CodecResult.Of("GUID 格式规范化", normalized, NumberCodecs.GuidViews(Guid.Parse(normalized)));
    }

    private static IReadOnlyList<CodecView> TextViews(string text, CharsetName charset)
    {
        byte[] utf8 = TextToBytes(text, CharsetName.Utf8, false);
        var views = new List<CodecView>
        {
            new("UTF-8 字节 Hex", HexEncode(utf8)),
            new("Base64（UTF-8）", BinaryCodecs.Base64Encode(utf8, false, true)),
            new("Unicode 码点", TrimForDisplay(DescribeCodePoints(text)))
        };

        if (charset != CharsetName.Utf8)
        {
            try
            {
                views.Add(new CodecView($"{EncodingName(charset)} 字节 Hex", HexEncode(TextToBytes(text, charset, false))));
            }
            catch (CodecException)
            {
                views.Add(new CodecView($"{EncodingName(charset)} 字节 Hex", "（当前字符集无法表示该文本）"));
            }
        }

        return views;
    }

    private static IReadOnlyList<CodecView> BinaryViews(byte[] bytes, CharsetName charset)
    {
        string text = BytesToTextSafe(bytes, charset, out bool fallback);
        return
        [
            new("字节 Hex", HexEncode(bytes)),
            new("Base64（标准）", BinaryCodecs.Base64Encode(bytes, false, true)),
            new("Base64url（无填充）", BinaryCodecs.Base64Encode(bytes, true, false)),
            new("Unicode 码点" + (fallback ? "（Latin-1 回退）" : string.Empty), TrimForDisplay(DescribeCodePoints(text)))
        ];
    }

    /// <summary>任意二进制不一定是合法文本；解码失败时回退到 Latin-1，避免整个转换失败。</summary>
    private static string BytesToTextSafe(byte[] bytes, CharsetName charset, out bool fallback)
    {
        try
        {
            fallback = false;
            return BytesToText(bytes, charset);
        }
        catch (CodecException)
        {
            fallback = true;
            return System.Text.Encoding.Latin1.GetString(bytes);
        }
    }
}
