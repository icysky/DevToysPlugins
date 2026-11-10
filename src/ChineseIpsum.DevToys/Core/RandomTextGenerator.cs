using System.Text;

namespace ChineseIpsum.DevToys.Core;

/// <summary>文本来源：名著节选 / 纯随机乱文。</summary>
internal enum IpsumMode
{
    Corpus = 0,
    Random = 1
}

/// <summary>标点风格。</summary>
internal enum IpsumPunctuationStyle
{
    /// <summary>中文标点：，。！？；：、（）《》「」“”…·～</summary>
    Chinese = 0,

    /// <summary>西文标点：, . ! ? ; : ( ) [ ] { } " ' @ # % &amp; * + = / \ | …</summary>
    Western = 1,

    /// <summary>两种混着用。</summary>
    Mixed = 2
}

/// <summary>长度按什么计量。</summary>
internal enum IpsumCountMode
{
    /// <summary>按可见字符数（汉字、外文字母、标点都算，空白不算）。</summary>
    Characters = 0,

    /// <summary>按字词数（一个汉字算 1 个词，一个外文单词算 1 个词，标点不算）。</summary>
    Words = 1
}

/// <summary>随机乱文的生成参数。</summary>
internal sealed class RandomTextOptions
{
    public int Length { get; init; } = 300;

    public IpsumCountMode CountMode { get; init; } = IpsumCountMode.Characters;

    /// <summary>混入 CJK 扩展区（Ext A / Ext B / 基本区尾部）的生僻字。</summary>
    public bool IncludeRareCharacters { get; init; }

    /// <summary>按「段落」组织（段间空行）；关闭则是连续的一段。</summary>
    public bool Paragraphs { get; init; } = true;

    /// <summary>每段最少几句（仅 <see cref="Paragraphs" /> 为 true 时生效，闭区间）。</summary>
    public int MinSentencesPerParagraph { get; init; } = 2;

    /// <summary>每段最多几句（仅 <see cref="Paragraphs" /> 为 true 时生效，闭区间）。</summary>
    public int MaxSentencesPerParagraph { get; init; } = 4;

    /// <summary>段与段之间是否空一行（关闭则只用单个换行分隔）。</summary>
    public bool BlankLineBetweenParagraphs { get; init; } = true;

    public bool Punctuation { get; init; } = true;

    /// <summary>标点风格（仅 <see cref="Punctuation" /> 为 true 时生效）。</summary>
    public IpsumPunctuationStyle PunctuationStyle { get; init; } = IpsumPunctuationStyle.Chinese;

    /// <summary>混入英文 / 西里尔 / 希腊文的伪单词（无意义，只求长短与形态多样）。</summary>
    public bool MixedLanguages { get; init; }

    // ---- 以下为"测 web 文字渲染"用的压力项，默认全部关闭 ----

    /// <summary>字符压力分组（Emoji / 日韩 / RTL / 符号 / 全角 / 不可见字符 / 超长串 …）。</summary>
    public IpsumStressFlags Stress { get; init; } = IpsumStressFlags.None;

    public IpsumDensity StressDensity { get; init; } = IpsumDensity.Medium;

    /// <summary>结构化片段分组（URL / 邮箱 / UUID / 版本号 / 日期 / 金额 / 路径）。</summary>
    public IpsumFragmentFlags Fragments { get; init; } = IpsumFragmentFlags.None;

    public IpsumDensity FragmentDensity { get; init; } = IpsumDensity.Medium;

    /// <summary>句中停顿（逗号）的密度。</summary>
    public IpsumDensity PunctuationDensity { get; init; } = IpsumDensity.Medium;

    /// <summary>段落长短倾向（只影响每段句数，仍以「每段句子数」范围为基准）。</summary>
    public IpsumParagraphShape ParagraphShape { get; init; } = IpsumParagraphShape.Mixed;

    /// <summary>段首缩进两个全角空格。</summary>
    public bool IndentFirstLine { get; init; }

    /// <summary>段首列表符号。</summary>
    public IpsumListStyle ListStyle { get; init; } = IpsumListStyle.None;

    /// <summary>段首加引用符号 <c>&gt; </c>。</summary>
    public bool QuoteBlocks { get; init; }

    /// <summary>偶尔把词包成 `code`，观察等宽字体与行高。</summary>
    public bool InlineCode { get; init; }

    /// <summary>外文词前的空格策略。</summary>
    public IpsumSpaceStyle SpaceStyle { get; init; } = IpsumSpaceStyle.Single;
}

/// <summary>
/// 纯规则生成"无意义中文"：随机汉字 + 可选生僻字 + 可选标点 + 可选外文伪单词。
///
/// 不使用任何语料/词表：汉字从 Unicode 区间随机取，外文单词按「辅音+元音」音节拼装，
/// 因此不会有任何语义，只用于验证布局、字号、换行、段落间距等排版效果。
/// </summary>
internal static class RandomTextGenerator
{
    public const int MinLength = 1;

    public const int MaxLength = 5000;

    // 常用汉字：CJK 基本区；生僻字：Ext A、基本区尾部、Ext B（需要代理对）
    private const int CommonStart = 0x4E00;

    private const int CommonEnd = 0x9FA5;

    private static readonly (int Start, int End)[] RareRanges =
    [
        (0x3400, 0x4DB5),   // CJK Ext A
        (0x9FA6, 0x9FFF),   // 基本区尾部
        (0x20000, 0x2A6D6)  // Ext B，BMP 之外
    ];

    // ---- 标点集合：尽量覆盖各类符号，方便观察不同字形的宽度与避头尾 ----
    // 句末集合不含 '；' / ';'（它们是句中分隔符，混进来会让"每段几句"难以判定）
    private static readonly char[] ChineseEndMarks = ['。', '。', '！', '？', '…'];

    // 注意：省略号只归中文集合，避免"西文风格里混进中文标点"（两个集合保持不相交）
    private static readonly char[] WesternEndMarks = ['.', '.', '!', '?'];

    private static readonly char[] ChineseMidMarks = ['，', '、', '；', '：'];

    private static readonly char[] WesternMidMarks = [',', ',', ';', ':', '—', '-'];

    private static readonly char[] ChineseSingleMarks = ['·', '～', '￥', '℃', '±'];

    private static readonly char[] WesternSingleMarks =
        ['@', '#', '%', '&', '*', '+', '=', '<', '>', '/', '\\', '|', '^', '$', '~'];

    private static readonly (char Open, char Close)[] ChinesePairs =
        [('（', '）'), ('《', '》'), ('「', '」'), ('“', '”'), ('【', '】')];

    private static readonly (char Open, char Close)[] WesternPairs =
        [('(', ')'), ('[', ']'), ('{', '}'), ('"', '"'), ('\'', '\'')];

    private static readonly string[] LatinConsonants =
        ["b", "c", "d", "f", "g", "h", "j", "k", "l", "m", "n", "p", "r", "s", "t", "v", "w", "x", "y", "z",
         "ch", "sh", "th", "tr", "st", "pl", "gr", "br"];

    private static readonly string[] LatinVowels =
        ["a", "e", "i", "o", "u", "ai", "ea", "oo", "ou", "ia"];

    private static readonly string[] CyrillicConsonants =
        ["б", "в", "г", "д", "ж", "з", "к", "л", "м", "н", "п", "р", "с", "т", "ф", "х", "ц", "ш"];

    private static readonly string[] CyrillicVowels = ["а", "е", "и", "о", "у", "ы", "э", "ю", "я"];

    private static readonly string[] GreekConsonants =
        ["β", "γ", "δ", "ζ", "θ", "κ", "λ", "μ", "ν", "ξ", "π", "ρ", "σ", "τ", "φ", "χ", "ψ"];

    private static readonly string[] GreekVowels = ["α", "ε", "η", "ι", "ο", "υ", "ω"];

    public static string Generate(RandomTextOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        int target = Math.Clamp(options.Length, MinLength, MaxLength);
        bool byWords = options.CountMode == IpsumCountMode.Words;
        Random random = Random.Shared;

        StringBuilder builder = new();
        int characters = 0;
        int words = 0;

        bool IsFull() => byWords ? words >= target : characters >= target;

        int paragraphIndex = 0;

        while (!IsFull())
        {
            if (options.Paragraphs && builder.Length > 0)
            {
                builder.Append(Environment.NewLine);
                if (options.BlankLineBetweenParagraphs)
                {
                    builder.Append(Environment.NewLine);
                }
            }

            if (options.Paragraphs)
            {
                // 段首：缩进 / 引用 / 列表符号（列表编号按段落递增）
                if (options.IndentFirstLine)
                {
                    builder.Append('\u3000').Append('\u3000');
                }

                if (options.QuoteBlocks)
                {
                    builder.Append("> ");
                }

                switch (options.ListStyle)
                {
                    case IpsumListStyle.Bullets:
                        builder.Append("• ");
                        break;
                    case IpsumListStyle.Ordered:
                        builder.Append(paragraphIndex + 1).Append(". ");
                        break;
                }
            }

            int sentenceCount = options.Paragraphs ? NextSentenceCount(options, random) : int.MaxValue;
            for (int i = 0; i < sentenceCount && !IsFull(); i++)
            {
                AppendSentence(builder, options, random, ref characters, ref words);
            }

            paragraphIndex++;
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>追加一句话，并把计入的字数 / 词数累加到调用方的计数器上。</summary>
    private static void AppendSentence(
        StringBuilder builder,
        RandomTextOptions options,
        Random random,
        ref int characters,
        ref int words)
    {
        int start = builder.Length;
        int hanCount = random.Next(6, 18);
        int sinceComma = 0;
        int commaEvery = NextCommaInterval(options.PunctuationDensity, random);
        int stressChance = ChanceOf(options.StressDensity, 4, 9, 16);

        for (int i = 0; i < hanCount; i++)
        {
            // 字符压力：按强度插入 Emoji / 日韩 / RTL / 符号 / 全角 / 超长串
            if (options.Stress != IpsumStressFlags.None && random.Next(100) < stressChance)
            {
                AppendStress(builder, options.Stress, random);
            }

            // 行内代码：把 1~2 个汉字包成 `字`
            if (options.InlineCode && hanCount - i >= 2 && random.Next(100) < 8)
            {
                builder.Append('`');
                AppendHan(builder, options.IncludeRareCharacters, random);
                words++;
                if (random.Next(2) == 0)
                {
                    AppendHan(builder, options.IncludeRareCharacters, random);
                    words++;
                }

                builder.Append('`');
                sinceComma = 0;
                continue;
            }

            // 成对符号：偶尔把中间 1~2 个字包起来，让括号 / 引号 / 书名号也能出现
            if (options.Punctuation && hanCount - i >= 3 && random.Next(100) < 10)
            {
                bool chinesePair = IsChineseStyle(options.PunctuationStyle, random);
                (char open, char close) = PickPair(chinesePair, random);
                builder.Append(open);

                AppendHan(builder, options.IncludeRareCharacters, random);
                words++;
                if (random.Next(2) == 0)
                {
                    AppendHan(builder, options.IncludeRareCharacters, random);
                    words++;
                }

                builder.Append(close);
                sinceComma = 0;
                continue;
            }

            AppendHan(builder, options.IncludeRareCharacters, random);
            words++;

            sinceComma++;

            // 不可见字符（NBSP / 零宽空格 / 软连字符）：夹在汉字之间
            if ((options.Stress & IpsumStressFlags.Invisible) != 0 && random.Next(100) < 10)
            {
                builder.Append(StressContent.NextInvisible(random));
            }

            if (options.MixedLanguages && random.Next(100) < 14)
            {
                builder.Append(NextSpacer(options.SpaceStyle, random));

                if (options.InlineCode && random.Next(100) < 15)
                {
                    builder.Append('`');
                    AppendForeignWord(builder, random);
                    builder.Append('`');
                }
                else
                {
                    AppendForeignWord(builder, random);
                }

                words++;
                continue;
            }

            if (options.Punctuation && sinceComma >= commaEvery && i < hanCount - 1)
            {
                bool chineseMid = IsChineseStyle(options.PunctuationStyle, random);
                builder.Append(Pick(chineseMid ? ChineseMidMarks : WesternMidMarks, random));
                sinceComma = 0;
                commaEvery = NextCommaInterval(options.PunctuationDensity, random);
            }
        }

        // 结构化片段：URL / 邮箱 / UUID / 版本号 … 接在句末标点之前
        if (options.Fragments != IpsumFragmentFlags.None
            && random.Next(100) < ChanceOf(options.FragmentDensity, 10, 20, 32))
        {
            builder.Append(NextSpacer(options.SpaceStyle, random))
                .Append(StressContent.NextFragment(StressContent.PickFlag(options.Fragments, random), random));
        }

        if (options.Punctuation)
        {
            bool chineseEnd = IsChineseStyle(options.PunctuationStyle, random);
            char[] endMarks = chineseEnd ? ChineseEndMarks : WesternEndMarks;
            builder.Append(Pick(endMarks, random));

            int extra = random.Next(100);
            if (extra < 12)
            {
                // 连续句末符号：？！ / …！ 之类
                builder.Append(Pick(endMarks, random));
            }
            else if (extra < 22)
            {
                // 顺带用上单字符符号（·￥@#% 等）
                builder.Append(Pick(chineseEnd ? ChineseSingleMarks : WesternSingleMarks, random));
            }
        }

        characters += CountVisibleRunes(builder, start);
    }

    private static int NextSentenceCount(RandomTextOptions options, Random random)
    {
        int min = Math.Clamp(
            options.MinSentencesPerParagraph,
            IpsumGenerator.MinSentencesPerParagraph,
            IpsumGenerator.MaxSentencesPerParagraph);
        int max = Math.Clamp(
            options.MaxSentencesPerParagraph,
            min,
            IpsumGenerator.MaxSentencesPerParagraph);

        return options.ParagraphShape switch
        {
            // 短段：贴着下限；长段：贴着上限再往上延一点
            IpsumParagraphShape.Short => random.Next(min, Math.Min(min + 1, max) + 1),
            IpsumParagraphShape.Long => random.Next(
                max,
                Math.Min(max + 3, IpsumGenerator.MaxSentencesPerParagraph) + 1),
            _ => random.Next(min, max + 1)
        };
    }

    private static int NextCommaInterval(IpsumDensity density, Random random) => density switch
    {
        IpsumDensity.Low => random.Next(9, 16),
        IpsumDensity.High => random.Next(2, 5),
        _ => random.Next(4, 9)
    };

    private static int ChanceOf(IpsumDensity density, int low, int medium, int high) => density switch
    {
        IpsumDensity.Low => low,
        IpsumDensity.High => high,
        _ => medium
    };

    /// <summary>追加一个压力字符；超长不可断串单独处理（它是一长串字母）。</summary>
    private static void AppendStress(StringBuilder builder, IpsumStressFlags flags, Random random)
    {
        IpsumStressFlags flag = StressContent.PickFlag(flags, random);

        builder.Append(flag == IpsumStressFlags.LongRun
            ? StressContent.NextLongRun(random)
            : StressContent.NextToken(flag, random));
    }

    private static string NextSpacer(IpsumSpaceStyle style, Random random) => style switch
    {
        IpsumSpaceStyle.None => string.Empty,
        IpsumSpaceStyle.Multiple => random.Next(2) == 0 ? "  " : "   ",
        _ => " "
    };

    /// <summary>
    /// 只统计新增片段里的可见字符，按 Rune 计：Emoji（代理对）/ Ext B 汉字算 1 个，
    /// 而不是 2 个——否则长度目标会明显漂移。
    /// </summary>
    private static int CountVisibleRunes(StringBuilder builder, int start)
    {
        int count = 0;

        foreach (Rune rune in builder.ToString(start, builder.Length - start).EnumerateRunes())
        {
            if (!Rune.IsWhiteSpace(rune))
            {
                count++;
            }
        }

        return count;
    }

    private static void AppendHan(StringBuilder builder, bool includeRare, Random random)
    {
        if (includeRare && random.Next(100) < 25)
        {
            // 生僻字两种来源：GB2312 二级字（次常用）优先，其次 CJK 扩展区（含 BMP 之外）
            IReadOnlyList<char> rareChars = ChineseCharacterSets.Rare;
            if (rareChars.Count > 0 && random.Next(100) < 60)
            {
                builder.Append(rareChars[random.Next(rareChars.Count)]);
                return;
            }

            (int start, int end) = RareRanges[random.Next(RareRanges.Length)];
            builder.Append(char.ConvertFromUtf32(random.Next(start, end + 1)));
            return;
        }

        // 常用字：GB2312 一级字表；取不到码表时退回 CJK 基本区
        IReadOnlyList<char> commonChars = ChineseCharacterSets.Common;
        if (commonChars.Count > 0)
        {
            builder.Append(commonChars[random.Next(commonChars.Count)]);
            return;
        }

        builder.Append((char)random.Next(CommonStart, CommonEnd + 1));
    }

    private static void AppendForeignWord(StringBuilder builder, Random random)
    {
        int kind = random.Next(100);
        if (kind < 22)
        {
            // 规则生成的「公司 / 机构缩写」样式词，覆盖大写字母、句点与 & 的排版
            AppendAbbreviation(builder, random);
        }
        else if (kind < 80)
        {
            AppendSyllableWord(builder, random, LatinConsonants, LatinVowels, useLatinCase: true);
        }
        else if (kind < 92)
        {
            AppendSyllableWord(builder, random, CyrillicConsonants, CyrillicVowels, useLatinCase: false);
        }
        else
        {
            AppendSyllableWord(builder, random, GreekConsonants, GreekVowels, useLatinCase: false);
        }
    }

    /// <summary>
    /// 完全按规则生成「缩写词」：S.A. / N.V. / ABC / A 与 B / AB-1024。
    /// 不依赖任何词表，形态上对应 Inc.、Ltd.、GmbH、S.A. 这类机构后缀，
    /// 用来覆盖大写字母、间隔句点、连字符与数字的排版表现。
    /// </summary>
    private static void AppendAbbreviation(StringBuilder builder, Random random)
    {
        int form = random.Next(100);

        if (form < 40)
        {
            // 字母之间带点：S.A. / N.V. / A.B.C.
            int letters = random.Next(2, 4);
            for (int i = 0; i < letters; i++)
            {
                builder.Append((char)('A' + random.Next(26))).Append('.');
            }

            return;
        }

        if (form < 65)
        {
            // 连续大写：LLC / GmbH 风格
            int letters = random.Next(2, 5);
            for (int i = 0; i < letters; i++)
            {
                builder.Append((char)('A' + random.Next(26)));
            }

            return;
        }

        if (form < 85)
        {
            // A & B 形式
            builder.Append((char)('A' + random.Next(26)))
                .Append(" & ")
                .Append((char)('A' + random.Next(26)));
            return;
        }

        // 缩写 + 编号：AB-1024
        builder.Append((char)('A' + random.Next(26)))
            .Append((char)('A' + random.Next(26)))
            .Append('-')
            .Append(random.Next(100, 9999));
    }

    private static bool IsChineseStyle(IpsumPunctuationStyle style, Random random)
        => style switch
        {
            IpsumPunctuationStyle.Chinese => true,
            IpsumPunctuationStyle.Western => false,
            _ => random.Next(2) == 0
        };

    private static char Pick(char[] source, Random random) => source[random.Next(source.Length)];

    private static (char Open, char Close) PickPair(bool chinese, Random random)
    {
        (char, char)[] pairs = chinese ? ChinesePairs : WesternPairs;
        return pairs[random.Next(pairs.Length)];
    }

    /// <summary>
    /// 按「辅音 + 元音」拼伪单词，音节数分四档，覆盖 2~3 / 4~6 / 7~10 / 11~14 个字母，
    /// 偶尔首字母大写、带数字或连字符，模拟真实代码里各种形态的标识符/单词。
    /// </summary>
    private static void AppendSyllableWord(
        StringBuilder builder,
        Random random,
        string[] consonants,
        string[] vowels,
        bool useLatinCase)
    {
        int bucket = random.Next(100);
        int syllables = bucket < 30 ? random.Next(1, 3)
            : bucket < 70 ? random.Next(2, 4)
            : bucket < 90 ? random.Next(3, 6)
            : random.Next(5, 8);

        StringBuilder word = new();
        for (int i = 0; i < syllables; i++)
        {
            word.Append(consonants[random.Next(consonants.Length)]);
            word.Append(vowels[random.Next(vowels.Length)]);
        }

        if (useLatinCase && random.Next(100) < 10)
        {
            word[0] = char.ToUpperInvariant(word[0]);
        }

        if (random.Next(100) < 12)
        {
            word.Append(random.Next(10, 100));
        }

        if (random.Next(100) < 8)
        {
            word.Append('-')
                .Append(consonants[random.Next(consonants.Length)])
                .Append(vowels[random.Next(vowels.Length)]);
        }

        builder.Append(word);
    }
}
