using System.Text;
using System.Text.RegularExpressions;
using ChineseIpsum.DevToys.Core;

// 生成器用到的两套标点（中文 / 西文），测试里用来判定风格是否串味
const string ChinesePuncts = "，。！？；：、（）《》「」“”…·～￥℃±【】";
const string WesternPuncts = ",.!?;:()[]{}\"'@#%&*+=<>/\\|^$~-—";

int passed = 0;
int failed = 0;

Console.WriteLine("=== ChineseIpsum.DevToys 生成逻辑冒烟测试 ===");
Console.WriteLine();

Index("语料加载");
foreach (IpsumCorpus corpus in Enum.GetValues<IpsumCorpus>())
{
    IpsumCorpus current = corpus;
    Check($"{current} 句数 >= 20", () => IpsumCorpora.GetSentences(current).Count >= 20);
}

Check("混合 = 四本之和", () =>
{
    int sum = IpsumCorpora.GetSentences(IpsumCorpus.Sanguo).Count
              + IpsumCorpora.GetSentences(IpsumCorpus.Shuihu).Count
              + IpsumCorpora.GetSentences(IpsumCorpus.Xiyou).Count
              + IpsumCorpora.GetSentences(IpsumCorpus.Honglou).Count;
    int mixed = IpsumCorpora.GetSentences(IpsumCorpus.Mixed).Count;
    Console.WriteLine($"        四本合计 {sum}，混合 {mixed}");
    return sum == mixed;
});

Check("每本都有开篇句", () =>
{
    foreach (IpsumCorpus corpus in new[]
             { IpsumCorpus.Sanguo, IpsumCorpus.Shuihu, IpsumCorpus.Xiyou, IpsumCorpus.Honglou })
    {
        string opening = IpsumCorpora.GetOpening(corpus);
        if (opening.Length == 0 || !IpsumCorpora.GetSentences(corpus).Contains(opening))
        {
            Console.WriteLine($"        {corpus} 开篇句异常：{opening}");
            return false;
        }
    }

    return true;
});

Check("语料无空行、无重复前导空白", () =>
    IpsumCorpora.GetSentences(IpsumCorpus.Mixed)
        .All(s => s.Length > 0 && s == s.Trim()));

Index("段落模式");
Check("3 段", () =>
{
    string text = IpsumGenerator.Generate(IpsumCorpus.Mixed, IpsumUnit.Paragraphs, 3, startWithOpening: false);
    int paragraphs = text.Split($"{Environment.NewLine}{Environment.NewLine}", StringSplitOptions.None).Length;
    Console.WriteLine($"        段数={paragraphs}，字数={text.Length}");
    return paragraphs == 3;
});

Check("1 段不产生空行", () =>
    !IpsumGenerator.Generate(IpsumCorpus.Sanguo, IpsumUnit.Paragraphs, 1, false).Contains($"{Environment.NewLine}{Environment.NewLine}"));

Check("200 段可用（上限压测）", () =>
    IpsumGenerator.Generate(IpsumCorpus.Mixed, IpsumUnit.Paragraphs, 200, false).Length > 2000);

Index("句子模式");
foreach (int count in new[] { 1, 5, 30 })
{
    int expected = count;
    Check($"{expected} 句", () =>
    {
        string text = IpsumGenerator.Generate(IpsumCorpus.Honglou, IpsumUnit.Sentences, expected, false);
        int actual = text.Count(c => c == '。') + text.Count(c => c == '？') + text.Count(c => c == '！');
        Console.WriteLine($"        句号数={actual}，字数={text.Length}");
        return actual == expected;
    });
}

Check("开启「开篇起句」时首句固定", () =>
{
    string text = IpsumGenerator.Generate(IpsumCorpus.Xiyou, IpsumUnit.Sentences, 3, startWithOpening: true);
    string opening = IpsumCorpora.GetOpening(IpsumCorpus.Xiyou);
    Console.WriteLine($"        {text[..Math.Min(30, text.Length)]}...");
    return text.StartsWith(opening, StringComparison.Ordinal);
});

Check("关闭时首句可以不是开篇句（允许偶然命中）", () =>
{
    // 随机 20 次里至少有一次不是开篇句
    string opening = IpsumCorpora.GetOpening(IpsumCorpus.Sanguo);
    for (int i = 0; i < 20; i++)
    {
        string text = IpsumGenerator.Generate(IpsumCorpus.Sanguo, IpsumUnit.Sentences, 3, startWithOpening: false);
        if (!text.StartsWith(opening, StringComparison.Ordinal))
        {
            return true;
        }
    }

    return false;
});

Index("字数模式");
foreach (int target in new[] { 50, 300, 1000 })
{
    int expected = target;
    Check($"目标 {expected} 字（实际应略超）", () =>
    {
        string text = IpsumGenerator.Generate(IpsumCorpus.Mixed, IpsumUnit.Characters, expected, false);
        Console.WriteLine($"        实际={text.Length}");
        return text.Length >= expected && text.Length < expected + 120;
    });
}

Check("1 字也能给出内容", () =>
    IpsumGenerator.Generate(IpsumCorpus.Mixed, IpsumUnit.Characters, 1, false).Length > 0);

Index("其它约束");
Check("所有输出非空", () =>
{
    foreach (IpsumCorpus corpus in Enum.GetValues<IpsumCorpus>())
    {
        foreach (IpsumUnit unit in Enum.GetValues<IpsumUnit>())
        {
            if (IpsumGenerator.Generate(corpus, unit, 5, true).Length == 0)
            {
                Console.WriteLine($"        {corpus}/{unit} 输出为空");
                return false;
            }
        }
    }

    return true;
});

Check("段落内句子不重复", () =>
{
    // 语料以「。」收尾，因此可以按句号切回句子列表
    for (int round = 0; round < 40; round++)
    {
        string text = IpsumGenerator.Generate(IpsumCorpus.Sanguo, IpsumUnit.Paragraphs, 3, true);
        foreach (string paragraph in text.Split($"{Environment.NewLine}{Environment.NewLine}", StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = paragraph.Split('。', StringSplitOptions.RemoveEmptyEntries);
            int unique = parts.Select(p => p.Trim()).Distinct(StringComparer.Ordinal).Count();
            if (unique != parts.Length)
            {
                Console.WriteLine($"        重复：{paragraph}");
                return false;
            }
        }
    }

    return true;
});

Check("句子模式内句子不重复（语料够用时）", () =>
{
    for (int round = 0; round < 40; round++)
    {
        string text = IpsumGenerator.Generate(IpsumCorpus.Mixed, IpsumUnit.Sentences, 20, true);
        string[] parts = text.Split('。', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Select(p => p.Trim()).Distinct(StringComparer.Ordinal).Count() != parts.Length)
        {
            return false;
        }
    }

    return true;
});

Check("连续两次生成不同", () =>
{
    string a = IpsumGenerator.Generate(IpsumCorpus.Mixed, IpsumUnit.Sentences, 20, true);
    string b = IpsumGenerator.Generate(IpsumCorpus.Mixed, IpsumUnit.Sentences, 20, true);
    Console.WriteLine($"        两次生成是否相同：{a == b}");
    return a != b;
});

Check("超出范围的数量会被夹到合法区间", () =>
    IpsumGenerator.Generate(IpsumCorpus.Mixed, IpsumUnit.Sentences, 99999, false).Length > 0
    && IpsumGenerator.Generate(IpsumCorpus.Mixed, IpsumUnit.Sentences, 0, false).Length > 0);

Index("随机乱文：字符数模式");
Check("500 字符：实际长度达到目标且只略微超出", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 500,
        CountMode = IpsumCountMode.Characters
    });
    int chars = CountVisibleCharacters(text);
    Console.WriteLine($"        目标 500，实际 {chars} 字符");
    return chars >= 500 && chars <= 580;
});

Check("首尾无多余空白", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions { Length = 120 });
    return text.Length > 0 && text == text.Trim();
});

Check("不同数量得到不同长度", () =>
{
    int small = CountVisibleCharacters(RandomTextGenerator.Generate(new RandomTextOptions { Length = 50 }));
    int large = CountVisibleCharacters(RandomTextGenerator.Generate(new RandomTextOptions { Length = 800 }));
    return large > small;
});

Index("随机乱文：字词数模式");
Check("200 字词：达到目标词数", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 200,
        CountMode = IpsumCountMode.Words
    });
    int words = CountWords(text);
    Console.WriteLine($"        目标 200 词，实际 {words} 词，{text.Length} 字符");
    return words >= 200 && words <= 320;
});

Index("随机乱文：标点开关");
Check("关闭标点：结果不含任何标点", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 300,
        Punctuation = false
    });

    return !text.Any(c => ChinesePuncts.Contains(c) || WesternPuncts.Contains(c));
});

Check("开启标点：有句末标点且以标点结尾", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 300,
        Punctuation = true
    });

    bool hasEnd = text.Any(c => "。！？…".Contains(c));
    bool endsWithMark = "。！？…·～￥℃±".Contains(text[^1]);
    return hasEnd && endsWithMark;
});

Index("随机乱文：标点风格与符号覆盖");
Check("中文标点：不出现西文标点", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 1200,
        PunctuationStyle = IpsumPunctuationStyle.Chinese
    });

    return !text.Any(c => WesternPuncts.Contains(c)) && text.Any(c => ChinesePuncts.Contains(c));
});

Check("西文标点：不出现中文标点", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 1200,
        PunctuationStyle = IpsumPunctuationStyle.Western
    });

    return !text.Any(c => ChinesePuncts.Contains(c)) && text.Any(c => WesternPuncts.Contains(c));
});

Check("混合标点：两种都出现", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 1500,
        PunctuationStyle = IpsumPunctuationStyle.Mixed
    });

    return text.Any(c => ChinesePuncts.Contains(c)) && text.Any(c => WesternPuncts.Contains(c));
});

Check("符号覆盖：1500 字符内至少出现 10 种不同标点", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 1500,
        PunctuationStyle = IpsumPunctuationStyle.Mixed
    });

    int distinct = text
        .Where(c => ChinesePuncts.Contains(c) || WesternPuncts.Contains(c))
        .Distinct()
        .Count();

    Console.WriteLine($"        不同标点数={distinct}");
    return distinct >= 10;
});

Index("随机乱文：多语言缩写词");
Check("出现缩写样式（大写字母 + 句点 / 与号 / 编号）", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 1200,
        MixedLanguages = true
    });

    bool dotted = Regex.IsMatch(text, "[A-Z]\\.[A-Z]?\\.");
    bool ampersand = text.Contains(" & ", StringComparison.Ordinal);
    bool coded = Regex.IsMatch(text, "[A-Z]{2}-\\d{2,4}");
    Console.WriteLine($"        带点={dotted} 与号={ampersand} 编号={coded}");
    return dotted || ampersand || coded;
});

Index("随机乱文：段落开关");
Check("模拟段落：出现空行分段", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 600,
        Paragraphs = true
    });
    int paragraphs = text.Split($"{Environment.NewLine}{Environment.NewLine}", StringSplitOptions.None).Length;
    Console.WriteLine($"        段数={paragraphs}");
    return paragraphs >= 2;
});

Check("关闭段落：不出现空行", () =>
    !RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 600,
        Paragraphs = false
    }).Contains($"{Environment.NewLine}{Environment.NewLine}"));

Index("每段句子数范围");
Check("名著模式：1 句/段 与 6 句/段 长度明显不同", () =>
{
    string few = IpsumGenerator.Generate(IpsumCorpus.Mixed, IpsumUnit.Paragraphs, 4, false, 1, 1);
    string many = IpsumGenerator.Generate(IpsumCorpus.Mixed, IpsumUnit.Paragraphs, 4, false, 6, 6);
    Console.WriteLine($"        1 句/段={few.Length} 字符，6 句/段={many.Length} 字符");
    return many.Length > few.Length * 3;
});

Check("名著模式：min > max 自动纠正，不抛异常", () =>
    IpsumGenerator.Generate(IpsumCorpus.Mixed, IpsumUnit.Paragraphs, 2, false, 6, 1).Length > 0);

Check("随机乱文：固定 3 句/段 时每段正好 3 句", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 900,
        Paragraphs = true,
        MinSentencesPerParagraph = 3,
        MaxSentencesPerParagraph = 3
    });

    string[] paragraphs = text.Split($"{Environment.NewLine}{Environment.NewLine}", StringSplitOptions.RemoveEmptyEntries);
    Console.WriteLine($"        段数={paragraphs.Length}，首段句数={paragraphs[0].Count(c => c is '。' or '！' or '？')}");

    // 句末符号可能连续出现（？！ / …！），所以按「连续句末符号」为一组来数句子
    int CountSentences(string paragraph) => Regex.Matches(paragraph, "[。！？…]+").Count;

    // 最后一段会因为「达到目标长度就停」而被截短，这里只校验完整段
    return paragraphs.Length >= 2
           && paragraphs.Take(paragraphs.Length - 1).All(p => CountSentences(p) == 3)
           && CountSentences(paragraphs[^1]) is >= 1 and <= 3;
});

Check("随机乱文：1~2 句/段 时每段落在区间内", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 900,
        Paragraphs = true,
        MinSentencesPerParagraph = 1,
        MaxSentencesPerParagraph = 2
    });

    string[] paragraphs = text.Split($"{Environment.NewLine}{Environment.NewLine}", StringSplitOptions.RemoveEmptyEntries);
    return paragraphs.Length >= 2
           && paragraphs.All(p => Regex.Matches(p, "[。！？…]+").Count is >= 1 and <= 2);
});

Index("随机乱文：生僻字开关");
Console.WriteLine($"        字表来源：{(ChineseCharacterSets.UsesGb2312 ? "GB2312 一级/二级" : "回退到 Unicode 基本区")}，"
                  + $"常用 {ChineseCharacterSets.Common.Count} 字，生僻 {ChineseCharacterSets.Rare.Count} 字");

Check("字表加载：常用字 >= 3000 且生僻字 > 0", () =>
    ChineseCharacterSets.Common.Count >= 3000 && ChineseCharacterSets.Rare.Count > 0);

Check("关闭生僻字：所有汉字都在常用字表内", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions { Length = 800 });
    bool ok = AllHanInCommon(text);
    Console.WriteLine($"        全部为常用字：{ok}");
    return ok;
});

Check("开启生僻字：出现常用字表以外的字", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 800,
        IncludeRareCharacters = true
    });

    int outside = CountHanOutsideCommon(text);
    Console.WriteLine($"        常用字表外的汉字数：{outside}");
    return outside > 0;
});

Index("段落间空行开关");
Check("随机乱文：关闭后无空行但段落仍换行", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 600,
        BlankLineBetweenParagraphs = false
    });

    return !text.Contains($"{Environment.NewLine}{Environment.NewLine}")
           && text.Contains(Environment.NewLine);
});

Check("随机乱文：开启后段落之间有空行", () =>
    RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 600,
        BlankLineBetweenParagraphs = true
    }).Contains($"{Environment.NewLine}{Environment.NewLine}"));

Check("名著模式：关闭后无空行但段落仍换行", () =>
{
    string text = IpsumGenerator.Generate(
        IpsumCorpus.Mixed,
        IpsumUnit.Paragraphs,
        3,
        startWithOpening: false,
        minSentencesPerParagraph: 3,
        maxSentencesPerParagraph: 6,
        blankLineBetweenParagraphs: false);

    return !text.Contains($"{Environment.NewLine}{Environment.NewLine}")
           && text.Contains(Environment.NewLine);
});

Check("名著模式：开启后段落之间有空行", () =>
    IpsumGenerator.Generate(IpsumCorpus.Mixed, IpsumUnit.Paragraphs, 3, false, 3, 6, true)
        .Contains($"{Environment.NewLine}{Environment.NewLine}"));

Index("随机乱文：多语言混合");
Check("开启混合：出现外文单词（ASCII 字母）", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 400,
        MixedLanguages = true
    });
    int latin = text.Count(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
    Console.WriteLine($"        ASCII 字母数={latin}");
    return latin >= 4;
});

Check("关闭混合：结果只有汉字与标点", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 400,
        MixedLanguages = false
    });
    return !text.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
});

Index("随机乱文：边界与性能");
Check("数量 1 也能出结果", () => RandomTextGenerator.Generate(new RandomTextOptions { Length = 1 }).Length > 0);

Check("超范围数量被夹到 1~5000", () =>
{
    string huge = RandomTextGenerator.Generate(new RandomTextOptions { Length = 999999 });
    string tiny = RandomTextGenerator.Generate(new RandomTextOptions { Length = -5 });
    return CountVisibleCharacters(huge) <= 5200 && tiny.Length > 0;
});

Check("5000 字符在 2 秒内生成", () =>
{
    var watch = System.Diagnostics.Stopwatch.StartNew();
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 5000,
        IncludeRareCharacters = true,
        MixedLanguages = true
    });
    watch.Stop();
    Console.WriteLine($"        {CountVisibleCharacters(text)} 字符，{watch.ElapsedMilliseconds} ms");
    return watch.ElapsedMilliseconds < 2000;
});

Index("字符压力（P0 / P1）");
Check("Emoji：出现代理对 / ZWJ 序列", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 600,
        Stress = IpsumStressFlags.Emoji
    });

    return HasRune(text, r => r.Value > 0xFFFF || r.Value == 0x200D);
});

Check("日文 / 韩文：出现假名或谚文", () =>
    HasRune(
        RandomTextGenerator.Generate(new RandomTextOptions { Length = 600, Stress = IpsumStressFlags.EastAsian }),
        r => r.Value is >= 0x3040 and <= 0x30FF or >= 0xAC00 and <= 0xD7AF));

Check("RTL：出现阿拉伯文或希伯来文", () =>
    HasRune(
        RandomTextGenerator.Generate(new RandomTextOptions { Length = 600, Stress = IpsumStressFlags.Rtl }),
        r => r.Value is >= 0x0590 and <= 0x06FF));

Check("数学运算符：出现 ∑ ⊕ ⦿ / 各种箭头", () =>
    HasRune(
        RandomTextGenerator.Generate(new RandomTextOptions { Length = 800, Stress = IpsumStressFlags.MathSymbols }),
        r => r.Value is >= 0x2190 and <= 0x22FF or >= 0x27C0 and <= 0x2AFF));

Check("图形与符号：出现 ◆ ● ★ ☀ ♪ ✓ ┼ 等", () =>
    HasRune(
        RandomTextGenerator.Generate(new RandomTextOptions { Length = 800, Stress = IpsumStressFlags.GraphicSymbols }),
        r => r.Value is >= 0x2500 and <= 0x27BF));

Check("其他文字区块：出现盲文 / 藏文 / 彝文 / 泰文 / 康熙部首", () =>
    HasRune(
        RandomTextGenerator.Generate(new RandomTextOptions
        {
            Length = 1200,
            Stress = IpsumStressFlags.OtherScripts
        }),
        r => r.Value is >= 0x0900 and <= 0x0FFF
            or >= 0x1800 and <= 0x18AF
            or >= 0x2800 and <= 0x28FF
            or >= 0x2F00 and <= 0x2FDF
            or >= 0x3200 and <= 0x32FF
            or >= 0xA000 and <= 0xA4CF
            or >= 0xFE10 and <= 0xFE1F));

Check("带圈运算符（⊕ ⦿ ⊗ ⊙）能出现", () =>
    HasRune(
        RandomTextGenerator.Generate(new RandomTextOptions
        {
            Length = 2000,
            Stress = IpsumStressFlags.MathSymbols,
            StressDensity = IpsumDensity.High
        }),
        r => r.Value is >= 0x2295 and <= 0x29BF));

Check("拉丁扩展：出现变音符号或组合音标", () =>
    HasRune(
        RandomTextGenerator.Generate(new RandomTextOptions { Length = 600, Stress = IpsumStressFlags.LatinExtended }),
        r => r.Value is >= 0x00C0 and <= 0x00FF or >= 0x0300 and <= 0x036F));

Check("特殊数字与单位：出现 ½ / ① / ℃ 一类", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 800,
        Stress = IpsumStressFlags.SpecialForms
    });

    return text.Any(c => "¹²³₀₁₂½⅓¾⅔①②⑦⑩ⅠⅡⅢ¥€£₩₹₿℃℉㎡‰".Contains(c));
});

Check("全角字符：出现全角字母或数字", () =>
    HasRune(
        RandomTextGenerator.Generate(new RandomTextOptions { Length = 600, Stress = IpsumStressFlags.FullWidth }),
        // 只认全角字母/数字：0xFF01–0xFF5E 里还包含中文全角标点，不能整段判定
        r => r.Value is >= 0xFF10 and <= 0xFF19 or >= 0xFF21 and <= 0xFF3A or >= 0xFF41 and <= 0xFF5A));

Check("不可见字符：出现 NBSP / 零宽空格", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 600,
        Stress = IpsumStressFlags.Invisible
    });

    return text.Any(c => c is '\u00A0' or '\u200B' or '\u00AD' or '\u200E' or '\u2060');
});

Check("超长不可断串：出现 40 个以上连续字母", () =>
    Regex.IsMatch(
        RandomTextGenerator.Generate(new RandomTextOptions { Length = 800, Stress = IpsumStressFlags.LongRun }),
        "[A-Za-z]{40,}"));

Check("默认全关：以上字符一个都不出现", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions { Length = 800 });

    bool hasStress = HasRune(text, r =>
        r.Value > 0xFFFF
        || r.Value is >= 0x3040 and <= 0x30FF
        || r.Value is >= 0xAC00 and <= 0xD7AF
        || r.Value is >= 0x0590 and <= 0x06FF
        || r.Value is >= 0x0900 and <= 0x0FFF
        || r.Value is >= 0x1800 and <= 0x18AF
        || r.Value is >= 0x2190 and <= 0x27BF
        || r.Value is >= 0x2800 and <= 0x28FF
        || r.Value is >= 0x2A00 and <= 0x2AFF
        || r.Value is >= 0x2F00 and <= 0x2FDF
        || r.Value is >= 0x3200 and <= 0x32FF
        || r.Value is >= 0xA000 and <= 0xA4CF
        || r.Value is >= 0xFE10 and <= 0xFE1F
        || r.Value is >= 0xFF10 and <= 0xFF19
        || r.Value is >= 0xFF21 and <= 0xFF3A
        || r.Value is >= 0xFF41 and <= 0xFF5A);

    return !hasStress
           && !Regex.IsMatch(text, "[A-Za-z]{40,}")
           && !text.Any(c => c is '\u00A0' or '\u200B' or '\u00AD');
});

Index("结构化片段（P2）");
Check("URL", () => Regex.IsMatch(
    RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 1200,
        Fragments = IpsumFragmentFlags.Url,
        FragmentDensity = IpsumDensity.High
    }),
    "https://[a-z]+\\.[a-z.]+/"));

Check("邮箱", () => Regex.IsMatch(
    RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 1200,
        Fragments = IpsumFragmentFlags.Email,
        FragmentDensity = IpsumDensity.High
    }),
    "[a-z]+\\.[a-z]+@[a-z]+\\.[a-z.]+"));

Check("UUID / 哈希", () => Regex.IsMatch(
    RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 1200,
        Fragments = IpsumFragmentFlags.Identifier,
        FragmentDensity = IpsumDensity.High
    }),
    "[0-9a-f]{32,64}"));

Check("版本号", () => Regex.IsMatch(
    RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 1200,
        Fragments = IpsumFragmentFlags.Version,
        FragmentDensity = IpsumDensity.High
    }),
    "v\\d+\\.\\d+\\.\\d+"));

Check("日期时间", () => Regex.IsMatch(
    RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 1200,
        Fragments = IpsumFragmentFlags.DateTime,
        FragmentDensity = IpsumDensity.High
    }),
    "\\d{4}[-/]\\d{2}[-/]\\d{2}"));

Check("金额数值", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 1500,
        Fragments = IpsumFragmentFlags.Amount,
        FragmentDensity = IpsumDensity.High
    });

    return text.Contains("kg", StringComparison.Ordinal)
           || text.Contains("km/h", StringComparison.Ordinal)
           || text.Contains('%');
});

Check("文件路径", () => RandomTextGenerator.Generate(new RandomTextOptions
{
    Length = 1200,
    Fragments = IpsumFragmentFlags.FilePath,
    FragmentDensity = IpsumDensity.High
}).Contains("/var/www/", StringComparison.Ordinal));

Index("段落形态（P3）");
Check("首行缩进：每段以两个全角空格开头", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions { Length = 600, IndentFirstLine = true });
    return text
        .Split($"{Environment.NewLine}{Environment.NewLine}", StringSplitOptions.RemoveEmptyEntries)
        .All(p => p.StartsWith("\u3000\u3000", StringComparison.Ordinal));
});

Check("列表符号：项目符号 / 有序编号", () =>
{
    string bullets = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 400,
        ListStyle = IpsumListStyle.Bullets
    });
    string ordered = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 400,
        ListStyle = IpsumListStyle.Ordered
    });

    return bullets.StartsWith("• ", StringComparison.Ordinal)
           && ordered.StartsWith("1. ", StringComparison.Ordinal);
});

Check("引用块：段落以 > 开头", () =>
    RandomTextGenerator.Generate(new RandomTextOptions { Length = 400, QuoteBlocks = true })
        .StartsWith("> ", StringComparison.Ordinal));

Check("行内代码：出现反引号对", () =>
    Regex.IsMatch(
        RandomTextGenerator.Generate(new RandomTextOptions { Length = 1200, InlineCode = true }),
        "`[^`]+`"));

Check("段落长短：短段多 vs 长段多", () =>
{
    int CountParagraphs(string value)
        => value.Split($"{Environment.NewLine}{Environment.NewLine}", StringSplitOptions.RemoveEmptyEntries).Length;

    string shortText = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 1200,
        ParagraphShape = IpsumParagraphShape.Short
    });
    string longText = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 1200,
        ParagraphShape = IpsumParagraphShape.Long
    });

    Console.WriteLine($"        短段：{CountParagraphs(shortText)} 段，长段：{CountParagraphs(longText)} 段");
    return CountParagraphs(shortText) > CountParagraphs(longText);
});

Check("标点密度：高密度逗号更多", () =>
{
    int CountCommas(string value) => value.Count(c => c == '，');

    string low = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 1200,
        PunctuationDensity = IpsumDensity.Low
    });
    string high = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 1200,
        PunctuationDensity = IpsumDensity.High
    });

    Console.WriteLine($"        低密度 {CountCommas(low)} 个逗号，高密度 {CountCommas(high)} 个");
    return CountCommas(high) > CountCommas(low);
});

Check("空格策略：多空格时出现连续空格", () =>
    RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 800,
        MixedLanguages = true,
        SpaceStyle = IpsumSpaceStyle.Multiple
    }).Contains("  ", StringComparison.Ordinal));

Check("压力项全开时长度仍然达标（按 Rune 计数）", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 800,
        Stress = IpsumStressFlags.Emoji | IpsumStressFlags.EastAsian | IpsumStressFlags.Rtl
                 | IpsumStressFlags.MathSymbols | IpsumStressFlags.LatinExtended
                 | IpsumStressFlags.SpecialForms | IpsumStressFlags.FullWidth
                 | IpsumStressFlags.Invisible,
        MixedLanguages = true,
        InlineCode = true
    });

    int chars = CountVisibleCharacters(text);
    Console.WriteLine($"        目标 800，实际 {chars} 字符");
    return chars >= 800 && chars <= 1000;
});

Index("样例输出");
Console.WriteLine("【三国演义 · 段落 · 2 段】");
Console.WriteLine(IpsumGenerator.Generate(IpsumCorpus.Sanguo, IpsumUnit.Paragraphs, 2, startWithOpening: true));
Console.WriteLine();
Console.WriteLine("【四大名著混合 · 约 100 字】");
Console.WriteLine(IpsumGenerator.Generate(IpsumCorpus.Mixed, IpsumUnit.Characters, 100, startWithOpening: false));
Console.WriteLine();
Console.WriteLine("【随机乱文 · 300 字符 · 含标点 / 段落 / 多语言 / 生僻字】");
Console.WriteLine(RandomTextGenerator.Generate(new RandomTextOptions
{
    Length = 300,
    IncludeRareCharacters = true,
    MixedLanguages = true
}));

Console.WriteLine();
Console.WriteLine($"===== 通过 {passed} 项，失败 {failed} 项 =====");
Console.WriteLine(failed == 0 ? "全部通过 ✅" : "存在失败 ❌");
return failed == 0 ? 0 : 1;

void Index(string title)
{
    Console.WriteLine();
    Console.WriteLine($"--- {title} ---");
}

void Check(string name, Func<bool> condition)
{
    bool ok;
    try
    {
        ok = condition();
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL  {name}: {ex.GetType().Name}: {ex.Message}");
        return;
    }

    if (ok)
    {
        passed++;
        Console.WriteLine($"PASS  {name}");
    }
    else
    {
        failed++;
        Console.WriteLine($"FAIL  {name}");
    }
}

static int CountVisibleCharacters(string text) => text.Count(c => !char.IsWhiteSpace(c));

static bool HasRune(string text, Func<Rune, bool> predicate)
{
    foreach (Rune rune in text.EnumerateRunes())
    {
        if (predicate(rune))
        {
            return true;
        }
    }

    return false;
}

static int CountWords(string text)
{
    int words = 0;
    bool inForeign = false;

    foreach (Rune rune in text.EnumerateRunes())
    {
        if (IsHan(rune.Value))
        {
            words++;
            inForeign = false;
        }
        else if (Rune.IsLetterOrDigit(rune))
        {
            if (!inForeign)
            {
                words++;
                inForeign = true;
            }
        }
        else
        {
            inForeign = false;
        }
    }

    return words;
}

static bool IsHan(int codePoint)
    => codePoint is >= 0x4E00 and <= 0x9FFF
       || codePoint is >= 0x3400 and <= 0x4DBF
       || codePoint is >= 0x20000 and <= 0x2A6DF;

static bool AllHanInCommon(string text) => CountHanOutsideCommon(text) == 0;

static int CountHanOutsideCommon(string text)
{
    HashSet<int> common = [.. ChineseCharacterSets.Common.Select(c => (int)c)];
    int outside = 0;

    foreach (Rune rune in text.EnumerateRunes())
    {
        if (IsHan(rune.Value) && !common.Contains(rune.Value))
        {
            outside++;
        }
    }

    return outside;
}
