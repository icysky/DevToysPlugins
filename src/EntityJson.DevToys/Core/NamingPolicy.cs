using System.Text;

namespace EntityJson.DevToys.Core;

/// <summary>字段名输出策略。</summary>
public enum FieldNamingPolicy
{
    /// <summary>保持源码里的名字。</summary>
    Keep,

    /// <summary>userName。</summary>
    CamelCase,

    /// <summary>user_name。</summary>
    SnakeCase,

    /// <summary>UserName。</summary>
    PascalCase,

    /// <summary>user-name。</summary>
    KebabCase
}

/// <summary>把 C# 标识符按策略改写。</summary>
public static class NamingPolicy
{
    public static string Apply(string name, FieldNamingPolicy policy)
    {
        if (string.IsNullOrEmpty(name) || policy == FieldNamingPolicy.Keep)
        {
            return name;
        }

        string[] words = SplitWords(name);
        if (words.Length == 0)
        {
            return name;
        }

        return policy switch
        {
            FieldNamingPolicy.CamelCase => LowerFirst(words[0]) + string.Concat(words.Skip(1).Select(Capitalize)),
            FieldNamingPolicy.PascalCase => string.Concat(words.Select(Capitalize)),
            FieldNamingPolicy.SnakeCase => string.Join('_', words.Select(Lower)),
            FieldNamingPolicy.KebabCase => string.Join('-', words.Select(Lower)),
            _ => name
        };
    }

    /// <summary>去掉下划线/连字符，再按大写字母与数字分段；连续大写（URL、ID）视为一个词。</summary>
    private static string[] SplitWords(string name)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];

            if (c is '_' or '-' or ' ')
            {
                Flush();
                continue;
            }

            if (char.IsUpper(c) && current.Length > 0)
            {
                bool previousIsLowerOrDigit = char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]);
                bool nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                bool previousIsUpper = char.IsUpper(name[i - 1]);

                // URLValue → URL + Value；UserName → User + Name
                if (previousIsLowerOrDigit || (previousIsUpper && nextIsLower))
                {
                    Flush();
                }
            }

            current.Append(c);
        }

        Flush();
        return [.. words];

        void Flush()
        {
            if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }
    }

    private static string Capitalize(string word)
        => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();

    private static string Lower(string word) => word.ToLowerInvariant();

    private static string LowerFirst(string word)
        => word.Length == 0 ? word : char.ToLowerInvariant(word[0]) + word[1..].ToLowerInvariant();
}
