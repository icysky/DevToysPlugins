using System.Text;

namespace EntityJson.DevToys.Core;

internal enum CSharpTokenKind
{
    Identifier,
    Number,
    String,
    Char,
    Punctuation,
    EndOfFile
}

internal readonly struct CSharpToken
{
    public CSharpToken(CSharpTokenKind kind, string text, int line)
    {
        Kind = kind;
        Text = text;
        Line = line;
    }

    public CSharpTokenKind Kind { get; }

    public string Text { get; }

    public int Line { get; }
}

/// <summary>轻量词法分析：只为「读实体类声明」服务，不追求完整 C# 语法。</summary>
internal static class CSharpTokenizer
{
    public static List<CSharpToken> Tokenize(string code)
    {
        var tokens = new List<CSharpToken>();
        int i = 0;
        int line = 1;
        int length = code.Length;

        while (i < length)
        {
            char c = code[i];

            if (c == '\n')
            {
                line++;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < length && code[i + 1] == '/')
            {
                while (i < length && code[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < length && code[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < length && !(code[i] == '*' && code[i + 1] == '/'))
                {
                    if (code[i] == '\n')
                    {
                        line++;
                    }

                    i++;
                }

                i = Math.Min(i + 2, length);
                continue;
            }

            if (c == '#')
            {
                while (i < length && code[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            // 字符串（普通 / 逐字 / 内插）
            if (c == '"' || ((c == '@' || c == '$') && IsStringPrefix(code, i)))
            {
                int startLine = line;
                int start = i;
                while (i < length && (code[i] == '@' || code[i] == '$'))
                {
                    i++;
                }

                bool verbatim = code.AsSpan(start, i - start).Contains('@');

                if (i < length && code[i] == '"')
                {
                    i++;
                    while (i < length)
                    {
                        if (verbatim)
                        {
                            if (code[i] == '"')
                            {
                                if (i + 1 < length && code[i + 1] == '"')
                                {
                                    i += 2;
                                    continue;
                                }

                                i++;
                                break;
                            }
                        }
                        else
                        {
                            if (code[i] == '\\' && i + 1 < length)
                            {
                                i += 2;
                                continue;
                            }

                            if (code[i] == '"')
                            {
                                i++;
                                break;
                            }
                        }

                        if (code[i] == '\n')
                        {
                            line++;
                        }

                        i++;
                    }
                }

                tokens.Add(new CSharpToken(CSharpTokenKind.String, code[start..Math.Min(i, length)], startLine));
                continue;
            }

            if (c == '\'')
            {
                int startLine = line;
                int start = i;
                i++;
                while (i < length)
                {
                    if (code[i] == '\\' && i + 1 < length)
                    {
                        i += 2;
                        continue;
                    }

                    if (code[i] == '\'')
                    {
                        i++;
                        break;
                    }

                    i++;
                }

                tokens.Add(new CSharpToken(CSharpTokenKind.Char, code[start..Math.Min(i, length)], startLine));
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < length && char.IsDigit(code[i + 1])))
            {
                int start = i;
                while (i < length && (char.IsLetterOrDigit(code[i]) || code[i] is '.' or '_'))
                {
                    i++;
                }

                tokens.Add(new CSharpToken(CSharpTokenKind.Number, code[start..i], line));
                continue;
            }

            if (char.IsLetter(c) || c == '_' || (c == '@' && i + 1 < length && (char.IsLetter(code[i + 1]) || code[i + 1] == '_')))
            {
                int start = i;
                if (c == '@')
                {
                    i++;
                }

                while (i < length && (char.IsLetterOrDigit(code[i]) || code[i] == '_'))
                {
                    i++;
                }

                tokens.Add(new CSharpToken(CSharpTokenKind.Identifier, code[start..i], line));
                continue;
            }

            if (c == '=' && i + 1 < length && code[i + 1] == '>')
            {
                tokens.Add(new CSharpToken(CSharpTokenKind.Punctuation, "=>", line));
                i += 2;
                continue;
            }

            if (c == ':' && i + 1 < length && code[i + 1] == ':')
            {
                tokens.Add(new CSharpToken(CSharpTokenKind.Punctuation, "::", line));
                i += 2;
                continue;
            }

            tokens.Add(new CSharpToken(CSharpTokenKind.Punctuation, c.ToString(), line));
            i++;
        }

        tokens.Add(new CSharpToken(CSharpTokenKind.EndOfFile, string.Empty, line));
        return tokens;
    }

    private static bool IsStringPrefix(string code, int index)
    {
        int i = index;
        while (i < code.Length && (code[i] == '@' || code[i] == '$'))
        {
            i++;
        }

        return i < code.Length && code[i] == '"';
    }
}

/// <summary>
/// 从 C# 源码里提取类 / record / struct / enum 的字段信息。
/// 只覆盖「实体类」这一子集：属性、字段、record 位置参数、泛型集合、可空、常用序列化注解。
/// </summary>
public static class CSharpEntityParser
{
    public static EntityParseResult Parse(string? code, string? rootTypeName = null)
    {
        var diagnostics = new List<Diagnostic>();

        if (string.IsNullOrWhiteSpace(code))
        {
            return new EntityParseResult([], diagnostics);
        }

        try
        {
            List<CSharpToken> tokens = CSharpTokenizer.Tokenize(code);
            var parser = new Parser(tokens, diagnostics);
            parser.ParseAll();
            ModelResolver.Resolve(parser.Types, diagnostics);
            return new EntityParseResult(parser.Types, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new Diagnostic($"解析失败：{ex.Message}", DiagnosticSeverity.Error));
            return new EntityParseResult([], diagnostics);
        }
    }

    // ---------------------------------------------------------------- 语法解析

    private sealed class Parser
    {
        private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal)
        {
            "public", "private", "protected", "internal", "static", "readonly", "const", "required",
            "virtual", "override", "abstract", "sealed", "partial", "new", "extern", "unsafe", "volatile",
            "async", "ref", "in", "out", "params", "this", "fixed"
        };

        private readonly List<CSharpToken> _tokens;
        private readonly List<Diagnostic> _diagnostics;
        private int _index;
        private int _nestingDepth;

        public Parser(List<CSharpToken> tokens, List<Diagnostic> diagnostics)
        {
            _tokens = tokens;
            _diagnostics = diagnostics;
        }

        public List<EntityType> Types { get; } = [];

        private CSharpToken Current => _tokens[_index];

        private CSharpToken Next => _index + 1 < _tokens.Count ? _tokens[_index + 1] : _tokens[^1];

        private bool AtEnd => Current.Kind == CSharpTokenKind.EndOfFile;

        public void ParseAll()
        {
            var pendingAttributes = new List<AttributeInfo>();

            while (!AtEnd)
            {
                if (IsPunct("["))
                {
                    pendingAttributes.AddRange(ParseAttributes());
                    continue;
                }

                if (IsTypeDeclarationStart())
                {
                    ParseTypeDeclaration(pendingAttributes);
                    pendingAttributes = [];
                    continue;
                }

                _index++;
            }
        }

        // ------------------------------------------------------------ 声明

        private bool IsTypeDeclarationStart()
        {
            if (Current.Kind != CSharpTokenKind.Identifier || !IsTypeKeyword(Current.Text))
            {
                return false;
            }

            int j = _index + 1;
            if (Current.Text == "record" && j < _tokens.Count
                && _tokens[j].Kind == CSharpTokenKind.Identifier
                && _tokens[j].Text is "class" or "struct")
            {
                j++;
            }

            if (j >= _tokens.Count || _tokens[j].Kind != CSharpTokenKind.Identifier)
            {
                return false;
            }

            if (j + 1 >= _tokens.Count)
            {
                return false;
            }

            string afterName = _tokens[j + 1].Text;
            return afterName is "<" or "(" or "{" or ":" or ";" or "where";
        }

        private void ParseTypeDeclaration(List<AttributeInfo> attributes)
        {
            string keyword = Current.Text;
            int line = Current.Line;
            _index++;

            if (keyword == "record" && Current.Kind == CSharpTokenKind.Identifier && Current.Text is "class" or "struct")
            {
                _index++;
            }

            if (Current.Kind != CSharpTokenKind.Identifier)
            {
                return;
            }

            string name = CleanIdentifier(Current.Text);
            _index++;

            bool isEnum = keyword == "enum";
            var type = new EntityType
            {
                Name = name,
                Line = line,
                Kind = isEnum ? EntityTypeKind.Enum : EntityTypeKind.Class,
                IsInterface = keyword == "interface",
                IsNested = _nestingDepth > 0
            };

            // 泛型参数
            if (IsPunct("<"))
            {
                type.GenericParameters.AddRange(ParseGenericParameterNames());
            }

            // record 的位置参数
            if (!isEnum && IsPunct("("))
            {
                ParsePositionalParameters(type);
            }

            // 基类 / 接口
            if (IsPunct(":"))
            {
                type.BaseTypeNames.AddRange(ParseBaseTypeNames());
            }

            // where 约束：丢弃到 { 或 ;
            while (!AtEnd && !IsPunct("{") && !IsPunct(";"))
            {
                _index++;
            }

            _nestingDepth++;

            try
            {
                if (isEnum)
                {
                    if (IsPunct("{"))
                    {
                        ParseEnumBody(type);
                    }
                }
                else if (IsPunct("{"))
                {
                    ParseClassBody(type);
                }
            }
            finally
            {
                _nestingDepth--;
            }

            Types.Add(type);
        }

        private List<string> ParseGenericParameterNames()
        {
            var names = new List<string>();
            int depth = 0;

            while (!AtEnd)
            {
                CSharpToken token = Current;

                if (token.Text == "<")
                {
                    depth++;
                    _index++;
                    continue;
                }

                if (token.Text == ">")
                {
                    depth--;
                    _index++;
                    if (depth == 0)
                    {
                        break;
                    }

                    continue;
                }

                if (depth == 1 && token.Kind == CSharpTokenKind.Identifier)
                {
                    names.Add(CleanIdentifier(token.Text));
                }

                _index++;
            }

            return names;
        }

        private void ParsePositionalParameters(EntityType type)
        {
            _index++; // (

            while (!AtEnd && !IsPunct(")"))
            {
                var attributes = new List<AttributeInfo>();
                while (IsPunct("["))
                {
                    attributes.AddRange(ParseAttributes());
                }

                while (Current.Kind == CSharpTokenKind.Identifier && Modifiers.Contains(Current.Text))
                {
                    _index++;
                }

                TypeShape shape = ParseType(out _);

                if (Current.Kind != CSharpTokenKind.Identifier)
                {
                    SkipToDelimiter(")", ",");
                    continue;
                }

                string name = CleanIdentifier(Current.Text);
                _index++;

                string? initial = null;
                if (IsPunct("="))
                {
                    initial = ReadInitializer();
                }

                type.Fields.Add(CreateField(name, shape, attributes, ["public"], initial, Current.Line));

                if (IsPunct(","))
                {
                    _index++;
                }
            }

            if (IsPunct(")"))
            {
                _index++;
            }

            if (IsPunct(";"))
            {
                _index++;
            }
        }

        private List<string> ParseBaseTypeNames()
        {
            var names = new List<string>();
            _index++; // :

            while (!AtEnd && !IsPunct("{") && !IsPunct(";"))
            {
                if (Current.Kind == CSharpTokenKind.Identifier && Current.Text == "where")
                {
                    break;
                }

                if (Current.Kind == CSharpTokenKind.Identifier)
                {
                    string name = CleanIdentifier(Current.Text);
                    _index++;

                    // global:: 前缀
                    if (name == "global" && IsPunct("::"))
                    {
                        _index++;
                        continue;
                    }

                    // 跳过泛型实参
                    if (IsPunct("<"))
                    {
                        SkipBalanced("<", ">");
                    }

                    if (name is not ("where" or "class" or "struct"))
                    {
                        names.Add(name);
                    }

                    continue;
                }

                _index++;
            }

            return names;
        }

        private void ParseClassBody(EntityType type)
        {
            _index++; // {
            var pendingAttributes = new List<AttributeInfo>();

            while (!AtEnd && !IsPunct("}"))
            {
                if (IsPunct("["))
                {
                    pendingAttributes.AddRange(ParseAttributes());
                    continue;
                }

                if (IsPunct(";"))
                {
                    _index++;
                    pendingAttributes = [];
                    continue;
                }

                if (IsTypeDeclarationStart())
                {
                    ParseTypeDeclaration(pendingAttributes);
                    pendingAttributes = [];
                    continue;
                }

                if (Current.Kind == CSharpTokenKind.Identifier && Current.Text is "using" or "delegate")
                {
                    SkipStatement();
                    pendingAttributes = [];
                    continue;
                }

                var modifierSet = new HashSet<string>(StringComparer.Ordinal);
                bool isEvent = false;
                while (Current.Kind == CSharpTokenKind.Identifier && (Modifiers.Contains(Current.Text) || Current.Text == "event"))
                {
                    if (Current.Text == "event")
                    {
                        isEvent = true;
                    }

                    modifierSet.Add(Current.Text);
                    _index++;
                }

                if (AtEnd || IsPunct("}"))
                {
                    pendingAttributes = [];
                    continue;
                }

                if (IsTypeDeclarationStart())
                {
                    ParseTypeDeclaration(pendingAttributes);
                    pendingAttributes = [];
                    continue;
                }

                if (isEvent)
                {
                    SkipMember();
                    pendingAttributes = [];
                    continue;
                }

                int memberLine = Current.Line;
                TypeShape shape = ParseType(out _);

                if (AtEnd)
                {
                    break;
                }

                if (IsPunct("("))
                {
                    // 构造函数
                    SkipMember();
                    pendingAttributes = [];
                    continue;
                }

                if (Current.Kind != CSharpTokenKind.Identifier || Current.Text == "this")
                {
                    SkipMember();
                    pendingAttributes = [];
                    continue;
                }

                string name = CleanIdentifier(Current.Text);
                _index++;

                if (IsPunct("(") || IsPunct("["))
                {
                    // 方法 / 索引器
                    SkipMember();
                    pendingAttributes = [];
                    continue;
                }

                if (IsPunct("{"))
                {
                    // 属性
                    SkipBalanced("{", "}");
                    string? initial = null;
                    if (IsPunct("="))
                    {
                        initial = ReadInitializer();
                    }
                    else if (IsPunct(";"))
                    {
                        _index++;
                    }

                    type.Fields.Add(CreateField(name, shape, pendingAttributes, [.. modifierSet], initial, memberLine));
                    pendingAttributes = [];
                    continue;
                }

                if (IsPunct("=>"))
                {
                    // 表达式体属性
                    SkipStatement();
                    type.Fields.Add(CreateField(name, shape, pendingAttributes, [.. modifierSet], null, memberLine));
                    pendingAttributes = [];
                    continue;
                }

                // 字段（可能有多个声明符）
                string? fieldInitial = null;
                if (IsPunct("="))
                {
                    fieldInitial = ReadInitializer();
                }

                type.Fields.Add(CreateField(name, shape, pendingAttributes, [.. modifierSet], fieldInitial, memberLine));

                while (IsPunct(","))
                {
                    _index++;
                    if (Current.Kind != CSharpTokenKind.Identifier)
                    {
                        break;
                    }

                    string extraName = CleanIdentifier(Current.Text);
                    _index++;
                    string? extraInitial = null;
                    if (IsPunct("="))
                    {
                        extraInitial = ReadInitializer();
                    }

                    type.Fields.Add(CreateField(extraName, shape, pendingAttributes, [.. modifierSet], extraInitial, memberLine));
                }

                if (IsPunct(";"))
                {
                    _index++;
                }

                pendingAttributes = [];
            }

            if (IsPunct("}"))
            {
                _index++;
            }
        }

        private void ParseEnumBody(EntityType type)
        {
            _index++; // {

            while (!AtEnd && !IsPunct("}"))
            {
                var attributes = new List<AttributeInfo>();
                while (IsPunct("["))
                {
                    attributes.AddRange(ParseAttributes());
                }

                if (Current.Kind != CSharpTokenKind.Identifier)
                {
                    _index++;
                    continue;
                }

                string name = CleanIdentifier(Current.Text);
                _index++;

                string? jsonName = attributes.FirstOrDefault(a =>
                    a.Name is "EnumMember" or "EnumMemberAttribute" && a.FirstArgument is not null)?.FirstArgument;

                if (IsPunct("="))
                {
                    while (!AtEnd && !IsPunct(",") && !IsPunct("}"))
                    {
                        _index++;
                    }
                }

                type.EnumValues.Add(new EntityEnumValue(name, jsonName));

                if (IsPunct(","))
                {
                    _index++;
                }
            }

            if (IsPunct("}"))
            {
                _index++;
            }
        }

        // ------------------------------------------------------------ 成员

        private static EntityField CreateField(
            string name,
            TypeShape shape,
            List<AttributeInfo> attributes,
            string[] modifiers,
            string? initialValue,
            int line)
        {
            string? jsonName = attributes.FirstOrDefault(a =>
                a.Name is "JsonPropertyName" or "JsonPropertyNameAttribute"
                    or "JsonProperty" or "JsonPropertyAttribute"
                && a.FirstArgument is not null)?.FirstArgument;

            var field = new EntityField
            {
                Name = name,
                Type = shape,
                JsonName = jsonName,
                InitialValue = initialValue,
                Line = line
            };

            if (attributes.Any(a => a.Name is "JsonIgnore" or "JsonIgnoreAttribute"))
            {
                field.IsIgnored = true;
                field.IgnoreReason = "[JsonIgnore]";
            }
            else if (modifiers.Contains("static"))
            {
                field.IsIgnored = true;
                field.IgnoreReason = "static";
            }
            else if (modifiers.Contains("const"))
            {
                field.IsIgnored = true;
                field.IgnoreReason = "const";
            }
            else if (!modifiers.Contains("public"))
            {
                field.IsIgnored = true;
                field.IgnoreReason = modifiers.FirstOrDefault(m => m is "private" or "protected" or "internal")
                                     ?? "非 public";
            }

            return field;
        }

        // ------------------------------------------------------------ 类型

        private TypeShape ParseType(out string typeText)
        {
            typeText = string.Empty;
            int start = _index;

            // 元组 / 函数指针等：直接当未知类型跳过
            if (IsPunct("("))
            {
                SkipBalanced("(", ")");
                typeText = JoinTokens(start, _index);
                return TypeShape.UnknownOf(typeText);
            }

            if (Current.Kind != CSharpTokenKind.Identifier)
            {
                _index++;
                return TypeShape.UnknownOf(null);
            }

            string name = CleanIdentifier(Current.Text);
            _index++;

            // global::System.String 这类前缀
            while (IsPunct("::") || IsPunct("."))
            {
                if (Next.Kind == CSharpTokenKind.Identifier)
                {
                    _index++;
                    name = CleanIdentifier(Current.Text);
                    _index++;
                }
                else
                {
                    break;
                }
            }

            var args = new List<TypeShape>();
            if (IsPunct("<"))
            {
                args = ParseTypeArguments();
            }

            TypeShape shape = BuildShape(name, args);

            while (IsPunct("?"))
            {
                shape.Nullable = true;
                _index++;
            }

            while (IsPunct("[") && (Next.Text == "]" || Next.Text == ","))
            {
                // 多维数组按一维处理
                while (IsPunct("[") || IsPunct(",") || IsPunct("]"))
                {
                    bool closed = IsPunct("]");
                    _index++;
                    if (closed)
                    {
                        break;
                    }
                }

                shape = shape.Kind == ShapeKind.Primitive && shape.Primitive == PrimitiveKind.Integer && name is "byte" or "Byte"
                    ? TypeShape.PrimitiveOf(PrimitiveKind.ByteArray)
                    : TypeShape.ArrayOf(shape);
            }

            typeText = JoinTokens(start, _index);
            return shape;
        }

        private List<TypeShape> ParseTypeArguments()
        {
            var args = new List<TypeShape>();
            _index++; // <

            while (!AtEnd && !IsPunct(">"))
            {
                if (IsPunct(","))
                {
                    _index++;
                    continue;
                }

                args.Add(ParseType(out _));

                if (IsPunct(","))
                {
                    _index++;
                }
            }

            if (IsPunct(">"))
            {
                _index++;
            }

            return args;
        }

        private static TypeShape BuildShape(string name, List<TypeShape> args)
        {
            switch (name.ToLowerInvariant())
            {
                case "string":
                case "uri":
                case "jsonstring":
                    return TypeShape.PrimitiveOf(PrimitiveKind.String);
                case "char":
                    return TypeShape.PrimitiveOf(PrimitiveKind.Char);
                case "bool":
                case "boolean":
                    return TypeShape.PrimitiveOf(PrimitiveKind.Bool);
                case "byte":
                case "sbyte":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "int16":
                case "int32":
                case "int64":
                case "uint16":
                case "uint32":
                case "uint64":
                    return TypeShape.PrimitiveOf(PrimitiveKind.Integer);
                case "float":
                case "double":
                case "decimal":
                case "single":
                    return TypeShape.PrimitiveOf(PrimitiveKind.Number);
                case "datetime":
                case "datetimeoffset":
                case "dateonly":
                    return TypeShape.PrimitiveOf(PrimitiveKind.DateTime);
                case "timeonly":
                case "timespan":
                    return TypeShape.PrimitiveOf(PrimitiveKind.TimeSpan);
                case "guid":
                    return TypeShape.PrimitiveOf(PrimitiveKind.Guid);
                case "object":
                case "dynamic":
                case "jsonelement":
                case "jsonnode":
                case "jtoken":
                case "jobject":
                    return TypeShape.UnknownOf(name);
                case "nullable":
                    if (args.Count >= 1)
                    {
                        args[0].Nullable = true;
                        return args[0];
                    }

                    return TypeShape.UnknownOf(name);
                case "task":
                case "valuetask":
                    return args.Count >= 1 ? args[0] : TypeShape.UnknownOf(name);
            }

            if (args.Count >= 1 && name is "List" or "IList" or "ICollection" or "IEnumerable"
                or "IReadOnlyList" or "IReadOnlyCollection" or "HashSet" or "ISet" or "Collection"
                or "IAsyncEnumerable" or "Queue" or "Stack" or "LinkedList")
            {
                return TypeShape.ArrayOf(args[0]);
            }

            if (args.Count >= 2 && name is "Dictionary" or "IDictionary" or "IReadOnlyDictionary"
                or "SortedDictionary" or "ConcurrentDictionary" or "SortedList")
            {
                return TypeShape.DictionaryOf(args[0], args[1]);
            }

            return TypeShape.ObjectOf(name);
        }

        // ------------------------------------------------------------ 属性与工具

        private List<AttributeInfo> ParseAttributes()
        {
            var result = new List<AttributeInfo>();

            while (IsPunct("["))
            {
                _index++; // [
                int bracketDepth = 1;
                int parenDepth = 0;
                string? name = null;
                string? argument = null;
                string? lastIdentifier = null;

                while (!AtEnd && bracketDepth > 0)
                {
                    CSharpToken token = Current;

                    if (token.Text == "[")
                    {
                        bracketDepth++;
                        _index++;
                        continue;
                    }

                    if (token.Text == "]")
                    {
                        bracketDepth--;
                        _index++;
                        if (bracketDepth == 0)
                        {
                            name ??= lastIdentifier;
                            break;
                        }

                        continue;
                    }

                    if (token.Text == "(" || token.Text == ")")
                    {
                        if (token.Text == "(")
                        {
                            // 形如 System.Text.Json.Serialization.JsonPropertyName(...)：取括号前的标识符
                            name ??= lastIdentifier;
                            parenDepth++;
                        }
                        else
                        {
                            parenDepth = Math.Max(0, parenDepth - 1);
                        }

                        _index++;
                        continue;
                    }

                    if (token.Text == "," && bracketDepth == 1 && parenDepth == 0)
                    {
                        name ??= lastIdentifier;
                        if (name is not null)
                        {
                            result.Add(new AttributeInfo(name, argument));
                        }

                        name = null;
                        argument = null;
                        lastIdentifier = null;
                        _index++;
                        continue;
                    }

                    if (token.Kind == CSharpTokenKind.Identifier)
                    {
                        lastIdentifier = token.Text;
                    }
                    else if (argument is null && token.Kind == CSharpTokenKind.String)
                    {
                        argument = Unquote(token.Text);
                    }

                    _index++;
                }

                name ??= lastIdentifier;
                if (name is not null)
                {
                    result.Add(new AttributeInfo(name, argument));
                }
            }

            return result;
        }

        private string? ReadInitializer()
        {
            if (!IsPunct("="))
            {
                return null;
            }

            _index++;
            var builder = new StringBuilder();
            int depth = 0;

            while (!AtEnd)
            {
                if (depth == 0 && (IsPunct(";") || IsPunct(",")))
                {
                    if (IsPunct(";"))
                    {
                        _index++;
                    }

                    break;
                }

                if (IsPunct("(") || IsPunct("[") || IsPunct("{"))
                {
                    depth++;
                }
                else if (IsPunct(")") || IsPunct("]") || IsPunct("}"))
                {
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                }

                builder.Append(Current.Text);
                _index++;
            }

            return builder.Length == 0 ? null : builder.ToString();
        }

        private void SkipMember()
        {
            int before = _index;

            if (IsPunct("("))
            {
                SkipBalanced("(", ")");
            }

            if (IsPunct("{"))
            {
                SkipBalanced("{", "}");
            }
            else if (IsPunct("=>"))
            {
                SkipStatement();
            }
            else if (IsPunct(";"))
            {
                _index++;
            }

            // 保证循环一定有进展，避免遇到无法识别的成员时死循环
            if (_index == before)
            {
                _index++;
            }
        }

        private void SkipStatement()
        {
            while (!AtEnd && !IsPunct(";"))
            {
                if (IsPunct("{"))
                {
                    SkipBalanced("{", "}");
                    continue;
                }

                _index++;
            }

            if (IsPunct(";"))
            {
                _index++;
            }
        }

        private void SkipBalanced(string open, string close)
        {
            if (!IsPunct(open))
            {
                return;
            }

            int depth = 0;
            while (!AtEnd)
            {
                if (IsPunct(open))
                {
                    depth++;
                }
                else if (IsPunct(close))
                {
                    depth--;
                    if (depth == 0)
                    {
                        _index++;
                        return;
                    }
                }

                _index++;
            }
        }

        private void SkipToDelimiter(params string[] delimiters)
        {
            while (!AtEnd)
            {
                foreach (string delimiter in delimiters)
                {
                    if (IsPunct(delimiter))
                    {
                        return;
                    }
                }

                _index++;
            }
        }

        private string JoinTokens(int start, int end)
        {
            var builder = new StringBuilder();
            for (int i = start; i < Math.Min(end, _tokens.Count); i++)
            {
                builder.Append(_tokens[i].Text);
            }

            return builder.ToString();
        }

        private bool IsPunct(string text) => Current.Kind == CSharpTokenKind.Punctuation && Current.Text == text;

        private static bool IsTypeKeyword(string text)
            => text is "class" or "record" or "struct" or "interface" or "enum";

        private static string CleanIdentifier(string text) => text.StartsWith('@') ? text[1..] : text;

        private static string? Unquote(string text)
        {
            int firstQuote = text.IndexOf('"');
            if (firstQuote < 0)
            {
                return null;
            }

            int lastQuote = text.LastIndexOf('"');
            if (lastQuote <= firstQuote)
            {
                return null;
            }

            string inner = text[(firstQuote + 1)..lastQuote];

            if (text.Contains('@'))
            {
                return inner.Replace("\"\"", "\"");
            }

            return inner.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }

    private sealed class AttributeInfo
    {
        public AttributeInfo(string name, string? firstArgument)
        {
            Name = name;
            FirstArgument = firstArgument;
        }

        public string Name { get; }

        public string? FirstArgument { get; }
    }
}
