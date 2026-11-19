namespace EntityJson.DevToys.Core;

/// <summary>
/// 从 Java 源码里提取 class / record / interface / enum 的字段信息。
/// 与 C# / TypeScript 解析器输出同一个 <see cref="EntityParseResult"/>。
/// </summary>
public static class JavaEntityParser
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
            "public", "private", "protected", "static", "final", "abstract", "transient", "volatile",
            "synchronized", "native", "default", "strictfp", "sealed"
        };

        private readonly List<TypeScriptToken> _tokens;
        private readonly List<Diagnostic> _diagnostics;
        private int _index;

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
                if (IsWord("package") || IsWord("import"))
                {
                    SkipToSemicolon();
                    continue;
                }

                if (IsPunct("@"))
                {
                    ParseAnnotations();
                    continue;
                }

                if (IsWord("class") || IsWord("interface") || IsWord("enum") || IsWord("record"))
                {
                    ParseTypeDeclaration();
                    continue;
                }

                _index++;
            }
        }

        // ------------------------------------------------------------ 声明

        private void ParseTypeDeclaration()
        {
            int line = Current.Line;
            string keyword = Current.Text;
            _index++;

            if (Current.Kind != TypeScriptTokenKind.Identifier)
            {
                return;
            }

            var type = new EntityType
            {
                Name = Current.Text,
                Line = line,
                Kind = keyword == "enum" ? EntityTypeKind.Enum : EntityTypeKind.Class,
                IsInterface = keyword == "interface"
            };
            _index++;

            SkipGenericParameters();

            if (keyword == "record" && IsPunct("("))
            {
                ParseRecordComponents(type);
            }

            if (IsWord("extends") || IsWord("implements"))
            {
                ParseBaseList(type);
            }

            if (keyword == "enum")
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

            Types.Add(type);
        }

        private void ParseClassBody(EntityType type)
        {
            _index++; // {
            var getters = new List<(string Property, TypeShape Shape, string? JsonName, bool Ignored)>();

            while (!AtEnd && !IsPunct("}"))
            {
                if (IsPunct(";"))
                {
                    _index++;
                    continue;
                }

                var annotations = ParseAnnotations();

                var modifiers = new HashSet<string>(StringComparer.Ordinal);
                while (Current.Kind == TypeScriptTokenKind.Identifier && Modifiers.Contains(Current.Text))
                {
                    modifiers.Add(Current.Text);
                    _index++;
                }

                if (IsWord("class") || IsWord("interface") || IsWord("enum") || IsWord("record"))
                {
                    ParseTypeDeclaration();
                    continue;
                }

                if (IsPunct("{"))
                {
                    // 初始化块 / static 块
                    SkipBalanced("{", "}");
                    continue;
                }

                if (IsPunct(";"))
                {
                    _index++;
                    continue;
                }

                int memberLine = Current.Line;

                // 构造方法：类名 + (
                if (Current.Kind == TypeScriptTokenKind.Identifier && Current.Text == type.Name && Next.Text == "(")
                {
                    SkipMethod();
                    continue;
                }

                TypeShape shape = ParseType(out _);

                if (AtEnd)
                {
                    break;
                }

                if (Current.Kind != TypeScriptTokenKind.Identifier)
                {
                    SkipMember();
                    continue;
                }

                string name = Current.Text;
                _index++;

                if (IsPunct("("))
                {
                    // 方法：记录 getter，其余忽略
                    if (!modifiers.Contains("private") && !modifiers.Contains("static"))
                    {
                        string? property = GetterPropertyName(name);

                        if (property is not null)
                        {
                            string? jsonName = annotations.FirstOrDefault(a =>
                                a.Name is "JsonProperty" or "SerializedName" or "JSONField"
                                && a.FirstArgument is not null)?.FirstArgument;

                            bool ignored = annotations.Any(a => a.Name == "JsonIgnore");
                            getters.Add((property, shape, jsonName, ignored));
                        }
                    }

                    SkipMethod();
                    continue;
                }

                // 字段（可能多个声明符）
                string? initial = null;
                if (IsPunct("="))
                {
                    initial = ReadInitializer();
                }

                AddField(type, name, shape, annotations, modifiers, initial, memberLine);

                while (IsPunct(","))
                {
                    _index++;
                    if (Current.Kind != TypeScriptTokenKind.Identifier)
                    {
                        break;
                    }

                    string extra = Current.Text;
                    _index++;
                    string? extraInitial = null;
                    if (IsPunct("="))
                    {
                        extraInitial = ReadInitializer();
                    }

                    AddField(type, extra, shape, annotations, modifiers, extraInitial, memberLine);
                }

                if (IsPunct(";"))
                {
                    _index++;
                }
            }

            if (IsPunct("}"))
            {
                _index++;
            }

            // getter 补充：字段没声明但 getter 存在时，按 getter 补一个属性
            foreach (var getter in getters)
            {
                EntityField? field = type.Fields.FirstOrDefault(f =>
                    string.Equals(f.Name, getter.Property, StringComparison.OrdinalIgnoreCase));

                if (field is not null)
                {
                    if (getter.JsonName is not null)
                    {
                        field.JsonName = getter.JsonName;
                    }

                    continue;
                }

                if (getter.Ignored)
                {
                    continue;
                }

                type.Fields.Add(new EntityField
                {
                    Name = getter.Property,
                    Type = getter.Shape,
                    JsonName = getter.JsonName,
                    Line = type.Line
                });
            }
        }

        private void AddField(
            EntityType type,
            string name,
            TypeShape shape,
            List<AnnotationInfo> annotations,
            HashSet<string> modifiers,
            string? initial,
            int line)
        {
            var field = new EntityField
            {
                Name = name,
                Type = shape,
                JsonName = annotations.FirstOrDefault(a =>
                    a.Name is "JsonProperty" or "SerializedName" or "JSONField"
                    && a.FirstArgument is not null)?.FirstArgument,
                InitialValue = initial,
                Line = line
            };

            if (annotations.Any(a => a.Name == "JsonIgnore"))
            {
                field.IsIgnored = true;
                field.IgnoreReason = "[JsonIgnore]";
            }
            else if (modifiers.Contains("static"))
            {
                field.IsIgnored = true;
                field.IgnoreReason = "static";
            }
            else if (modifiers.Contains("transient"))
            {
                field.IsIgnored = true;
                field.IgnoreReason = "transient";
            }

            type.Fields.Add(field);
        }

        private void ParseRecordComponents(EntityType type)
        {
            _index++; // (

            while (!AtEnd && !IsPunct(")"))
            {
                ParseAnnotations();

                while (Current.Kind == TypeScriptTokenKind.Identifier && Modifiers.Contains(Current.Text))
                {
                    _index++;
                }

                TypeShape shape = ParseType(out _);

                if (Current.Kind != TypeScriptTokenKind.Identifier)
                {
                    SkipToDelimiter(")", ",");
                    continue;
                }

                string name = Current.Text;
                _index++;

                type.Fields.Add(new EntityField
                {
                    Name = name,
                    Type = shape,
                    Line = type.Line
                });

                if (IsPunct(","))
                {
                    _index++;
                }
            }

            if (IsPunct(")"))
            {
                _index++;
            }
        }

        private void ParseBaseList(EntityType type)
        {
            while (!AtEnd && !IsPunct("{") && !IsPunct(";"))
            {
                if (IsWord("extends") || IsWord("implements"))
                {
                    _index++;
                    continue;
                }

                if (Current.Kind == TypeScriptTokenKind.Identifier)
                {
                    type.BaseTypeNames.Add(Current.Text);
                    _index++;

                    if (IsPunct("<"))
                    {
                        SkipGenericParameters();
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

        private void ParseEnumBody(EntityType type)
        {
            _index++; // {

            while (!AtEnd && !IsPunct("}"))
            {
                if (IsPunct(";"))
                {
                    // 常量结束后可能还有字段/方法，直接跳到枚举结尾
                    while (!AtEnd && !IsPunct("}"))
                    {
                        _index++;
                    }

                    break;
                }

                var annotations = ParseAnnotations();

                if (Current.Kind != TypeScriptTokenKind.Identifier)
                {
                    _index++;
                    continue;
                }

                string name = Current.Text;
                _index++;

                string? jsonName = annotations.FirstOrDefault(a =>
                    a.Name is "JsonProperty" or "SerializedName" or "JSONField"
                    && a.FirstArgument is not null)?.FirstArgument;

                if (IsPunct("("))
                {
                    SkipBalanced("(", ")");
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

        // ------------------------------------------------------------ 注解

        private List<AnnotationInfo> ParseAnnotations()
        {
            var result = new List<AnnotationInfo>();

            while (IsPunct("@"))
            {
                _index++; // @

                if (Current.Kind != TypeScriptTokenKind.Identifier)
                {
                    continue;
                }

                string name = Current.Text;
                _index++;

                while (IsPunct(".") && Next.Kind == TypeScriptTokenKind.Identifier)
                {
                    _index++;
                    name = Current.Text;
                    _index++;
                }

                string? argument = null;
                if (IsPunct("("))
                {
                    _index++;
                    while (!AtEnd && !IsPunct(")"))
                    {
                        if (argument is null && Current.Kind == TypeScriptTokenKind.String)
                        {
                            argument = Unquote(Current.Text);
                        }

                        if (IsPunct("("))
                        {
                            SkipBalanced("(", ")");
                            continue;
                        }

                        _index++;
                    }

                    if (IsPunct(")"))
                    {
                        _index++;
                    }
                }

                result.Add(new AnnotationInfo(name, argument));
            }

            return result;
        }

        // ------------------------------------------------------------ 类型

        private TypeShape ParseType(out string text)
        {
            text = string.Empty;
            int start = _index;

            if (Current.Kind == TypeScriptTokenKind.Punctuation && Current.Text is "?" or "*")
            {
                _index++;
            }

            if (Current.Kind != TypeScriptTokenKind.Identifier)
            {
                _index++;
                return TypeShape.UnknownOf(null);
            }

            string name = Current.Text;
            _index++;

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

            while (IsPunct("["))
            {
                _index++;
                if (IsPunct("]"))
                {
                    _index++;
                }

                shape = shape.Kind == ShapeKind.Primitive && shape.Primitive == PrimitiveKind.Integer
                                                && name is "byte" or "Byte"
                    ? TypeShape.PrimitiveOf(PrimitiveKind.ByteArray)
                    : TypeShape.ArrayOf(shape);
            }

            text = JoinTokens(start, _index);
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

                if (IsPunct("?"))
                {
                    _index++;
                    if (IsWord("extends") || IsWord("super"))
                    {
                        _index++;
                    }
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
            TypeShape First() => args.Count >= 1 ? args[0] : TypeShape.UnknownOf(name);

            switch (name)
            {
                case "String":
                case "CharSequence":
                case "StringBuilder":
                case "StringBuffer":
                    return TypeShape.PrimitiveOf(PrimitiveKind.String);
                case "char":
                case "Character":
                    return TypeShape.PrimitiveOf(PrimitiveKind.Char);
                case "boolean":
                case "Boolean":
                case "AtomicBoolean":
                    return TypeShape.PrimitiveOf(PrimitiveKind.Bool);
                case "byte":
                case "short":
                case "int":
                case "long":
                case "Byte":
                case "Short":
                case "Integer":
                case "Long":
                case "BigInteger":
                case "AtomicInteger":
                case "AtomicLong":
                    return TypeShape.PrimitiveOf(PrimitiveKind.Integer);
                case "float":
                case "double":
                case "Float":
                case "Double":
                case "BigDecimal":
                case "Number":
                    return TypeShape.PrimitiveOf(PrimitiveKind.Number);
                case "Date":
                case "LocalDate":
                case "LocalDateTime":
                case "Instant":
                case "ZonedDateTime":
                case "OffsetDateTime":
                case "Timestamp":
                    return TypeShape.PrimitiveOf(PrimitiveKind.DateTime);
                case "LocalTime":
                    return TypeShape.PrimitiveOf(PrimitiveKind.TimeSpan);
                case "UUID":
                    return TypeShape.PrimitiveOf(PrimitiveKind.Guid);
                case "Object":
                    return TypeShape.UnknownOf(name);
            }

            if (args.Count >= 1 && name is "List" or "ArrayList" or "LinkedList" or "Set" or "HashSet"
                or "LinkedHashSet" or "Collection" or "Iterable" or "Queue" or "Deque" or "Stream")
            {
                return TypeShape.ArrayOf(First());
            }

            if (args.Count >= 2 && name is "Map" or "HashMap" or "LinkedHashMap" or "TreeMap"
                or "ConcurrentHashMap" or "Properties")
            {
                return TypeShape.DictionaryOf(args[0], args[1]);
            }

            if (args.Count >= 1 && name is "Optional" or "OptionalInt" or "OptionalLong" or "OptionalDouble")
            {
                TypeShape inner = First();
                inner.Nullable = true;
                return inner;
            }

            return TypeShape.ObjectOf(name);
        }

        // ------------------------------------------------------------ 工具

        private static string? GetterPropertyName(string methodName)
        {
            if (methodName.Length > 3 && methodName.StartsWith("get", StringComparison.Ordinal)
                && methodName != "getClass")
            {
                return Decapitalize(methodName[3..]);
            }

            if (methodName.Length > 2 && methodName.StartsWith("is", StringComparison.Ordinal))
            {
                return Decapitalize(methodName[2..]);
            }

            return null;
        }

        private static string Decapitalize(string name)
        {
            if (name.Length == 0)
            {
                return name;
            }

            if (name.Length > 1 && char.IsUpper(name[0]) && char.IsUpper(name[1]))
            {
                return name;
            }

            return char.ToLowerInvariant(name[0]) + name[1..];
        }

        private void SkipMethod()
        {
            if (IsPunct("("))
            {
                SkipBalanced("(", ")");
            }

            while (!AtEnd && IsPunct("["))
            {
                SkipBalanced("[", "]");
            }

            SkipToDelimiter(";", "{", "}");

            if (IsPunct("{"))
            {
                SkipBalanced("{", "}");
            }
            else if (IsPunct(";"))
            {
                _index++;
            }
        }

        private void SkipMember()
        {
            int before = _index;
            SkipToDelimiter(";", "}");
            if (IsPunct(";"))
            {
                _index++;
            }

            if (_index == before)
            {
                _index++;
            }
        }

        private void SkipToSemicolon()
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

        private void SkipGenericParameters()
        {
            if (!IsPunct("<"))
            {
                return;
            }

            SkipBalanced("<", ">");
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

        private string? ReadInitializer()
        {
            if (!IsPunct("="))
            {
                return null;
            }

            _index++;
            var builder = new System.Text.StringBuilder();
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

        private string JoinTokens(int start, int end)
        {
            var builder = new System.Text.StringBuilder();
            for (int i = start; i < Math.Min(end, _tokens.Count); i++)
            {
                builder.Append(_tokens[i].Text);
            }

            return builder.ToString();
        }

        private bool IsPunct(string text)
            => Current.Kind == TypeScriptTokenKind.Punctuation && Current.Text == text;

        private bool IsWord(string text)
            => Current.Kind == TypeScriptTokenKind.Identifier && Current.Text == text;

        private static string Unquote(string text)
        {
            if (text.Length >= 2 && (text[0] == '"' || text[0] == '\'') && text[^1] == text[0])
            {
                return text[1..^1];
            }

            return text;
        }
    }

    private sealed class AnnotationInfo
    {
        public AnnotationInfo(string name, string? firstArgument)
        {
            Name = name;
            FirstArgument = firstArgument;
        }

        public string Name { get; }

        public string? FirstArgument { get; }
    }
}
