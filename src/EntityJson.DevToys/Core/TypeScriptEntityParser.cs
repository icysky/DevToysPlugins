using System.Text;

namespace EntityJson.DevToys.Core;

internal enum TypeScriptTokenKind
{
    Identifier,
    Number,
    String,
    Punctuation,
    EndOfFile
}

internal readonly struct TypeScriptToken
{
    public TypeScriptToken(TypeScriptTokenKind kind, string text, int line)
    {
        Kind = kind;
        Text = text;
        Line = line;
    }

    public TypeScriptTokenKind Kind { get; }

    public string Text { get; }

    public int Line { get; }
}

internal static class TypeScriptTokenizer
{
    public static List<TypeScriptToken> Tokenize(string code)
    {
        var tokens = new List<TypeScriptToken>();
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

            if (c is '"' or '\'' or '`')
            {
                int startLine = line;
                int start = i;
                char quote = c;
                i++;

                while (i < length)
                {
                    if (code[i] == '\\' && i + 1 < length)
                    {
                        i += 2;
                        continue;
                    }

                    if (code[i] == quote)
                    {
                        i++;
                        break;
                    }

                    if (code[i] == '\n')
                    {
                        line++;
                    }

                    i++;
                }

                tokens.Add(new TypeScriptToken(TypeScriptTokenKind.String, code[start..Math.Min(i, length)], startLine));
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < length && char.IsDigit(code[i + 1])))
            {
                int start = i;
                while (i < length && (char.IsLetterOrDigit(code[i]) || code[i] is '.' or '_'))
                {
                    i++;
                }

                tokens.Add(new TypeScriptToken(TypeScriptTokenKind.Number, code[start..i], line));
                continue;
            }

            if (char.IsLetter(c) || c is '_' or '$')
            {
                int start = i;
                while (i < length && (char.IsLetterOrDigit(code[i]) || code[i] is '_' or '$'))
                {
                    i++;
                }

                tokens.Add(new TypeScriptToken(TypeScriptTokenKind.Identifier, code[start..i], line));
                continue;
            }

            if (c == '=' && i + 1 < length && code[i + 1] == '>')
            {
                tokens.Add(new TypeScriptToken(TypeScriptTokenKind.Punctuation, "=>", line));
                i += 2;
                continue;
            }

            tokens.Add(new TypeScriptToken(TypeScriptTokenKind.Punctuation, c.ToString(), line));
            i++;
        }

        tokens.Add(new TypeScriptToken(TypeScriptTokenKind.EndOfFile, string.Empty, line));
        return tokens;
    }
}

/// <summary>
/// 从 TypeScript 源码里提取 interface / type / class / enum 的字段信息。
/// 输出与 C# 解析器相同的 <see cref="EntityParseResult"/>，后续 JSON 生成完全复用。
/// </summary>
public static class TypeScriptEntityParser
{
    public static EntityParseResult Parse(string? code)
    {
        var diagnostics = new List<Diagnostic>();

        if (string.IsNullOrWhiteSpace(code))
        {
            return new EntityParseResult([], diagnostics);
        }

        try
        {
            List<TypeScriptToken> tokens = TypeScriptTokenizer.Tokenize(code);
            var parser = new Parser(tokens, diagnostics);
            parser.ParseAll();
            parser.ApplyAliases();
            ModelResolver.Resolve(parser.Types, diagnostics);
            return new EntityParseResult(parser.Types, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new Diagnostic($"解析失败：{ex.Message}", DiagnosticSeverity.Error));
            return new EntityParseResult([], diagnostics);
        }
    }

    private sealed class Parser
    {
        private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal)
        {
            "public", "private", "protected", "static", "readonly", "abstract", "declare",
            "override", "async", "get", "set"
        };

        private static readonly HashSet<string> Nullish = new(StringComparer.Ordinal) { "null", "undefined", "void" };

        private readonly List<TypeScriptToken> _tokens;
        private readonly List<Diagnostic> _diagnostics;
        private readonly Dictionary<string, TypeShape> _aliases = new(StringComparer.Ordinal);
        private int _index;
        private int _anonymous;

        public Parser(List<TypeScriptToken> tokens, List<Diagnostic> diagnostics)
        {
            _tokens = tokens;
            _diagnostics = diagnostics;
        }

        public List<EntityType> Types { get; } = [];

        private TypeScriptToken Current => _tokens[_index];

        private TypeScriptToken Next => _index + 1 < _tokens.Count ? _tokens[_index + 1] : _tokens[^1];

        private bool AtEnd => Current.Kind == TypeScriptTokenKind.EndOfFile;

        public void ParseAll()
        {
            while (!AtEnd)
            {
                if (IsWord("export") || IsWord("default") || IsWord("declare") || IsWord("abstract") || IsWord("const"))
                {
                    _index++;
                    continue;
                }

                if (IsWord("interface"))
                {
                    ParseInterface();
                    continue;
                }

                if (IsWord("class"))
                {
                    ParseClass();
                    continue;
                }

                if (IsWord("enum"))
                {
                    ParseEnum();
                    continue;
                }

                if (IsWord("type") && Next.Kind == TypeScriptTokenKind.Identifier)
                {
                    ParseTypeAlias();
                    continue;
                }

                if (IsWord("namespace") || IsWord("module"))
                {
                    SkipStatementOrBlock();
                    continue;
                }

                _index++;
            }
        }

        public void ApplyAliases()
        {
            foreach (EntityType type in Types)
            {
                foreach (EntityField field in type.Fields)
                {
                    field.Type = Resolve(field.Type, []);
                }
            }

            TypeShape Resolve(TypeShape shape, HashSet<string> visiting)
            {
                if (shape.Kind == ShapeKind.Object && shape.TypeName is string name
                    && _aliases.TryGetValue(name, out TypeShape? alias)
                    && visiting.Add(name))
                {
                    TypeShape expanded = alias.CloneShape();
                    expanded.Nullable |= shape.Nullable;
                    return Resolve(expanded, visiting);
                }

                if (shape.Element is not null)
                {
                    shape.Element = Resolve(shape.Element, visiting);
                }

                if (shape.Key is not null)
                {
                    shape.Key = Resolve(shape.Key, visiting);
                }

                if (shape.Value is not null)
                {
                    shape.Value = Resolve(shape.Value, visiting);
                }

                return shape;
            }
        }

        // ------------------------------------------------------------ 声明

        private void ParseInterface()
        {
            int line = Current.Line;
            _index++; // interface

            if (Current.Kind != TypeScriptTokenKind.Identifier)
            {
                return;
            }

            // TS 的 interface 就是实体形状（不是 C# 那种不可实例化的接口），
            // 所以不能标记成 IsInterface，否则它会从根类型下拉里消失。
            var type = new EntityType
            {
                Name = Current.Text,
                Line = line,
                Kind = EntityTypeKind.Class
            };
            _index++;

            SkipGenericParameters();

            if (IsWord("extends"))
            {
                _index++;
                ParseBaseList(type);
            }

            if (IsPunct("{"))
            {
                ParseMembers(type);
            }

            Types.Add(type);
        }

        private void ParseClass()
        {
            int line = Current.Line;
            _index++; // class

            if (Current.Kind != TypeScriptTokenKind.Identifier)
            {
                return;
            }

            var type = new EntityType
            {
                Name = Current.Text,
                Line = line,
                Kind = EntityTypeKind.Class
            };
            _index++;

            SkipGenericParameters();

            if (IsWord("extends"))
            {
                _index++;
                ParseBaseList(type);
            }

            if (IsWord("implements"))
            {
                _index++;
                ParseBaseList(type);
            }

            if (IsPunct("{"))
            {
                ParseMembers(type);
            }

            Types.Add(type);
        }

        private void ParseEnum()
        {
            int line = Current.Line;
            _index++; // enum

            if (Current.Kind != TypeScriptTokenKind.Identifier)
            {
                return;
            }

            var type = new EntityType
            {
                Name = Current.Text,
                Line = line,
                Kind = EntityTypeKind.Enum
            };
            _index++;

            if (!IsPunct("{"))
            {
                Types.Add(type);
                return;
            }

            _index++;

            while (!AtEnd && !IsPunct("}"))
            {
                if (Current.Kind != TypeScriptTokenKind.Identifier && Current.Kind != TypeScriptTokenKind.String)
                {
                    _index++;
                    continue;
                }

                string name = UnquoteIdentifier(Current.Text);
                _index++;

                string? jsonName = null;
                if (IsPunct("="))
                {
                    _index++;
                    if (Current.Kind == TypeScriptTokenKind.String)
                    {
                        jsonName = UnquoteString(Current.Text);
                    }

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

            Types.Add(type);
        }

        private void ParseTypeAlias()
        {
            int line = Current.Line;
            _index++; // type

            if (Current.Kind != TypeScriptTokenKind.Identifier)
            {
                return;
            }

            string name = Current.Text;
            _index++;

            SkipGenericParameters();

            if (!IsPunct("="))
            {
                SkipStatementOrBlock();
                return;
            }

            _index++; // =

            if (IsPunct("{"))
            {
                var type = new EntityType
                {
                    Name = name,
                    Line = line,
                    Kind = EntityTypeKind.Class
                };
                ParseMembers(type);
                Types.Add(type);
            }
            else
            {
                int save = _index;
                if (TryParseStringLiteralUnion(out List<string>? literals))
                {
                    var enumType = new EntityType
                    {
                        Name = name,
                        Line = line,
                        Kind = EntityTypeKind.Enum
                    };

                    foreach (string literal in literals)
                    {
                        enumType.EnumValues.Add(new EntityEnumValue(literal, literal));
                    }

                    Types.Add(enumType);
                }
                else
                {
                    _index = save;
                    _aliases[name] = ParseType();
                }
            }

            if (IsPunct(";"))
            {
                _index++;
            }
        }

        private bool TryParseStringLiteralUnion(out List<string> literals)
        {
            literals = [];
            int save = _index;

            while (!AtEnd)
            {
                if (Current.Kind != TypeScriptTokenKind.String)
                {
                    _index = save;
                    literals = [];
                    return false;
                }

                literals.Add(UnquoteString(Current.Text));
                _index++;

                if (IsPunct("|"))
                {
                    _index++;
                    continue;
                }

                break;
            }

            if (literals.Count == 0)
            {
                _index = save;
                return false;
            }

            return true;
        }

        private void ParseBaseList(EntityType type)
        {
            while (!AtEnd && !IsPunct("{") && !IsPunct(";"))
            {
                if (Current.Kind == TypeScriptTokenKind.Identifier)
                {
                    type.BaseTypeNames.Add(Current.Text);
                    _index++;

                    if (IsPunct("<"))
                    {
                        SkipBalanced("<", ">");
                    }

                    if (IsPunct(","))
                    {
                        _index++;
                    }

                    continue;
                }

                break;
            }
        }

        private void SkipGenericParameters()
        {
            if (!IsPunct("<"))
            {
                return;
            }

            int depth = 0;
            while (!AtEnd)
            {
                if (IsPunct("<"))
                {
                    depth++;
                }
                else if (IsPunct(">"))
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

        // ------------------------------------------------------------ 成员

        private void ParseMembers(EntityType type)
        {
            _index++; // {

            while (!AtEnd && !IsPunct("}"))
            {
                if (IsPunct(";") || IsPunct(","))
                {
                    _index++;
                    continue;
                }

                if (IsPunct("@"))
                {
                    SkipDecorator();
                    continue;
                }

                var modifiers = new HashSet<string>(StringComparer.Ordinal);
                while (Current.Kind == TypeScriptTokenKind.Identifier && Modifiers.Contains(Current.Text))
                {
                    modifiers.Add(Current.Text);
                    _index++;
                }

                if (IsPunct("["))
                {
                    // 索引签名：[key: string]: any;
                    SkipToDelimiter(";", "}");
                    continue;
                }

                if (IsPunct("("))
                {
                    // 调用签名
                    SkipBalanced("(", ")");
                    SkipToDelimiter(";", "{", "}");
                    if (IsPunct("{"))
                    {
                        SkipBalanced("{", "}");
                    }

                    continue;
                }

                if (Current.Kind != TypeScriptTokenKind.Identifier && Current.Kind != TypeScriptTokenKind.String)
                {
                    _index++;
                    continue;
                }

                int line = Current.Line;
                bool quotedName = Current.Kind == TypeScriptTokenKind.String;
                string name = quotedName ? UnquoteString(Current.Text) : Current.Text;
                _index++;

                bool optional = false;
                if (IsPunct("?"))
                {
                    optional = true;
                    _index++;
                }

                if (IsPunct("!") || IsPunct("<"))
                {
                    if (IsPunct("<"))
                    {
                        SkipBalanced("<", ">");
                    }
                    else
                    {
                        _index++;
                    }
                }

                if (IsPunct("("))
                {
                    // 方法
                    SkipBalanced("(", ")");
                    if (IsPunct(":"))
                    {
                        while (!AtEnd && !IsPunct("{") && !IsPunct(";") && !IsPunct("}"))
                        {
                            _index++;
                        }
                    }

                    if (IsPunct("{"))
                    {
                        SkipBalanced("{", "}");
                    }
                    else if (IsPunct(";"))
                    {
                        _index++;
                    }

                    continue;
                }

                TypeShape shape;
                string? initialValue = null;

                if (IsPunct(":"))
                {
                    _index++;
                    shape = ParseType();

                    // 有类型标注时也可能带初始值：public name: string = 'abc';
                    if (IsPunct("="))
                    {
                        initialValue = ReadInitializer();
                    }
                }
                else if (IsPunct("="))
                {
                    initialValue = ReadInitializer();
                    shape = InferType(initialValue);
                }
                else
                {
                    shape = TypeShape.UnknownOf(name);
                }

                if (optional)
                {
                    shape.Nullable = true;
                }

                var field = new EntityField
                {
                    Name = name,
                    Type = shape,
                    JsonName = quotedName ? name : null,
                    InitialValue = initialValue,
                    Line = line
                };

                if (modifiers.Contains("private") || modifiers.Contains("protected"))
                {
                    field.IsIgnored = true;
                    field.IgnoreReason = modifiers.Contains("private") ? "private" : "protected";
                }
                else if (modifiers.Contains("static"))
                {
                    field.IsIgnored = true;
                    field.IgnoreReason = "static";
                }

                type.Fields.Add(field);

                if (IsPunct(";") || IsPunct(","))
                {
                    _index++;
                }
            }

            if (IsPunct("}"))
            {
                _index++;
            }
        }

        private void SkipDecorator()
        {
            _index++; // @
            while (Current.Kind == TypeScriptTokenKind.Identifier || IsPunct("."))
            {
                _index++;
            }

            if (IsPunct("("))
            {
                SkipBalanced("(", ")");
            }
        }

        // ------------------------------------------------------------ 类型

        private TypeShape ParseType()
        {
            TypeShape shape = ParseIntersection();
            bool nullable = false;

            while (IsPunct("|"))
            {
                _index++;
                TypeShape next = ParseIntersection();

                if (IsNullish(next))
                {
                    nullable = true;
                }
                else if (IsNullish(shape))
                {
                    shape = next;
                }
            }

            shape.Nullable |= nullable;
            return shape;
        }

        private TypeShape ParseIntersection()
        {
            TypeShape shape = ParsePrimary();

            while (IsPunct("&"))
            {
                _index++;
                ParsePrimary();
            }

            return shape;
        }

        private TypeShape ParsePrimary()
        {
            if (IsPunct("("))
            {
                _index++;
                TypeShape inner = ParseType();

                if (IsPunct(")"))
                {
                    _index++;
                }

                if (IsPunct("=>"))
                {
                    _index++;
                    ParseType();
                    return TypeShape.UnknownOf("function");
                }

                return inner;
            }

            if (IsPunct("["))
            {
                _index++;
                if (IsPunct("]"))
                {
                    _index++;
                    return TypeShape.ArrayOf(TypeShape.UnknownOf(null));
                }

                TypeShape first = ParseType();
                while (!AtEnd && !IsPunct("]"))
                {
                    _index++;
                }

                if (IsPunct("]"))
                {
                    _index++;
                }

                return TypeShape.ArrayOf(first);
            }

            if (IsPunct("{"))
            {
                return ParseAnonymousObject();
            }

            if (Current.Kind == TypeScriptTokenKind.String)
            {
                _index++;
                return TypeShape.PrimitiveOf(PrimitiveKind.String);
            }

            if (Current.Kind == TypeScriptTokenKind.Number)
            {
                _index++;
                return TypeShape.PrimitiveOf(PrimitiveKind.Number);
            }

            if (Current.Kind == TypeScriptTokenKind.Identifier)
            {
                string name = Current.Text;
                _index++;

                if (name is "readonly" or "keyof" or "typeof" or "infer")
                {
                    return ParseType();
                }

                while (IsPunct(".") && Next.Kind == TypeScriptTokenKind.Identifier)
                {
                    _index++;
                    name = Current.Text;
                    _index++;
                }

                var args = new List<TypeShape>();
                if (IsPunct("<"))
                {
                    args = ParseTypeArguments();
                }

                TypeShape shape = BuildShape(name, args);

                while (IsPunct("[") && Next.Kind != TypeScriptTokenKind.EndOfFile && _tokens[_index + 1].Text == "]")
                {
                    _index += 2;
                    shape = TypeShape.ArrayOf(shape);
                }

                return shape;
            }

            if (!AtEnd)
            {
                _index++;
            }

            return TypeShape.UnknownOf(null);
        }

        private TypeShape ParseAnonymousObject()
        {
            string name = $"__anonymous{++_anonymous}";
            var type = new EntityType
            {
                Name = name,
                Kind = EntityTypeKind.Class,
                IsNested = true,
                Line = Current.Line
            };

            ParseMembers(type);
            Types.Add(type);
            return TypeShape.ObjectOf(name);
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

                args.Add(ParseType());

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
            TypeShape First() => args.Count >= 1 ? args[0] : TypeShape.UnknownOf(name);

            switch (name.ToLowerInvariant())
            {
                case "string":
                    return TypeShape.PrimitiveOf(PrimitiveKind.String);
                case "number":
                    return TypeShape.PrimitiveOf(PrimitiveKind.Number);
                case "bigint":
                    return TypeShape.PrimitiveOf(PrimitiveKind.Integer);
                case "boolean":
                    return TypeShape.PrimitiveOf(PrimitiveKind.Bool);
                case "date":
                    return TypeShape.PrimitiveOf(PrimitiveKind.DateTime);
                case "null":
                case "undefined":
                case "void":
                case "never":
                case "any":
                case "unknown":
                case "object":
                case "symbol":
                case "function":
                    return TypeShape.UnknownOf(name);
            }

            if (args.Count >= 1 && name is "Array" or "ReadonlyArray" or "Set" or "ReadonlySet" or "Iterable")
            {
                return TypeShape.ArrayOf(First());
            }

            if (args.Count >= 2 && name is "Record" or "Map" or "ReadonlyMap" or "WeakMap")
            {
                return TypeShape.DictionaryOf(args[0], args[1]);
            }

            if (args.Count >= 1 && name is "Partial" or "Required" or "Readonly" or "NonNullable"
                or "Promise" or "Awaited" or "Pick" or "Omit")
            {
                return First();
            }

            return TypeShape.ObjectOf(name);
        }

        // ------------------------------------------------------------ 工具

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
                if (depth == 0 && (IsPunct(";") || IsPunct(",") || IsPunct("}")))
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

        private static TypeShape InferType(string? initial)
        {
            if (string.IsNullOrWhiteSpace(initial))
            {
                return TypeShape.UnknownOf(null);
            }

            string text = initial.Trim();

            if (text is "true" or "false")
            {
                return TypeShape.PrimitiveOf(PrimitiveKind.Bool);
            }

            if (text is "null" or "undefined")
            {
                TypeShape nullable = TypeShape.UnknownOf(null);
                nullable.Nullable = true;
                return nullable;
            }

            if (text.StartsWith('"') || text.StartsWith('\'') || text.StartsWith('`'))
            {
                return TypeShape.PrimitiveOf(PrimitiveKind.String);
            }

            if (double.TryParse(text.TrimEnd('n'), System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                return TypeShape.PrimitiveOf(PrimitiveKind.Number);
            }

            return TypeShape.UnknownOf(null);
        }

        private bool IsNullish(TypeShape shape)
            => shape.Kind == ShapeKind.Unknown && shape.TypeName is not null && Nullish.Contains(shape.TypeName);

        private void SkipStatementOrBlock()
        {
            while (!AtEnd && !IsPunct(";") && !IsPunct("{"))
            {
                _index++;
            }

            if (IsPunct("{"))
            {
                SkipBalanced("{", "}");
            }
            else if (IsPunct(";"))
            {
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

        private bool IsPunct(string text)
            => Current.Kind == TypeScriptTokenKind.Punctuation && Current.Text == text;

        private bool IsWord(string text)
            => Current.Kind == TypeScriptTokenKind.Identifier && Current.Text == text;

        private static string UnquoteIdentifier(string text)
            => text.StartsWith('"') || text.StartsWith('\'') ? UnquoteString(text) : text;

        private static string UnquoteString(string text)
        {
            if (text.Length >= 2 && (text[0] == '"' || text[0] == '\'' || text[0] == '`') && text[^1] == text[0])
            {
                return text[1..^1];
            }

            return text;
        }
    }
}
