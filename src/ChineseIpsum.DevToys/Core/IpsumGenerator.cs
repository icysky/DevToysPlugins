using System.Text;

namespace ChineseIpsum.DevToys.Core;

/// <summary>
/// 中文占位文本生成器。语料是四大名著节选，按句子随机拼装。
/// </summary>
internal static class IpsumGenerator
{
    public const int MinLength = 1;
    public const int MaxLength = 5000;

    public static string Generate(IpsumCorpus corpus, IpsumUnit unit, int length, bool startWithOpening)
    {
        IReadOnlyList<string> sentences = IpsumCorpora.GetSentences(corpus);
        if (sentences.Count == 0)
        {
            return string.Empty;
        }

        length = Math.Clamp(length, MinLength, MaxLength);
        Random random = Random.Shared;
        string opening = startWithOpening ? IpsumCorpora.GetOpening(corpus) : string.Empty;

        return unit switch
        {
            IpsumUnit.Sentences => Join(BuildSentences(sentences, length, opening, random)),
            IpsumUnit.Characters => Join(BuildByCharacters(sentences, length, opening, random)),
            _ => BuildParagraphs(sentences, length, opening, random)
        };
    }

    private static List<string> BuildSentences(IReadOnlyList<string> sentences, int count, string opening, Random random)
    {
        List<string> picked = [];
        HashSet<string> used = new(StringComparer.Ordinal);

        if (opening.Length > 0)
        {
            picked.Add(opening);
            used.Add(opening);
        }

        while (picked.Count < count)
        {
            picked.Add(Next(sentences, used, picked, random));
        }

        return [.. picked.Take(count)];
    }

    private static List<string> BuildByCharacters(IReadOnlyList<string> sentences, int count, string opening, Random random)
    {
        List<string> picked = [];
        HashSet<string> used = new(StringComparer.Ordinal);
        int total = 0;

        if (opening.Length > 0)
        {
            picked.Add(opening);
            used.Add(opening);
            total += opening.Length;
        }

        while (total < count)
        {
            string next = Next(sentences, used, picked, random);
            picked.Add(next);
            total += next.Length;
        }

        return picked;
    }

    private static string BuildParagraphs(IReadOnlyList<string> sentences, int paragraphs, string opening, Random random)
    {
        // 开篇句只用于第一段；每段各自维护「已用句」集合，段内不重复。
        string pendingOpening = opening;
        StringBuilder builder = new();

        for (int i = 0; i < paragraphs; i++)
        {
            if (i > 0)
            {
                builder.Append(Environment.NewLine).Append(Environment.NewLine);
            }

            int sentenceCount = random.Next(3, 7);
            List<string> paragraph = [];
            HashSet<string> used = new(StringComparer.Ordinal);

            if (pendingOpening.Length > 0)
            {
                paragraph.Add(pendingOpening);
                used.Add(pendingOpening);
                pendingOpening = string.Empty;
            }

            while (paragraph.Count < sentenceCount)
            {
                paragraph.Add(Next(sentences, used, paragraph, random));
            }

            builder.Append(Join(paragraph));
        }

        return builder.ToString();
    }

    /// <summary>
    /// 取下一句：优先挑「本段/本篇还没用过」的句子；
    /// 语料用尽时清空已用集合再抽，避免出现连续的重复句或早早退化成重复文本。
    /// </summary>
    private static string Next(IReadOnlyList<string> sentences, HashSet<string> used, List<string> current, Random random)
    {
        string? previous = current.Count > 0 ? current[^1] : null;

        for (int attempt = 0; attempt < 32; attempt++)
        {
            string candidate = sentences[random.Next(sentences.Count)];
            if (used.Contains(candidate) || string.Equals(candidate, previous, StringComparison.Ordinal))
            {
                continue;
            }

            used.Add(candidate);
            return candidate;
        }

        // 语料被用光：重新开始一轮。
        used.Clear();
        if (previous is not null)
        {
            used.Add(previous);
        }

        for (int attempt = 0; attempt < 16; attempt++)
        {
            string candidate = sentences[random.Next(sentences.Count)];
            if (!string.Equals(candidate, previous, StringComparison.Ordinal))
            {
                used.Add(candidate);
                return candidate;
            }
        }

        return sentences[random.Next(sentences.Count)];
    }

    /// <summary>中文句子之间不加空格，直接拼接。</summary>
    private static string Join(IEnumerable<string> sentences) => string.Concat(sentences);
}
