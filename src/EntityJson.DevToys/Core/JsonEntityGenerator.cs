using System.Globalization;
using System.Text;
using System.Text.Json;

namespace EntityJson.DevToys.Core;

/// <summary>字段名与 JSON 键不一致时要生成的注解风格（按目标语言选择）。</summary>
public enum AnnotationStyle
{
    /// <summary>不生成注解，属性名保持 JSON 键原样。</summary>
    None,

    /// <summary>C# System.Text.Json：<c>[JsonPropertyName("x")]</c>。</summary>
    SystemTextJson,

    /// <summary>C# Newtonsoft.Json：<c>[JsonProperty("x")]</c>。</summary>
    NewtonsoftJson,

    /// <summary>C# SqlSugar：<c>[SugarColumn(ColumnName = "x")]</c>。</summary>
    SqlSugar,

    /// <summary>Java Jackson：<c>@JsonProperty("x")</c>。</summary>
    Jackson,

    /// <summary>Java Gson：<c>@SerializedName("x")</c>。</summary>
    Gson,

    /// <summary>Java fastjson：<c>@JSONField(name = "x")</c>。</summary>
    FastJson,

    /// <summary>Java MyBatis-Plus：<c>@TableField("x")</c>。</summary>
    MyBatisPlus
}

public sealed class JsonEntityOptions
{
    public SourceLanguage TargetLanguage { get; set; } = SourceLanguage.CSharp;

    public string RootClassName { get; set; } = "Root";

    public FieldNamingPolicy Naming { get; set; } = FieldNamingPolicy.PascalCase;

    public string IndentUnit { get; set; } = "    ";

    /// <summary>字段名与 JSON 键不一致时生成序列化注解。</summary>
    public bool AnnotateRenamed { get; set; } = true;

    /// <summary>注解风格；默认 C# 用 System.Text.Json、Java 用 Jackson，TS 不使用注解。</summary>
    public AnnotationStyle AnnotationStyle { get; set; } = AnnotationStyle.SystemTextJson;
}

public sealed class JsonEntityResult
{
    public JsonEntityResult(string code, IReadOnlyList<Diagnostic> diagnostics, int typeCount, int fieldCount)
    {
        Code = code;
        Diagnostics = diagnostics;
        TypeCount = typeCount;
        FieldCount = fieldCount;
    }

    public string Code { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public int TypeCount { get; }

    public int FieldCount { get; }
}

/// <summary>从一段 JSON 推断结构，生成 C# / TypeScript / Java 实体代码。</summary>
public static class JsonEntityGenerator
{
    public static JsonEntityResult Generate(string? json, JsonEntityOptions options)
    {
        var diagnostics = new List<Diagnostic>();

        if (string.IsNullOrWhiteSpace(json))
        {
            diagnostics.Add(new Diagnostic("JSON 为空。", DiagnosticSeverity.Error));
            return new JsonEntityResult(string.Empty, diagnostics, 0, 0);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException ex)
        {
            // System.Text.Json 的行列是从 0 开始的，这里换成 1 开始，方便对照编辑器
            string position = ex.LineNumber is long line && ex.BytePositionInLine is long column
                ? $"第 {line + 1} 行，第 {column + 1} 列："
                : string.Empty;

            diagnostics.Add(new Diagnostic($"JSON 解析失败（{position}{ex.Message}）", DiagnosticSeverity.Error));
            return new JsonEntityResult(string.Empty, diagnostics, 0, 0);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            var builder = new Builder(options, diagnostics);

            EntityType? rootType = root.ValueKind switch
            {
                JsonValueKind.Object => builder.BuildClass(builder.RootName, root),
                JsonValueKind.Array => builder.BuildRootArray(root),
                _ => null
            };

            if (rootType is null)
            {
                diagnostics.Add(new Diagnostic("根节点必须是 JSON 对象或对象数组。", DiagnosticSeverity.Error));
                return new JsonEntityResult(string.Empty, diagnostics, 0, 0);
            }

            string code = options.TargetLanguage switch
            {
                SourceLanguage.TypeScript => CodeWriter.GenerateTypeScript(builder.Types, rootType, options, diagnostics),
                SourceLanguage.Java => CodeWriter.GenerateJava(builder.Types, rootType, options, diagnostics),
                _ => CodeWriter.GenerateCSharp(builder.Types, rootType, options, diagnostics)
            };

            int fields = builder.Types.Sum(t => t.Fields.Count);
            return new JsonEntityResult(code, diagnostics, builder.Types.Count, fields);
        }
    }

    // ---------------------------------------------------------------- 结构推断

    private sealed class Builder
    {
        private readonly JsonEntityOptions _options;
        private readonly List<Diagnostic> _diagnostics;
        private readonly Dictionary<string, EntityType> _byName = new(StringComparer.Ordinal);

        public Builder(JsonEntityOptions options, List<Diagnostic> diagnostics)
        {
            _options = options;
            _diagnostics = diagnostics;
            RootName = MakeClassName(string.IsNullOrWhiteSpace(options.RootClassName) ? "Root" : options.RootClassName);
        }

        public List<EntityType> Types { get; } = [];

        public string RootName { get; }

        public EntityType BuildClass(string name, JsonElement element)
        {
            var type = new EntityType { Name = EnsureUniqueName(name), Kind = EntityTypeKind.Class };
            _byName[type.Name] = type;
            Types.Add(type);

            foreach (JsonProperty property in element.EnumerateObject())
            {
                AddOrMergeField(type, property.Name, property.Value);
            }

            return type;
        }

        public EntityType? BuildRootArray(JsonElement array)
        {
            if (array.GetArrayLength() == 0)
            {
                _diagnostics.Add(new Diagnostic("根数组为空，已按空对象生成。", DiagnosticSeverity.Warning));
                return CreateEmptyClass(RootName);
            }

            JsonElement first = array[0];

            if (first.ValueKind != JsonValueKind.Object)
            {
                _diagnostics.Add(new Diagnostic("根数组的元素不是对象，已按空对象生成。", DiagnosticSeverity.Warning));
                return CreateEmptyClass(RootName);
            }

            EntityType type = BuildClass(RootName, first);

            for (int i = 1; i < array.GetArrayLength(); i++)
            {
                if (array[i].ValueKind == JsonValueKind.Object)
                {
                    MergeObject(type, array[i]);
                }
            }

            _diagnostics.Add(new Diagnostic("根节点是数组，已按数组元素的形状生成一个类。", DiagnosticSeverity.Warning));
            return type;
        }

        private EntityType CreateEmptyClass(string name)
        {
            var type = new EntityType { Name = EnsureUniqueName(name), Kind = EntityTypeKind.Class };
            _byName[type.Name] = type;
            Types.Add(type);
            return type;
        }

        private void AddOrMergeField(EntityType type, string key, JsonElement value)
        {
            TypeShape incoming = InferType(value, key);
            EntityField? existing = type.Fields.FirstOrDefault(f => string.Equals(f.Name, key, StringComparison.Ordinal));

            if (existing is null)
            {
                type.Fields.Add(new EntityField { Name = key, Type = incoming });
                return;
            }

            existing.Type = MergeShapes(existing.Type, incoming);
        }

        private void MergeObject(EntityType type, JsonElement element)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                AddOrMergeField(type, property.Name, property.Value);
            }
        }

        private TypeShape InferType(JsonElement value, string hint)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    return TypeShape.ObjectOf(BuildClass(ClassHint(hint), value).Name);

                case JsonValueKind.Array:
                    return InferArray(value, hint);

                case JsonValueKind.String:
                    return TypeShape.PrimitiveOf(PrimitiveKind.String);

                case JsonValueKind.Number:
                    return InferNumber(value);

                case JsonValueKind.True:
                case JsonValueKind.False:
                    return TypeShape.PrimitiveOf(PrimitiveKind.Bool);

                case JsonValueKind.Null:
                    TypeShape unknown = TypeShape.UnknownOf(null);
                    unknown.Nullable = true;
                    return unknown;

                default:
                    return TypeShape.UnknownOf(null);
            }
        }

        private TypeShape InferArray(JsonElement array, string hint)
        {
            int length = array.GetArrayLength();

            if (length == 0)
            {
                return TypeShape.ArrayOf(TypeShape.UnknownOf(null));
            }

            List<JsonElement> objects = [];
            List<JsonElement> arrays = [];
            List<PrimitiveKind> numbers = [];
            int strings = 0;
            int bools = 0;
            int nulls = 0;
            int others = 0;

            foreach (JsonElement item in array.EnumerateArray())
            {
                switch (item.ValueKind)
                {
                    case JsonValueKind.Null:
                        nulls++;
                        break;
                    case JsonValueKind.Object:
                        objects.Add(item);
                        break;
                    case JsonValueKind.Array:
                        arrays.Add(item);
                        break;
                    case JsonValueKind.String:
                        strings++;
                        break;
                    case JsonValueKind.Number:
                        numbers.Add(InferNumber(item).Primitive);
                        break;
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        bools++;
                        break;
                    default:
                        others++;
                        break;
                }
            }

            int values = length - nulls;
            TypeShape element;

            if (objects.Count == values)
            {
                EntityType type = BuildClass(ClassHint(hint), objects[0]);
                for (int i = 1; i < objects.Count; i++)
                {
                    MergeObject(type, objects[i]);
                }

                element = TypeShape.ObjectOf(type.Name);
            }
            else if (arrays.Count == values && arrays.Count > 0)
            {
                element = InferArray(arrays[0], hint);
            }
            else if (numbers.Count == values && numbers.Count > 0)
            {
                element = TypeShape.PrimitiveOf(Widen(numbers));
            }
            else if (strings == values)
            {
                element = TypeShape.PrimitiveOf(PrimitiveKind.String);
            }
            else if (bools == values)
            {
                element = TypeShape.PrimitiveOf(PrimitiveKind.Bool);
            }
            else
            {
                _diagnostics.Add(new Diagnostic($"数组「{hint}」的元素类型不一致，已按字符串处理。", DiagnosticSeverity.Warning));
                element = TypeShape.PrimitiveOf(PrimitiveKind.String);
                others = 0;
            }

            _ = others;

            if (nulls > 0)
            {
                element.Nullable = true;
            }

            return TypeShape.ArrayOf(element);
        }

        private static TypeShape InferNumber(JsonElement value)
        {
            if (value.TryGetInt32(out _))
            {
                return TypeShape.PrimitiveOf(PrimitiveKind.Integer);
            }

            if (value.TryGetInt64(out _))
            {
                return TypeShape.PrimitiveOf(PrimitiveKind.Long);
            }

            return TypeShape.PrimitiveOf(PrimitiveKind.Decimal);
        }

        private static PrimitiveKind Widen(List<PrimitiveKind> kinds)
        {
            if (kinds.Any(k => k == PrimitiveKind.Decimal))
            {
                return PrimitiveKind.Decimal;
            }

            return kinds.Any(k => k == PrimitiveKind.Long) ? PrimitiveKind.Long : PrimitiveKind.Integer;
        }

        private static TypeShape MergeShapes(TypeShape left, TypeShape right)
        {
            if (left.Kind == ShapeKind.Primitive && right.Kind == ShapeKind.Primitive)
            {
                if (left.Primitive == right.Primitive)
                {
                    left.Nullable |= right.Nullable;
                    return left;
                }

                if (IsNumeric(left.Primitive) && IsNumeric(right.Primitive))
                {
                    left.Primitive = Widen([left.Primitive, right.Primitive]);
                    left.Nullable |= right.Nullable;
                    return left;
                }
            }

            left.Nullable |= right.Nullable;
            return left;
        }

        private static bool IsNumeric(PrimitiveKind kind)
            => kind is PrimitiveKind.Integer or PrimitiveKind.Long or PrimitiveKind.Decimal or PrimitiveKind.Number;

        private string ClassHint(string propertyName)
        {
            string singular = Singularize(propertyName);
            string pascal = NamingPolicy.Apply(singular, FieldNamingPolicy.PascalCase);
            return MakeClassName(pascal);
        }

        private string EnsureUniqueName(string name)
        {
            if (!_byName.ContainsKey(name) && !Types.Any(t => t.Name == name))
            {
                return name;
            }

            for (int i = 2; ; i++)
            {
                string candidate = name + i;
                if (!_byName.ContainsKey(candidate) && !Types.Any(t => t.Name == candidate))
                {
                    return candidate;
                }
            }
        }

        private static string Singularize(string name)
        {
            if (name.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && name.Length > 3)
            {
                return name[..^3] + "y";
            }

            if (name.EndsWith("ses", StringComparison.OrdinalIgnoreCase) && name.Length > 3)
            {
                return name[..^2];
            }

            if (name.EndsWith('s') && !name.EndsWith("ss", StringComparison.OrdinalIgnoreCase) && name.Length > 1)
            {
                return name[..^1];
            }

            return name;
        }

        private static string MakeClassName(string name)
        {
            string cleaned = Identifier(name, capitalize: true);
            return cleaned.Length == 0 ? "Root" : cleaned;
        }
    }

    // ---------------------------------------------------------------- 代码输出

    private static class CodeWriter
    {
        public static string GenerateCSharp(
            List<EntityType> types,
            EntityType root,
            JsonEntityOptions options,
            List<Diagnostic> diagnostics)
        {
            var usage = new Usage();
            var builder = new StringBuilder();
            var bodies = new List<string>();

            foreach (EntityType type in Order(types, root))
            {
                var body = new StringBuilder();
                body.Append("public class ").Append(type.Name).AppendLine();
                body.AppendLine("{");

                bool annotated = options.AnnotationStyle != AnnotationStyle.None;

                foreach (EntityField field in type.Fields)
                {
                    // 不生成注解时保持 JSON 键原样，避免属性名一变就接不上
                    string property = annotated
                        ? CSharpIdentifier(NamingPolicy.Apply(field.Name, options.Naming))
                        : SanitizeIdentifier(field.Name);
                    if (property.Length == 0)
                    {
                        property = "Field";
                    }

                    string typeName = CSharpType(field.Type, usage);

                    if (options.AnnotateRenamed && annotated && property != field.Name)
                    {
                        usage.AnnotationImport = CSharpAnnotationImport(options.AnnotationStyle);
                        body.Append(options.IndentUnit).Append(CSharpAnnotation(options.AnnotationStyle, field.Name)).AppendLine();
                    }

                    body.Append(options.IndentUnit)
                        .Append("public ")
                        .Append(typeName)
                        .Append(' ')
                        .Append(property)
                        .AppendLine(" { get; set; }");
                }

                body.AppendLine("}");
                bodies.Add(body.ToString().TrimEnd());
            }

            if (usage.System)
            {
                builder.AppendLine("using System;");
            }

            if (usage.Collections)
            {
                builder.AppendLine("using System.Collections.Generic;");
            }

            if (usage.AnnotationImport is not null)
            {
                builder.AppendLine($"using {usage.AnnotationImport};");
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendJoin(Environment.NewLine + Environment.NewLine, bodies);
            return builder.ToString();
        }

        public static string GenerateTypeScript(
            List<EntityType> types,
            EntityType root,
            JsonEntityOptions options,
            List<Diagnostic> diagnostics)
        {
            var bodies = new List<string>();

            foreach (EntityType type in Order(types, root))
            {
                var body = new StringBuilder();
                body.Append("export interface ").Append(type.Name).AppendLine(" {");

                foreach (EntityField field in type.Fields)
                {
                    // TS 的属性名就是 JSON 键，命名策略在这里不生效（改了键就接不上了）
                    string key = IsValidIdentifier(field.Name) ? field.Name : Quote(field.Name);
                    string typeName = TypeScriptType(field.Type);
                    string optional = field.Type.Nullable ? "?" : string.Empty;
                    string nullable = field.Type.Nullable ? " | null" : string.Empty;

                    body.Append(options.IndentUnit)
                        .Append(key)
                        .Append(optional)
                        .Append(": ")
                        .Append(typeName)
                        .Append(nullable)
                        .AppendLine(";");
                }

                body.AppendLine("}");
                bodies.Add(body.ToString().TrimEnd());
            }

            return string.Join(Environment.NewLine + Environment.NewLine, bodies);
        }

        public static string GenerateJava(
            List<EntityType> types,
            EntityType root,
            JsonEntityOptions options,
            List<Diagnostic> diagnostics)
        {
            var usage = new Usage();
            var bodies = new List<string>();
            bool annotated = options.AnnotationStyle != AnnotationStyle.None;

            string FieldName(EntityField field)
            {
                string name = annotated
                    ? JavaIdentifier(NamingPolicy.Apply(field.Name, options.Naming), capitalize: false)
                    : SanitizeIdentifier(field.Name);

                return name.Length == 0 ? "field" : name;
            }

            foreach (EntityType type in Order(types, root))
            {
                var body = new StringBuilder();
                body.Append("public class ").Append(type.Name).AppendLine(" {");

                foreach (EntityField field in type.Fields)
                {
                    string name = FieldName(field);
                    string typeName = JavaType(field.Type, usage);

                    if (options.AnnotateRenamed && annotated && name != field.Name)
                    {
                        usage.AnnotationImport = JavaAnnotationImport(options.AnnotationStyle);
                        body.Append(options.IndentUnit).Append(JavaAnnotation(options.AnnotationStyle, field.Name)).AppendLine();
                    }

                    body.Append(options.IndentUnit)
                        .Append("private ")
                        .Append(typeName)
                        .Append(' ')
                        .Append(name)
                        .AppendLine(";");
                }

                if (type.Fields.Count > 0)
                {
                    body.AppendLine();
                }

                foreach (EntityField field in type.Fields)
                {
                    string name = FieldName(field);
                    string getter = "get" + Capitalize(name);
                    string setter = "set" + Capitalize(name);
                    string typeName = JavaType(field.Type, usage);

                    body.Append(options.IndentUnit)
                        .Append("public ").Append(typeName).Append(' ').Append(getter).Append("() { return ")
                        .Append(name).AppendLine("; }");

                    body.Append(options.IndentUnit)
                        .Append("public void ").Append(setter).Append('(').Append(typeName).Append(' ').Append(name)
                        .Append(") { this.").Append(name).Append(" = ").Append(name).AppendLine("; }");

                    if (!ReferenceEquals(field, type.Fields[^1]))
                    {
                        body.AppendLine();
                    }
                }

                body.AppendLine("}");
                bodies.Add(body.ToString().TrimEnd());
            }

            var header = new StringBuilder();
            if (usage.List)
            {
                header.AppendLine("import java.util.List;");
            }

            if (usage.Map)
            {
                header.AppendLine("import java.util.Map;");
            }

            if (usage.AnnotationImport is not null)
            {
                header.AppendLine($"import {usage.AnnotationImport};");
            }

            if (header.Length > 0)
            {
                header.AppendLine();
            }

            return header.ToString() + string.Join(Environment.NewLine + Environment.NewLine, bodies);
        }

        private static IEnumerable<EntityType> Order(List<EntityType> types, EntityType root)
        {
            yield return root;
            foreach (EntityType type in types)
            {
                if (!ReferenceEquals(type, root))
                {
                    yield return type;
                }
            }
        }

        private static string CSharpType(TypeShape shape, Usage usage)
        {
            string core = shape.Kind switch
            {
                ShapeKind.Primitive => shape.Primitive switch
                {
                    PrimitiveKind.String or PrimitiveKind.Char => "string",
                    PrimitiveKind.Bool => "bool",
                    PrimitiveKind.Integer => "int",
                    PrimitiveKind.Long => "long",
                    PrimitiveKind.Number or PrimitiveKind.Decimal => "double",
                    PrimitiveKind.DateTime => Use("DateTime", usage),
                    PrimitiveKind.Guid => Use("Guid", usage),
                    PrimitiveKind.TimeSpan => Use("TimeSpan", usage),
                    PrimitiveKind.ByteArray => "byte[]",
                    _ => "string"
                },
                ShapeKind.Object => shape.TypeName ?? "object",
                ShapeKind.Array => $"List<{CSharpType(shape.Element ?? TypeShape.UnknownOf(null), usage)}>",
                ShapeKind.Dictionary => $"Dictionary<string, {CSharpType(shape.Value ?? TypeShape.UnknownOf(null), usage)}>",
                ShapeKind.Unknown => "string",
                _ => "string"
            };

            if (shape.Kind is ShapeKind.Array or ShapeKind.Dictionary)
            {
                usage.Collections = true;
            }

            return shape.Nullable ? core + "?" : core;

            static string Use(string type, Usage usage)
            {
                usage.System = true;
                return type;
            }
        }

        private static string TypeScriptType(TypeShape shape)
        {
            return shape.Kind switch
            {
                ShapeKind.Primitive => shape.Primitive switch
                {
                    PrimitiveKind.Bool => "boolean",
                    PrimitiveKind.Integer or PrimitiveKind.Long or PrimitiveKind.Number or PrimitiveKind.Decimal => "number",
                    PrimitiveKind.String or PrimitiveKind.Char or PrimitiveKind.DateTime
                        or PrimitiveKind.Guid or PrimitiveKind.TimeSpan => "string",
                    PrimitiveKind.ByteArray => "string",
                    _ => "string"
                },
                ShapeKind.Object => shape.TypeName ?? "unknown",
                ShapeKind.Array => WrapArray(shape.Element),
                ShapeKind.Dictionary => $"Record<string, {TypeScriptType(shape.Value ?? TypeShape.UnknownOf(null))}>",
                _ => "unknown"
            };

            static string WrapArray(TypeShape? element)
            {
                string inner = TypeScriptType(element ?? TypeShape.UnknownOf(null));
                if (element?.Nullable == true)
                {
                    inner += " | null";
                }

                return inner.Contains(" | ", StringComparison.Ordinal) ? $"({inner})[]" : inner + "[]";
            }
        }

        private static string JavaType(TypeShape shape, Usage usage)
        {
            bool nullable = shape.Nullable;

            if (shape.Kind == ShapeKind.Primitive)
            {
                return shape.Primitive switch
                {
                    PrimitiveKind.String or PrimitiveKind.Char => "String",
                    PrimitiveKind.Bool => nullable ? "Boolean" : "boolean",
                    PrimitiveKind.Integer => nullable ? "Integer" : "int",
                    PrimitiveKind.Long => nullable ? "Long" : "long",
                    PrimitiveKind.Number or PrimitiveKind.Decimal => nullable ? "Double" : "double",
                    PrimitiveKind.DateTime => nullable ? "java.time.LocalDateTime" : "java.time.LocalDateTime",
                    PrimitiveKind.Guid => "java.util.UUID",
                    PrimitiveKind.TimeSpan => "String",
                    PrimitiveKind.ByteArray => "byte[]",
                    _ => "String"
                };
            }

            if (shape.Kind == ShapeKind.Array)
            {
                usage.List = true;
                return $"List<{JavaType(shape.Element ?? TypeShape.UnknownOf(null), usage)}>";
            }

            if (shape.Kind == ShapeKind.Dictionary)
            {
                usage.Map = true;
                return $"Map<String, {JavaType(shape.Value ?? TypeShape.UnknownOf(null), usage)}>";
            }

            if (shape.Kind == ShapeKind.Object && shape.TypeName is string name)
            {
                return name;
            }

            return "Object";
        }

        private static string CSharpAnnotation(AnnotationStyle style, string key) => style switch
        {
            AnnotationStyle.NewtonsoftJson => $"[JsonProperty(\"{Escape(key)}\")]",
            AnnotationStyle.SqlSugar => $"[SugarColumn(ColumnName = \"{Escape(key)}\")]",
            _ => $"[JsonPropertyName(\"{Escape(key)}\")]"
        };

        private static string CSharpAnnotationImport(AnnotationStyle style) => style switch
        {
            AnnotationStyle.NewtonsoftJson => "Newtonsoft.Json",
            AnnotationStyle.SqlSugar => "SqlSugar",
            _ => "System.Text.Json.Serialization"
        };

        private static string JavaAnnotation(AnnotationStyle style, string key) => style switch
        {
            AnnotationStyle.Gson => $"@SerializedName(\"{Escape(key)}\")",
            AnnotationStyle.FastJson => $"@JSONField(name = \"{Escape(key)}\")",
            AnnotationStyle.MyBatisPlus => $"@TableField(\"{Escape(key)}\")",
            _ => $"@JsonProperty(\"{Escape(key)}\")"
        };

        private static string JavaAnnotationImport(AnnotationStyle style) => style switch
        {
            AnnotationStyle.Gson => "com.google.gson.annotations.SerializedName",
            AnnotationStyle.FastJson => "com.alibaba.fastjson.annotation.JSONField",
            AnnotationStyle.MyBatisPlus => "com.baomidou.mybatisplus.annotation.TableField",
            _ => "com.fasterxml.jackson.annotation.JsonProperty"
        };

        /// <summary>只替换非法字符、不改大小写，用于「不生成注解」时保持 JSON 键原样。</summary>
        private static string SanitizeIdentifier(string name)
        {
            var builder = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            }

            string result = builder.ToString();
            if (result.Length == 0)
            {
                return string.Empty;
            }

            return char.IsDigit(result[0]) ? "_" + result : result;
        }

        private static string CSharpIdentifier(string name)
        {
            string cleaned = Identifier(name, capitalize: true);
            return cleaned.Length == 0 ? "Field" : cleaned;
        }

        private static string JavaIdentifier(string name, bool capitalize)
        {
            string cleaned = Identifier(name, capitalize);
            return cleaned.Length == 0 ? "field" : cleaned;
        }

        private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string Quote(string value) => "\"" + Escape(value) + "\"";

        private static string Capitalize(string value)
            => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }

    internal static string Identifier(string name, bool capitalize)
    {
        var builder = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        string result = builder.ToString();
        if (result.Length == 0)
        {
            return string.Empty;
        }

        if (char.IsDigit(result[0]))
        {
            result = "_" + result;
        }

        if (capitalize)
        {
            result = char.ToUpperInvariant(result[0]) + result[1..];
        }
        else
        {
            result = char.ToLowerInvariant(result[0]) + result[1..];
        }

        return result;
    }

    private static bool IsValidIdentifier(string name)
    {
        if (name.Length == 0 || char.IsDigit(name[0]))
        {
            return false;
        }

        return name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '$');
    }

    private sealed class Usage
    {
        public bool Collections { get; set; }

        public bool List { get; set; }

        public bool Map { get; set; }

        /// <summary>注解所在的命名空间（using / import）。</summary>
        public string? AnnotationImport { get; set; }

        public bool System { get; set; }
    }
}
