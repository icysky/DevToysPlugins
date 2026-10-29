using System.Reflection;

namespace ChineseIpsum.DevToys.Core;

/// <summary>语料出处。</summary>
internal enum IpsumCorpus
{
    Sanguo = 0,
    Shuihu = 1,
    Xiyou = 2,
    Honglou = 3,
    Mixed = 4
}

/// <summary>生成单位。</summary>
internal enum IpsumUnit
{
    Paragraphs = 0,
    Sentences = 1,
    Characters = 2
}

/// <summary>
/// 内嵌语料（四大名著节选，公版）。每个文件一行一句，运行时按句子拼装。
/// </summary>
internal static class IpsumCorpora
{
    private const string ResourcePrefix = "ChineseIpsum.DevToys.Assets.Corpus.";

    private static readonly Dictionary<IpsumCorpus, string> ResourceNames = new()
    {
        [IpsumCorpus.Sanguo] = "sanguoyanyi.txt",
        [IpsumCorpus.Shuihu] = "shuihuzhuan.txt",
        [IpsumCorpus.Xiyou] = "xiyouji.txt",
        [IpsumCorpus.Honglou] = "hongloumeng.txt"
    };

    private static readonly Dictionary<IpsumCorpus, string[]> Cache = [];

    // 注意：不能用 .NET 9 才引入的 System.Threading.Lock。
    // DevToys 2.0.9 自带 .NET 8 运行时，`lock (Lock)` 会去加载 Lock.Scope 而抛
    // "Could not load type 'Scope' from assembly 'System.Runtime, Version=8.0.0.0'"。
    private static readonly object Gate = new();

    /// <summary>各书开篇首句（对应 Lorem Ipsum 的 "Lorem ipsum dolor sit amet"）。</summary>
    private static readonly Dictionary<IpsumCorpus, string> Openings = new()
    {
        [IpsumCorpus.Sanguo] = "话说天下大势，分久必合，合久必分。",
        [IpsumCorpus.Shuihu] = "试看书林隐处，几多俊逸儒流。",
        [IpsumCorpus.Xiyou] = "诗曰：混沌未分天地乱，茫茫渺渺无人见。",
        [IpsumCorpus.Honglou] = "此开卷第一回也。"
    };

    public static IReadOnlyList<string> GetSentences(IpsumCorpus corpus)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(corpus, out string[]? cached))
            {
                return cached;
            }

            string[] sentences = corpus == IpsumCorpus.Mixed
                ? [.. ResourceNames.Keys.SelectMany(key => Load(key))]
                : Load(corpus);

            Cache[corpus] = sentences;
            return sentences;
        }
    }

    public static string GetOpening(IpsumCorpus corpus)
    {
        if (corpus == IpsumCorpus.Mixed)
        {
            IpsumCorpus[] all = [IpsumCorpus.Sanguo, IpsumCorpus.Shuihu, IpsumCorpus.Xiyou, IpsumCorpus.Honglou];
            return Openings[all[Random.Shared.Next(all.Length)]];
        }

        return Openings.TryGetValue(corpus, out string? opening) ? opening : string.Empty;
    }

    private static string[] Load(IpsumCorpus corpus)
    {
        // 注意：xiyouji.txt 的文件名在这里集中维护，避免各处硬编码。
        if (!ResourceNames.TryGetValue(corpus, out string? fileName))
        {
            return [];
        }

        Assembly assembly = typeof(IpsumCorpora).Assembly;
        string resourceName = ResourcePrefix + fileName;

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return [];
        }

        using StreamReader reader = new(stream, detectEncodingFromByteOrderMarks: true);

        List<string> sentences = [];
        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                sentences.Add(trimmed);
            }
        }

        return [.. sentences];
    }
}
