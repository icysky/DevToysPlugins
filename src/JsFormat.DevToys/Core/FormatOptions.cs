namespace JsFormat.DevToys.Core;

/// <summary>
/// 一次格式化的选项。取值语义与 Prettier 3 完全一致，只是这里用强类型承载，
/// 免得在 C# 里到处拼 JS 字面量。
/// </summary>
internal sealed class FormatOptions
{
    /// <summary>typescript = TS/TSX（也能解析大部分 JS）、babel = JS + JSX、json。</summary>
    public string Parser { get; init; } = "typescript";

    public int PrintWidth { get; init; } = 80;

    public int TabWidth { get; init; } = 2;

    public bool UseTabs { get; init; }

    public bool Semi { get; init; } = true;

    public bool SingleQuote { get; init; }

    /// <summary>all / es5 / none。</summary>
    public string TrailingComma { get; init; } = "all";

    public bool BracketSpacing { get; init; } = true;

    /// <summary>always / avoid。</summary>
    public string ArrowParens { get; init; } = "always";

    /// <summary>as-needed / consistent / preserve。</summary>
    public string QuoteProps { get; init; } = "as-needed";

    /// <summary>lf / crlf / auto。</summary>
    public string EndOfLine { get; init; } = "lf";
}

/// <summary>格式化结果。<see cref="Success" /> 为 false 时 <see cref="Text" /> 就是给用户看的错误信息。</summary>
internal readonly record struct FormatResult(bool Success, string Text, long ElapsedMs);
