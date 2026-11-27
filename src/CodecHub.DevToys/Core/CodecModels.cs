namespace CodecHub.DevToys.Core;

/// <summary>单一工具里的模式切换项；严格限定在「编码 + 进制」范围内。</summary>
public enum CodecMode
{
    UrlComponent,
    HtmlEntity,
    UnicodeEscape,
    JsonEscape,
    Base64,
    Base64Url,
    Base32,
    Base58,
    HexBytes,
    Charset,
    Radix,
    TwosComplement,
    IpAddress,
    UnixTime,
    GuidFormat
}

public enum CodecDirection
{
    Encode,
    Decode
}

public enum CharsetName
{
    Utf8,
    Gb18030,
    Utf16Le,
    Utf16Be,
    Ascii
}

public enum TextEscapeStyle
{
    /// <summary>ASCII 原样，非 ASCII 用 \uXXXX / 代理对。</summary>
    Universal,
    /// <summary>0x00~0xFF 用 \xNN，其余用 \uXXXX。</summary>
    HexEscape,
    /// <summary>U+XXXX / U+XXXXX 码点写法。</summary>
    CodePoint
}

public enum ByteOrder
{
    Big,
    Little
}

/// <summary>所有选项都是 init 属性，UI 里可以用 with 表达式派生。</summary>
public sealed record CodecOptions
{
    public CodecMode Mode { get; init; } = CodecMode.UrlComponent;
    public CodecDirection Direction { get; init; } = CodecDirection.Encode;
    public CharsetName Charset { get; init; } = CharsetName.Utf8;
    /// <summary>URL 模式下 true = application/x-www-form-urlencoded（空格转 +）。</summary>
    public bool PlusAsSpace { get; init; }
    public bool IncludeBom { get; init; }
    public bool Padding { get; init; } = true;
    public bool UpperCaseHex { get; init; }
    public string HexSeparator { get; init; } = string.Empty;
    public TextEscapeStyle UnicodeStyle { get; init; } = TextEscapeStyle.Universal;
    public int FromRadix { get; init; } = 10;
    public int ToRadix { get; init; } = 16;
    public int BitWidth { get; init; } = 32;
    public bool Signed { get; init; } = true;
    public ByteOrder ByteOrder { get; init; } = ByteOrder.Big;
    /// <summary>s / ms / us / ns。</summary>
    public string TimeUnit { get; init; } = "ms";
    /// <summary>时间戳输入没有显式时区时，true 按 UTC 解释，false 按本机时区。</summary>
    public bool Utc { get; init; }
    /// <summary>对主结果再串一次解码（链式转换）。</summary>
    public CodecMode? FollowUp { get; init; }
}

public sealed record CodecView(string Title, string Text);

public sealed record CodecResult(string PrimaryTitle, string PrimaryText, IReadOnlyList<CodecView> Views)
{
    public static CodecResult Of(string title, string text, params CodecView[] views)
        => new(title, text, views);

    public static CodecResult Of(string title, string text, IReadOnlyList<CodecView> views)
        => new(title, text, views);
}

public sealed class CodecException : Exception
{
    public CodecException(string message) : base(message)
    {
    }
}
