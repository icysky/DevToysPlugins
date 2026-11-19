namespace EntityJson.DevToys.Core;

/// <summary>输入源码 / 输出代码的语言。</summary>
public enum SourceLanguage
{
    CSharp,
    TypeScript,
    Java
}

/// <summary>按语言选择解析器，输出统一的 <see cref="EntityParseResult"/>。</summary>
public static class EntityParser
{
    public static EntityParseResult Parse(SourceLanguage language, string? code)
    {
        return language switch
        {
            SourceLanguage.TypeScript => TypeScriptEntityParser.Parse(code),
            SourceLanguage.Java => JavaEntityParser.Parse(code),
            _ => CSharpEntityParser.Parse(code)
        };
    }

    public static string DisplayName(SourceLanguage language) => language switch
    {
        SourceLanguage.TypeScript => "TypeScript",
        SourceLanguage.Java => "Java",
        _ => "C#"
    };
}
