using System.Globalization;
using System.Text;

namespace EntityJson.DevToys.Core;

/// <summary>JSON 输出选项。</summary>
public sealed class JsonRenderOptions
{
    /// <summary>键名两侧的引号，默认双引号。</summary>
    public char KeyQuote { get; set; } = '"';

    /// <summary>字符串值两侧的引号，默认双引号。</summary>
    public char ValueQuote { get; set; } = '"';

    /// <summary>缩进单位；空字符串表示压缩成一行。</summary>
    public string IndentUnit { get; set; } = "  ";

    /// <summary>可空字段是否输出 null（关闭时直接不输出该字段）。</summary>
    public bool IncludeNulls { get; set; } = true;

    /// <summary>数组 / 集合生成几个元素。</summary>
    public int ArrayItemCount { get; set; } = 1;

    /// <summary>嵌套对象最多展开几层。</summary>
    public int MaxDepth { get; set; } = 5;

    /// <summary>字段按 JSON 名排序。</summary>
    public bool SortFields { get; set; }

    /// <summary>采用源码里的字段初始值（仅支持字符串 / 数字 / 布尔 / null）。</summary>
    public bool UseInitialValues { get; set; }

    /// <summary>遵循 [JsonPropertyName] / [JsonProperty] 等注解。</summary>
    public bool UseJsonAttributes { get; set; } = true;

    public FieldNamingPolicy NamingPolicy { get; set; } = FieldNamingPolicy.CamelCase;

    /// <summary>非空时把根对象包一层：{"名字": {...}}。</summary>
    public string? WrapRootName { get; set; }

    /// <summary>根节点输出成数组。</summary>
    public bool RootArray { get; set; }

    internal bool Multiline => IndentUnit.Length > 0;
}

public sealed class JsonRenderResult
{
    public JsonRenderResult(string json, IReadOnlyList<Diagnostic> diagnostics, EntityType? rootType, int fieldCount)
    {
        Json = json;
        Diagnostics = diagnostics;
        RootType = rootType;
        FieldCount = fieldCount;
    }

    public string Json { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public EntityType? RootType { get; }

    public int FieldCount { get; }
}

/// <summary>把实体类型渲染成带占位值的 JSON。</summary>
public static class JsonSampleGenerator
{
    public static JsonRenderResult Generate(
        EntityParseResult model,
        string? rootTypeName,
        JsonRenderOptions options)
    {
        var diagnostics = new List<Diagnostic>(model.Diagnostics);
        EntityType? root = model.FindClass(rootTypeName);

        if (root is null)
        {
            diagnostics.Add(new Diagnostic("没有找到可以导出的类。", DiagnosticSeverity.Error));
            return new JsonRenderResult("{}", diagnostics, null, 0);
        }

        var renderer = new Renderer(model, options, diagnostics);
        string json = renderer.Render(root);

        return new JsonRenderResult(json, diagnostics, root, renderer.FieldCount);
    }

    private sealed class Renderer
    {
        private readonly EntityParseResult _model;
        private readonly JsonRenderOptions _options;
        private readonly List<Diagnostic> _diagnostics;
        private readonly HashSet<string> _warned = new(StringComparer.Ordinal);
        private readonly StringBuilder _builder = new();

        public Renderer(EntityParseResult model, JsonRenderOptions options, List<Diagnostic> diagnostics)
        {
            _model = model;
            _options = options;
            _diagnostics = diagnostics;
        }

        public int FieldCount { get; private set; }

        public string Render(EntityType root)
        {
            var stack = new List<string>();

            if (!string.IsNullOrWhiteSpace(_options.WrapRootName))
            {
                _builder.Append('{');
                NewLine(1);
                AppendKey(_options.WrapRootName!.Trim());
                _builder.Append(_options.Multiline ? ": " : ":");
                WriteObject(root, 1, stack);
                NewLine(0);
                _builder.Append('}');
            }
            else if (_options.RootArray)
            {
                _builder.Append('[');
                NewLine(1);
                WriteObject(root, 1, stack);
                NewLine(0);
                _builder.Append(']');
            }
            else
            {
                WriteObject(root, 0, stack);
            }

            return _builder.ToString();
        }

        private void WriteObject(EntityType type, int depth, List<string> stack)
        {
            if (depth > _options.MaxDepth)
            {
                _builder.Append("{}");
                Warn($"嵌套深度超过 {_options.MaxDepth} 层，已截断（{type.Name}）。");
                return;
            }

            if (stack.Contains(type.Name))
            {
                _builder.Append("{}");
                Warn($"检测到循环引用（{type.Name}），已截断。");
                return;
            }

            List<EntityField> fields = [.. type.Fields.Where(f => !f.IsIgnored && IsIncluded(f))];

            // 序列化后同名时后者覆盖：只保留同名里最后出现的那一个
            if (fields.Count > 1)
            {
                var lastIndex = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < fields.Count; i++)
                {
                    lastIndex[FieldName(fields[i])] = i;
                }

                fields = [.. fields.Where((f, i) => lastIndex[FieldName(f)] == i)];
            }

            if (_options.SortFields)
            {
                fields = [.. fields.OrderBy(FieldName, StringComparer.Ordinal)];
            }

            if (fields.Count == 0)
            {
                _builder.Append("{}");
                return;
            }

            stack = [.. stack, type.Name];

            if (!_options.Multiline)
            {
                _builder.Append('{');
                for (int i = 0; i < fields.Count; i++)
                {
                    if (i > 0)
                    {
                        _builder.Append(',');
                    }

                    AppendKey(FieldName(fields[i]));
                    _builder.Append(':');
                    WriteFieldValue(fields[i], depth + 1, stack);
                }

                _builder.Append('}');
                return;
            }

            _builder.Append('{');

            for (int i = 0; i < fields.Count; i++)
            {
                NewLine(depth + 1);
                AppendKey(FieldName(fields[i]));
                _builder.Append(": ");
                WriteFieldValue(fields[i], depth + 1, stack);

                if (i < fields.Count - 1)
                {
                    _builder.Append(',');
                }
            }

            NewLine(depth);
            _builder.Append('}');
        }

        private void WriteFieldValue(EntityField field, int depth, List<string> stack)
        {
            FieldCount++;

            if (_options.UseInitialValues && TryAppendInitialValue(field.InitialValue))
            {
                return;
            }

            if (field.Type.Nullable)
            {
                _builder.Append("null");
                return;
            }

            WriteShape(field.Type, depth, stack);
        }

        private void WriteShape(TypeShape shape, int depth, List<string> stack)
        {
            switch (shape.Kind)
            {
                case ShapeKind.Primitive:
                    AppendPrimitive(shape.Primitive);
                    return;

                case ShapeKind.Enum:
                    WriteEnum(shape.TypeName);
                    return;

                case ShapeKind.Object:
                    EntityType? target = shape.TypeName is null ? null : _model.FindAnyClass(shape.TypeName);
                    if (target is not null && !target.IsInterface)
                    {
                        WriteObject(target, depth, stack);
                    }
                    else
                    {
                        _builder.Append("{}");
                        Warn($"类型 {shape.TypeName} 不在输入里，已输出空对象。");
                    }

                    return;

                case ShapeKind.Array:
                    WriteArray(shape, depth, stack);
                    return;

                case ShapeKind.Dictionary:
                    _builder.Append('{');
                    if (_options.Multiline)
                    {
                        NewLine(depth + 1);
                    }

                    AppendKey("key");
                    _builder.Append(_options.Multiline ? ": " : ":");
                    WriteShape(shape.Value ?? TypeShape.UnknownOf(null), depth + 1, stack);

                    if (_options.Multiline)
                    {
                        NewLine(depth);
                    }

                    _builder.Append('}');
                    return;

                default:
                    _builder.Append("{}");
                    if (shape.TypeName is not null)
                    {
                        Warn($"无法识别类型 {shape.TypeName}，已输出空对象。");
                    }

                    return;
            }
        }

        private void WriteArray(TypeShape shape, int depth, List<string> stack)
        {
            if (shape.IsByteArray)
            {
                AppendQuoted(string.Empty, _options.ValueQuote);
                return;
            }

            int count = Math.Clamp(_options.ArrayItemCount, 0, 10);
            TypeShape element = shape.Element ?? TypeShape.UnknownOf(null);

            _builder.Append('[');
            bool multiline = _options.Multiline && element.Kind is ShapeKind.Object or ShapeKind.Dictionary;

            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    _builder.Append(',');
                }

                if (multiline)
                {
                    NewLine(depth + 1);
                }

                WriteShape(element, depth + 1, stack);
            }

            if (multiline)
            {
                NewLine(depth);
            }

            _builder.Append(']');
        }

        private void WriteEnum(string? typeName)
        {
            EntityType? enumType = typeName is null ? null : _model.FindEnum(typeName);
            EntityEnumValue? first = enumType?.EnumValues.FirstOrDefault();

            if (first is null)
            {
                AppendQuoted("string", _options.ValueQuote);
                if (typeName is not null)
                {
                    Warn($"找不到枚举 {typeName} 的成员，已用 \"string\" 占位。");
                }

                return;
            }

            string value = _options.UseJsonAttributes && first.JsonName is not null ? first.JsonName : first.Name;
            AppendQuoted(value, _options.ValueQuote);
        }

        private void AppendPrimitive(PrimitiveKind primitive)
        {
            switch (primitive)
            {
                case PrimitiveKind.String:
                    AppendQuoted("string", _options.ValueQuote);
                    break;
                case PrimitiveKind.Char:
                    AppendQuoted("a", _options.ValueQuote);
                    break;
                case PrimitiveKind.Bool:
                    _builder.Append("false");
                    break;
                case PrimitiveKind.Integer:
                case PrimitiveKind.Long:
                case PrimitiveKind.Number:
                case PrimitiveKind.Decimal:
                    _builder.Append('0');
                    break;
                case PrimitiveKind.DateTime:
                    AppendQuoted("2026-01-01T00:00:00", _options.ValueQuote);
                    break;
                case PrimitiveKind.Guid:
                    AppendQuoted("00000000-0000-0000-0000-000000000000", _options.ValueQuote);
                    break;
                case PrimitiveKind.TimeSpan:
                    AppendQuoted("00:00:00", _options.ValueQuote);
                    break;
                case PrimitiveKind.ByteArray:
                    AppendQuoted(string.Empty, _options.ValueQuote);
                    break;
                default:
                    AppendQuoted("string", _options.ValueQuote);
                    break;
            }
        }

        private bool IsIncluded(EntityField field)
            => _options.IncludeNulls || !field.Type.Nullable;

        private string FieldName(EntityField field)
        {
            if (_options.UseJsonAttributes && !string.IsNullOrEmpty(field.JsonName))
            {
                return field.JsonName!;
            }

            return NamingPolicy.Apply(field.Name, _options.NamingPolicy);
        }

        private bool TryAppendInitialValue(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string text = raw.Trim();

            if (text is "null" or "default" || text.StartsWith("default(", StringComparison.Ordinal))
            {
                _builder.Append("null");
                return true;
            }

            if (text is "true" or "false")
            {
                _builder.Append(text);
                return true;
            }

            if (text.StartsWith('"') || text.StartsWith("@\"", StringComparison.Ordinal)
                || text.StartsWith("$\"", StringComparison.Ordinal) || text.StartsWith("$@\"", StringComparison.Ordinal)
                || text.StartsWith("@$\"", StringComparison.Ordinal))
            {
                string? inner = Unquote(text);
                if (inner is null)
                {
                    return false;
                }

                AppendQuoted(inner, _options.ValueQuote);
                return true;
            }

            if (text.StartsWith('\'') && text.EndsWith('\'') && text.Length >= 3)
            {
                AppendQuoted(text[1..^1], _options.ValueQuote);
                return true;
            }

            string number = text.TrimEnd('m', 'M', 'd', 'D', 'f', 'F', 'l', 'L', 'u', 'U');
            if (decimal.TryParse(number, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            {
                _builder.Append(number);
                return true;
            }

            return false;
        }

        private static string? Unquote(string text)
        {
            int firstQuote = text.IndexOf('"');
            int lastQuote = text.LastIndexOf('"');

            if (firstQuote < 0 || lastQuote <= firstQuote)
            {
                return null;
            }

            string inner = text[(firstQuote + 1)..lastQuote];
            bool verbatim = text.Contains('@') || text.Contains("$@") || text.Contains("@$");

            return verbatim
                ? inner.Replace("\"\"", "\"")
                : inner.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n").Replace("\\t", "\t");
        }

        private void AppendKey(string name) => AppendQuoted(name, _options.KeyQuote);

        private void AppendQuoted(string value, char quote)
        {
            _builder.Append(quote);
            foreach (char c in value)
            {
                if (c == quote || c == '\\')
                {
                    _builder.Append('\\');
                }

                _builder.Append(c);
            }

            _builder.Append(quote);
        }

        private void NewLine(int depth)
        {
            if (!_options.Multiline)
            {
                return;
            }

            _builder.AppendLine();
            for (int i = 0; i < depth; i++)
            {
                _builder.Append(_options.IndentUnit);
            }
        }

        private void Warn(string message)
        {
            if (_warned.Add(message))
            {
                _diagnostics.Add(new Diagnostic(message, DiagnosticSeverity.Warning));
            }
        }
    }
}
