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

Index("样例输出");
Console.WriteLine("【三国演义 · 段落 · 2 段】");
Console.WriteLine(IpsumGenerator.Generate(IpsumCorpus.Sanguo, IpsumUnit.Paragraphs, 2, startWithOpening: true));
Console.WriteLine();
Console.WriteLine("【四大名著混合 · 约 100 字】");
Console.WriteLine(IpsumGenerator.Generate(IpsumCorpus.Mixed, IpsumUnit.Characters, 100, startWithOpening: false));

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
