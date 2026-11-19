namespace EntityJson.DevToys.Core;

/// <summary>字段类型在 JSON 里的处理方式。</summary>
public enum ShapeKind
{
    /// <summary>基础类型（string / int / bool / DateTime 等）。</summary>
    Primitive,

    /// <summary>引用到输入里的枚举。</summary>
    Enum,

    /// <summary>引用到输入里的类 / record / struct。</summary>
    Object,

    /// <summary>数组、List&lt;T&gt;、IEnumerable&lt;T&gt; 等。</summary>
    Array,

    /// <summary>Dictionary&lt;K,V&gt; 等。</summary>
    Dictionary,

    /// <summary>识别不出具体类型（泛型参数 T、object、dynamic 等），输出 {} 并给警告。</summary>
    Unknown
}

/// <summary>基础类型种类，决定占位值。</summary>
public enum PrimitiveKind
{
    String,
    Char,
    Bool,
    Integer,
    Long,
    Number,
    Decimal,
    DateTime,
    Guid,
    TimeSpan,
    ByteArray
}

/// <summary>一个字段的类型描述（可嵌套集合与可空）。</summary>
public sealed class TypeShape
{
    private TypeShape(ShapeKind kind)
    {
        Kind = kind;
    }

    public ShapeKind Kind { get; private set; }

    public PrimitiveKind Primitive { get; internal set; }

    /// <summary>解析完所有类型后，把「名字恰好是枚举」的对象引用改标成枚举。</summary>
    internal void MarkAsEnum(string name)
    {
        Kind = ShapeKind.Enum;
        TypeName = name;
    }

    /// <summary>对象 / 枚举 / 未知类型在源码里的名字（泛型参数会带尖括号）。</summary>
    public string? TypeName { get; private set; }

    /// <summary>数组元素类型。</summary>
    public TypeShape? Element { get; internal set; }

    /// <summary>字典键类型。</summary>
    public TypeShape? Key { get; internal set; }

    /// <summary>字典值类型。</summary>
    public TypeShape? Value { get; internal set; }

    /// <summary>是否为可空（<c>?</c> / <c>Nullable&lt;T&gt;</c>）。</summary>
    public bool Nullable { get; set; }

    public static TypeShape PrimitiveOf(PrimitiveKind kind)
        => new(ShapeKind.Primitive) { Primitive = kind };

    public static TypeShape EnumOf(string name) => new(ShapeKind.Enum) { TypeName = name };

    public static TypeShape ObjectOf(string name) => new(ShapeKind.Object) { TypeName = name };

    public static TypeShape UnknownOf(string? name) => new(ShapeKind.Unknown) { TypeName = name };

    public static TypeShape ArrayOf(TypeShape element) => new(ShapeKind.Array) { Element = element };

    public static TypeShape DictionaryOf(TypeShape key, TypeShape value)
        => new(ShapeKind.Dictionary) { Key = key, Value = value };

    /// <summary>把 <c>List&lt;byte&gt;</c> 这类也当成字节数组。</summary>
    public bool IsByteArray =>
        Kind == ShapeKind.Array && Element is { Kind: ShapeKind.Primitive, Primitive: PrimitiveKind.ByteArray };

    /// <summary>深拷贝（类型别名展开时用，避免共享可变节点）。</summary>
    internal TypeShape CloneShape()
    {
        return new TypeShape(Kind)
        {
            Primitive = Primitive,
            TypeName = TypeName,
            Element = Element?.CloneShape(),
            Key = Key?.CloneShape(),
            Value = Value?.CloneShape(),
            Nullable = Nullable
        };
    }
}
