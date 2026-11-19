namespace EntityJson.DevToys.Core;

/// <summary>
/// 语言无关的收尾处理：继承字段合并、把「名字是枚举」的对象引用改标成枚举。
/// C# 与 TypeScript 解析器共用。
/// </summary>
internal static class ModelResolver
{
    public static void Resolve(List<EntityType> types, List<Diagnostic> diagnostics)
    {
        ResolveInheritance(types, diagnostics);
        ResolveShapeKinds(types);
    }

    /// <summary>同一份输入里能找到基类就合并字段；派生类同名字段覆盖基类。</summary>
    private static void ResolveInheritance(List<EntityType> types, List<Diagnostic> diagnostics)
    {
        // 接口也参与合并：TS 的 interface Admin extends User 依赖这一步。
        var classes = types
            .Where(t => t.Kind == EntityTypeKind.Class)
            .GroupBy(t => t.Name)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (EntityType type in classes.Values)
        {
            var merged = new List<EntityField>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            Collect(type);
            type.Fields = merged;

            void Collect(EntityType current)
            {
                if (!visited.Add(current.Name))
                {
                    return;
                }

                foreach (string baseName in current.BaseTypeNames)
                {
                    if (classes.TryGetValue(baseName, out EntityType? baseType) && !ReferenceEquals(baseType, current))
                    {
                        Collect(baseType);
                        break;
                    }
                }

                foreach (EntityField field in current.Fields)
                {
                    merged.RemoveAll(f => string.Equals(f.Name, field.Name, StringComparison.Ordinal));
                    merged.Add(field);
                }
            }
        }
    }

    private static void ResolveShapeKinds(List<EntityType> types)
    {
        var enumNames = types
            .Where(t => t.Kind == EntityTypeKind.Enum)
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (EntityType type in types)
        {
            foreach (EntityField field in type.Fields)
            {
                Rewrite(field.Type);
            }
        }

        void Rewrite(TypeShape shape)
        {
            if (shape.Kind == ShapeKind.Object && shape.TypeName is string name && enumNames.Contains(name))
            {
                shape.MarkAsEnum(name);
            }

            if (shape.Element is not null)
            {
                Rewrite(shape.Element);
            }

            if (shape.Value is not null)
            {
                Rewrite(shape.Value);
            }
        }
    }
}
