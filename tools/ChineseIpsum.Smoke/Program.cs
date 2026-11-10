using System.Text;
using ChineseIpsum.DevToys.Core;

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
    return !text.Any(c => c is '，' or '。' or '；' or '：' or '！' or '？' or '、');
});

Check("开启标点：有句末标点且以标点结尾", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 300,
        Punctuation = true
    });
    return text.Any(c => c is '。' or '！' or '？')
           && (text[^1] is '。' or '！' or '？');
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

    // 最后一段会因为「达到目标长度就停」而被截短，这里只校验完整段
    return paragraphs.Length >= 2
           && paragraphs.Take(paragraphs.Length - 1).All(p => p.Count(c => c is '。' or '！' or '？') == 3)
           && paragraphs[^1].Count(c => c is '。' or '！' or '？') is >= 1 and <= 3;
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
           && paragraphs.All(p =>
           {
               int count = p.Count(c => c is '。' or '！' or '？');
               return count is >= 1 and <= 2;
           });
});

Index("随机乱文：生僻字开关");
Check("默认无扩展区生僻字", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions { Length = 800 });
    return !ContainsRareCharacter(text);
});

Check("开启后混入扩展区生僻字", () =>
{
    string text = RandomTextGenerator.Generate(new RandomTextOptions
    {
        Length = 800,
        IncludeRareCharacters = true
    });
    Console.WriteLine($"        含生僻字：{ContainsRareCharacter(text)}");
    return ContainsRareCharacter(text);
});

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

static bool ContainsRareCharacter(string text)
{
    foreach (Rune rune in text.EnumerateRunes())
    {
        if (rune.Value is >= 0x3400 and <= 0x4DB5
            or >= 0x9FA6 and <= 0x9FFF
            or >= 0x20000 and <= 0x2A6D6)
        {
            return true;
        }
    }

    return false;
}
