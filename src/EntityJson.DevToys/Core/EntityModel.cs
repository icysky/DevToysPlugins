namespace EntityJson.DevToys.Core;

public enum EntityTypeKind
{
    Class,
    Enum
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record Diagnostic(string Message, DiagnosticSeverity Severity, int Line = 0);

/// <summary>一个成员（属性或字段）。</summary>
public sealed class EntityField
{
    public required string Name { get; init; }

    /// <summary>序列化注解里给出的名字，例如 <c>[JsonPropertyName("user_name")]</c>。</summary>
    public string? JsonName { get; set; }

    public required TypeShape Type { get; set; }

    /// <summary>源码里的初始值原文（只保留字面量）。</summary>
    public string? InitialValue { get; set; }

    public bool IsIgnored { get; set; }

    /// <summary>被忽略的原因：static / const / private / [JsonIgnore] 等。</summary>
    public string? IgnoreReason { get; set; }

    public int Line { get; init; }
}

/// <summary>枚举成员。</summary>
public sealed record EntityEnumValue(string Name, string? JsonName);

/// <summary>解析出的类型（类 / record / struct / 枚举）。</summary>
public sealed class EntityType
{
    public required string Name { get; init; }

    public EntityTypeKind Kind { get; set; }

    public bool IsInterface { get; set; }

    /// <summary>是否声明在另一个类型内部（根类型默认不选嵌套类型）。</summary>
    public bool IsNested { get; set; }

    public int Line { get; init; }

    public List<string> GenericParameters { get; } = [];

    /// <summary>基类 / 接口名（已去掉泛型实参）。</summary>
    public List<string> BaseTypeNames { get; } = [];

    public List<EntityField> Fields { get; set; } = [];

    public List<EntityEnumValue> EnumValues { get; } = [];
}

public sealed class EntityParseResult
{
    public EntityParseResult(IReadOnlyList<EntityType> types, IReadOnlyList<Diagnostic> diagnostics)
    {
        Types = types;
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<EntityType> Types { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>所有类 / record / struct（含匿名对象、接口），按源码顺序排列。</summary>
    public IReadOnlyList<EntityType> AllClasses =>
        [.. Types.Where(t => t.Kind == EntityTypeKind.Class).OrderBy(t => t.Line)];

    /// <summary>
    /// 可以作为根类型的类 / record / struct：不含接口，也不含解析时生成的匿名对象（名字以 __ 开头）。
    /// </summary>
    public IReadOnlyList<EntityType> Classes =>
        [.. AllClasses.Where(t => !t.IsInterface && !t.Name.StartsWith("__", StringComparison.Ordinal))];

    /// <summary>按名字找类，包含匿名对象（供生成器解析嵌套字段使用）。</summary>
    public EntityType? FindAnyClass(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return AllClasses.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// 找根类型：指定名字优先；否则优先选「顶层、且没有被别的类继承」的那个（最派生的叶子类），
    /// 避免默认选中 BaseEntity 这种基类。
    /// </summary>
    public EntityType? FindClass(string? name)
    {
        IReadOnlyList<EntityType> classes = Classes;

        if (!string.IsNullOrEmpty(name))
        {
            return classes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal))
                   ?? classes.FirstOrDefault();
        }

        List<EntityType> topLevel = [.. classes.Where(t => !t.IsNested)];
        List<EntityType> candidates = topLevel.Count > 0 ? topLevel : [.. classes];

        var baseNames = classes
            .SelectMany(t => t.BaseTypeNames)
            .ToHashSet(StringComparer.Ordinal);

        return candidates.FirstOrDefault(t => !baseNames.Contains(t.Name)) ?? candidates.FirstOrDefault();
    }

    public EntityType? FindEnum(string name)
        => Types.FirstOrDefault(t => t.Kind == EntityTypeKind.Enum
                                     && string.Equals(t.Name, name, StringComparison.Ordinal));
}
