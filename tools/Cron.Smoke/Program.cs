using System.Globalization;
using Cron.DevToys.Core;

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");

const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
DateTime now = new(2026, 11, 12, 11, 27, 30);   // 周四

int passed = 0;
int failed = 0;

Console.WriteLine("=== Cron.DevToys 解析 / 生成逻辑冒烟测试 ===");
Console.WriteLine();

Index("中文描述");
(string Expression, string Expected)[] descriptions =
[
    ("* * * * *", "每分钟"),
    ("*/5 * * * *", "每 5 分钟"),
    ("0 * * * *", "每小时第 0 分"),
    ("0 9 * * *", "每天 09:00"),
    ("30 9 * * 1-5", "工作日 09:30"),
    ("0 0 1 * *", "每月 1 日 00:00"),
    ("0 0 1 1 *", "每年 1 月 1 日 00:00"),
    ("0 0 * * 0", "每周日 00:00"),
    ("0 0 * * 7", "每周日 00:00"),
    ("0 0 * * MON-FRI", "工作日 00:00"),
    ("0 12 * * 5#2", "每月第 2 个周五 12:00"),
    ("0 12 * * 5L", "每月最后一个周五 12:00"),
    ("0 12 L * *", "每月最后一天 12:00"),
    ("0 12 LW * *", "每月最后一个工作日 12:00"),
    ("0 12 15W * *", "每月最接近 15 日的工作日 12:00"),
    ("*/15 * * * * *", "每 15 秒"),
    ("* * * * * *", "每秒"),
    ("0 30 9 * * *", "每天 09:30:00"),
    ("0 30 9 * * 1-5", "工作日 09:30:00"),
    ("0 */2 * * * *", "每 2 分钟"),
    ("0 0 0 1 * ?", "每月 1 日 00:00:00"),
    ("0 0 ? * 1", "每周一 00:00"),
    ("0 0 9-18 * * *", "每天 09~18 点的第 0 分"),
    ("0 0/30 9-18 * * *", "每天 09~18 点内每 30 分钟"),
    ("@daily", "每天 00:00"),
    ("@hourly", "每小时第 0 分"),
    ("@weekly", "每周日 00:00"),
    ("@monthly", "每月 1 日 00:00"),
    ("@yearly", "每年 1 月 1 日 00:00"),
    ("0 0 1 */3 *", "每 3 个月的 1 日 00:00"),
    ("0 0 * * 1,3,5", "每周一、三、五 00:00")
];

foreach ((string expression, string expected) in descriptions)
{
    Check($"{expression} → {expected}", () =>
    {
        string actual = Describe(expression);
        if (actual != expected)
        {
            Console.WriteLine($"        实际：{actual}");
            return false;
        }

        return true;
    });
}

Index("下次执行时间");
(string Expression, string Expected)[] nextRuns =
[
    ("* * * * *", "2026-11-12 11:28:00"),
    ("*/5 * * * *", "2026-11-12 11:30:00"),
    ("0 9 * * *", "2026-11-13 09:00:00"),
    ("0 0 1 * *", "2026-12-01 00:00:00"),
    ("0 0 1 3 *", "2027-03-01 00:00:00"),
    ("0 0 29 2 *", "2028-02-29 00:00:00"),
    ("0 12 * * 5#2", "2026-11-13 12:00:00"),
    ("0 12 LW * *", "2026-11-30 12:00:00"),
    ("0 12 15W * *", "2026-11-16 12:00:00"),
    ("0 12 * * 5L", "2026-11-27 12:00:00"),
    ("0 9 * * 7", "2026-11-15 09:00:00"),
    ("0 9 * JAN-MAR *", "2027-01-01 09:00:00"),
    ("@weekly", "2026-11-15 00:00:00"),
    ("*/15 * * * * *", "2026-11-12 11:27:45"),
    ("0 0 ? * 1", "2026-11-16 00:00:00"),
    ("0 9 15 * ?", "2026-11-15 09:00:00")
];

foreach ((string expression, string expected) in nextRuns)
{
    Check($"{expression} 的下一次是 {expected}", () =>
    {
        CronSchedule schedule = ParseOk(expression);
        IReadOnlyList<DateTime> next = schedule.GetNextOccurrences(now, 1, out bool limited);
        if (limited || next.Count != 1)
        {
            Console.WriteLine($"        没有算出结果（limited={limited}）");
            return false;
        }

        string actual = next[0].ToString(TimeFormat, CultureInfo.InvariantCulture);
        if (actual != expected)
        {
            Console.WriteLine($"        实际：{actual}");
            return false;
        }

        return true;
    });
}

Check("连续 3 次（每 15 秒）按顺序递增", () =>
{
    IReadOnlyList<DateTime> next = ParseOk("*/15 * * * * *").GetNextOccurrences(now, 3, out _);
    string actual = string.Join(" | ", next.Select(t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
    Console.WriteLine($"        {actual}");
    return actual == "11:27:45 | 11:28:00 | 11:28:15";
});

Check("跨年推算（每年 1 月 1 日）", () =>
{
    IReadOnlyList<DateTime> next = ParseOk("0 0 1 1 *").GetNextOccurrences(now, 2, out _);
    string actual = string.Join(" | ", next.Select(t => t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
    Console.WriteLine($"        {actual}");
    return actual == "2027-01-01 | 2028-01-01";
});

Check("5 段表达式按整分触发（秒恒为 0）", () =>
{
    IReadOnlyList<DateTime> next = ParseOk("* * * * *").GetNextOccurrences(now, 3, out _);
    return next.All(t => t.Second == 0);
});

Index("非法输入");
(string Expression, string ExpectedFragment)[] invalid =
[
    ("", "表达式为空"),
    ("   ", "表达式为空"),
    ("* * * *", "需要 5 段"),
    ("* * * * * * *", "需要 5 段"),
    ("60 * * * *", "0~59"),
    ("* 24 * * *", "0~23"),
    ("0 0 0 * *", "1~31"),
    ("0 0 * 13 *", "1~12"),
    ("0 0 * * 8", "0~7"),
    ("0 0 * * 1-5-7", "无法识别的值"),
    ("0 0 * * * * /5", "需要 5 段"),
    ("0 0 ? * ?", "不能同时写成 ?"),
    ("@reboot", "不支持"),
    ("0 0 0 L-x * *", "L-n")
];

foreach ((string expression, string fragment) in invalid)
{
    Check($"拒绝「{expression}」并提示「{fragment}」", () =>
    {
        CronParseResult result = CronSchedule.Parse(expression);
        if (result.Success)
        {
            Console.WriteLine("        居然解析成功了");
            return false;
        }

        string error = result.Error ?? string.Empty;
        if (!error.Contains(fragment, StringComparison.Ordinal))
        {
            Console.WriteLine($"        实际错误：{error}");
            return false;
        }

        return true;
    });
}

Check("永远不可能命中的表达式能快速返回空结果", () =>
{
    var watch = System.Diagnostics.Stopwatch.StartNew();
    IReadOnlyList<DateTime> next = ParseOk("0 0 30 2 *").GetNextOccurrences(now, 5, out bool limited);
    watch.Stop();
    Console.WriteLine($"        结果 {next.Count} 条，{watch.ElapsedMilliseconds} ms，limited={limited}");
    return next.Count == 0 && limited && watch.ElapsedMilliseconds < 2000;
});

Index("输出格式");
Check("默认格式带中文星期", () =>
{
    CronSchedule schedule = ParseOk("0 30 9 * * *");
    DateTime time = schedule.GetNextOccurrences(now, 1, out _)[0];
    string actual = CronSchedule.FormatTime(time, "yyyy-MM-dd ddd HH:mm:ss");
    Console.WriteLine($"        {actual}");
    return actual == "2026-11-13 周五 09:30:00";
});

Check("自定义格式生效", () =>
    CronSchedule.FormatTime(new DateTime(2026, 11, 12, 11, 27, 55), "HH:mm:ss") == "11:27:55");

Check("空格式串回退到默认格式", () =>
    CronSchedule.FormatTime(new DateTime(2026, 11, 12, 11, 27, 55), "") == "2026-11-12 周四 11:27:55");

Check("非法格式串抛 FormatException", () =>
{
    try
    {
        CronSchedule.FormatTime(now, "yyyy'MM");
        return false;
    }
    catch (FormatException)
    {
        return true;
    }
});

Index("生成器");
Check("默认配置 → 0 9 * * *", () =>
    CronExpressionBuilder.Build(new CronBuilderState()) == "0 9 * * *");

Check("包含秒数 → 0 0 9 * * *", () =>
    CronExpressionBuilder.Build(new CronBuilderState { IncludeSeconds = true }) == "0 0 9 * * *");

Check("分钟步长 → */5 * * * *", () =>
{
    var state = new CronBuilderState
    {
        Minute = new CronUnitField { Mode = CronUnitMode.Step, Step = 5 },
        Hour = new CronUnitField { Mode = CronUnitMode.Every }
    };
    return CronExpressionBuilder.Build(state) == "*/5 * * * *";
});

Check("日字段 L + 周字段 ? → 0 9 L * ?", () =>
{
    var state = new CronBuilderState
    {
        DayOfMonth = new CronDayOfMonthField { Mode = CronDayOfMonthMode.LastDay },
        DayOfWeek = new CronDayOfWeekField { Mode = CronDayOfWeekMode.Unspecified }
    };
    return CronExpressionBuilder.Build(state) == "0 9 L * ?";
});

Check("周字段 5#2 与 5L", () =>
{
    string nth = CronExpressionBuilder.Build(new CronBuilderState
    {
        DayOfWeek = new CronDayOfWeekField { Mode = CronDayOfWeekMode.NthWeekday, Weekday = 5, Nth = 2 }
    });
    string last = CronExpressionBuilder.Build(new CronBuilderState
    {
        DayOfWeek = new CronDayOfWeekField { Mode = CronDayOfWeekMode.LastWeekday, Weekday = 5 }
    });
    return nth == "0 9 * * 5#2" && last == "0 9 * * 5L";
});

Check("周字段 工作日 / 周末", () =>
{
    var weekday = new CronBuilderState { DayOfWeek = new CronDayOfWeekField { Mode = CronDayOfWeekMode.Weekdays } };
    var weekend = new CronBuilderState { DayOfWeek = new CronDayOfWeekField { Mode = CronDayOfWeekMode.Weekend } };
    return CronExpressionBuilder.Build(weekday) == "0 9 * * 1-5"
           && CronExpressionBuilder.Build(weekend) == "0 9 * * 0,6";
});

Check("月字段支持名称 → 0 9 * JAN-MAR *", () =>
{
    var state = new CronBuilderState { Month = new CronUnitField { Mode = CronUnitMode.Specific, Specific = "JAN-MAR" } };
    return CronExpressionBuilder.Build(state) == "0 9 * JAN-MAR *";
});

Check("生成器的所有组合都能被解析", () =>
{
    var values = new (string Name, Func<CronBuilderState> Create)[]
    {
        ("每秒", () => new CronBuilderState { IncludeSeconds = true, Second = new CronUnitField { Mode = CronUnitMode.Every } }),
        ("每 15 秒", () => new CronBuilderState { IncludeSeconds = true, Second = new CronUnitField { Mode = CronUnitMode.Step, Step = 15 } }),
        ("最后一天", () => new CronBuilderState { DayOfMonth = new CronDayOfMonthField { Mode = CronDayOfMonthMode.LastDay } }),
        ("最后一个工作日", () => new CronBuilderState { DayOfMonth = new CronDayOfMonthField { Mode = CronDayOfMonthMode.LastWeekday } }),
        ("15W", () => new CronBuilderState { DayOfMonth = new CronDayOfMonthField { Mode = CronDayOfMonthMode.NearestWeekday } }),
        ("每月第 1 个周一", () => new CronBuilderState { DayOfWeek = new CronDayOfWeekField { Mode = CronDayOfWeekMode.NthWeekday } }),
        ("每月最后一个周五", () => new CronBuilderState { DayOfWeek = new CronDayOfWeekField { Mode = CronDayOfWeekMode.LastWeekday, Weekday = 5 } }),
        ("月份列表", () => new CronBuilderState { Month = new CronUnitField { Mode = CronUnitMode.Specific, Specific = "3,6,9,12" } }),
        ("日列表", () => new CronBuilderState { DayOfMonth = new CronDayOfMonthField { Mode = CronDayOfMonthMode.Specific, Specific = "1,15" } }),
        ("周列表", () => new CronBuilderState { DayOfWeek = new CronDayOfWeekField { Mode = CronDayOfWeekMode.Specific, Specific = "1,3,5" } })
    };

    foreach ((string name, Func<CronBuilderState> create) in values)
    {
        string expression = CronExpressionBuilder.Build(create());
        CronParseResult result = CronSchedule.Parse(expression);
        if (!result.Success)
        {
            Console.WriteLine($"        {name}：{expression} → {result.Error}");
            return false;
        }
    }

    return true;
});

Check("生成的表达式与描述 → 0 9 * * 1-5 = 工作日 09:00", () =>
{
    string expression = CronExpressionBuilder.Build(new CronBuilderState
    {
        DayOfWeek = new CronDayOfWeekField { Mode = CronDayOfWeekMode.Weekdays }
    });
    return expression == "0 9 * * 1-5" && Describe(expression) == "工作日 09:00";
});

Index("快捷模板");
(string Name, string FiveFields, string SixFields)[] templateCases =
[
    ("每分钟", "* * * * *", "0 * * * * *"),
    ("每 5 分钟", "*/5 * * * *", "0 */5 * * * *"),
    ("每小时", "0 * * * *", "0 0 * * * *"),
    ("每天 9:00", "0 9 * * *", "0 0 9 * * *"),
    ("每天 0:00", "0 0 * * *", "0 0 0 * * *"),
    ("工作日 9:00", "0 9 * * 1-5", "0 0 9 * * 1-5"),
    ("每周一 9:00", "0 9 * * 1", "0 0 9 * * 1"),
    ("每月 1 日 0:00", "0 0 1 * *", "0 0 0 1 * *"),
    ("每年 1 月 1 日", "0 0 1 1 *", "0 0 0 1 1 *")
];

foreach ((string name, string fiveFields, string sixFields) in templateCases)
{
    Check($"模板「{name}」→ {fiveFields} / {sixFields}", () =>
    {
        CronTemplate? template = CronTemplates.All.FirstOrDefault(t => t.Name == name);
        if (template is null)
        {
            Console.WriteLine("        找不到模板");
            return false;
        }

        var state = new CronBuilderState { IncludeSeconds = false };
        template.Apply(state);
        string five = CronExpressionBuilder.Build(state);

        state.IncludeSeconds = true;
        string six = CronExpressionBuilder.Build(state);

        Console.WriteLine($"        {five}  |  {six}");
        return five == fiveFields && six == sixFields;
    });
}

Check("全部模板都能生成可解析的表达式", () =>
{
    foreach (CronTemplate template in CronTemplates.All)
    {
        foreach (bool seconds in new[] { false, true })
        {
            var state = new CronBuilderState { IncludeSeconds = seconds };
            template.Apply(state);
            string expression = CronExpressionBuilder.Build(state);
            CronParseResult result = CronSchedule.Parse(expression);
            if (!result.Success)
            {
                Console.WriteLine($"        {template.Name}：{expression} → {result.Error}");
                return false;
            }
        }
    }

    return true;
});

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

static CronSchedule ParseOk(string expression)
{
    CronParseResult result = CronSchedule.Parse(expression);
    return result.Schedule ?? throw new InvalidOperationException(result.Error);
}

string Describe(string expression) => ParseOk(expression).Describe();
