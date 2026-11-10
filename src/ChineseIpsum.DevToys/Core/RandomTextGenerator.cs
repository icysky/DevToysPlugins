using System.Text;

namespace ChineseIpsum.DevToys.Core;

/// <summary>文本来源：名著节选 / 纯随机乱文。</summary>
internal enum IpsumMode
{
    Corpus = 0,
    Random = 1
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

    public bool Punctuation { get; init; } = true;

    /// <summary>混入英文 / 西里尔 / 希腊文的伪单词（无意义，只求长短与形态多样）。</summary>
    public bool MixedLanguages { get; init; }
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

    private static readonly char[] EndPunctuations = ['。', '。', '。', '！', '？'];

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

        while (!IsFull())
        {
            if (options.Paragraphs && builder.Length > 0)
            {
                builder.Append(Environment.NewLine).Append(Environment.NewLine);
            }

            int minSentences = Math.Clamp(
                options.MinSentencesPerParagraph,
                IpsumGenerator.MinSentencesPerParagraph,
                IpsumGenerator.MaxSentencesPerParagraph);
            int maxSentences = Math.Clamp(
                options.MaxSentencesPerParagraph,
                minSentences,
                IpsumGenerator.MaxSentencesPerParagraph);

            int sentenceCount = options.Paragraphs ? random.Next(minSentences, maxSentences + 1) : int.MaxValue;
            for (int i = 0; i < sentenceCount && !IsFull(); i++)
            {
                AppendSentence(builder, options, random, ref characters, ref words);
            }
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
        int commaEvery = random.Next(4, 9);

        for (int i = 0; i < hanCount; i++)
        {
            AppendHan(builder, options.IncludeRareCharacters, random);
            words++;

            sinceComma++;

            if (options.MixedLanguages && random.Next(100) < 14)
            {
                builder.Append(' ');
                AppendForeignWord(builder, random);
                words++;
                continue;
            }

            if (options.Punctuation && sinceComma >= commaEvery && i < hanCount - 1)
            {
                builder.Append('，');
                sinceComma = 0;
                commaEvery = random.Next(4, 9);
            }
        }

        if (options.Punctuation)
        {
            builder.Append(EndPunctuations[random.Next(EndPunctuations.Length)]);
        }

        // 字符数只统计新增片段里的非空白字符（标点算，空格与换行不算）
        for (int i = start; i < builder.Length; i++)
        {
            if (!char.IsWhiteSpace(builder[i]))
            {
                characters++;
            }
        }
    }

    private static void AppendHan(StringBuilder builder, bool includeRare, Random random)
    {
        if (includeRare && random.Next(100) < 18)
        {
            (int start, int end) = RareRanges[random.Next(RareRanges.Length)];
            int codePoint = random.Next(start, end + 1);
            builder.Append(char.ConvertFromUtf32(codePoint));
            return;
        }

        builder.Append((char)random.Next(CommonStart, CommonEnd + 1));
    }

    private static void AppendForeignWord(StringBuilder builder, Random random)
    {
        int kind = random.Next(100);
        if (kind < 82)
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
