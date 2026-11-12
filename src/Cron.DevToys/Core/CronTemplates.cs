namespace Cron.DevToys.Core;

/// <summary>一个快捷模板：把常见计划直接写进生成器状态。</summary>
public sealed class CronTemplate
{
    private readonly Action<CronBuilderState> _apply;

    internal CronTemplate(string name, Action<CronBuilderState> apply)
    {
        Name = name;
        _apply = apply;
    }

    public string Name { get; }

    public void Apply(CronBuilderState state) => _apply(state);
}

/// <summary>内置的快捷模板集合。</summary>
public static class CronTemplates
{
    public static IReadOnlyList<CronTemplate> All { get; } =
    [
        new("每分钟", state => Apply(state, Every(), Every())),
        new("每 5 分钟", state => Apply(state, Step(5), Every())),
        new("每小时", state => Apply(state, Zero(), Every())),
        new("每天 9:00", state => Apply(state, Zero(), Value(9))),
        new("每天 0:00", state => Apply(state, Zero(), Value(0))),
        new("工作日 9:00", state => Apply(state, Zero(), Value(9), dayOfWeek: Weekdays())),
        new("每周一 9:00", state => Apply(state, Zero(), Value(9), dayOfWeek: WeekDay(1))),
        new("每月 1 日 0:00", state => Apply(state, Zero(), Value(0), dayOfMonth: MonthDay(1))),
        new("每年 1 月 1 日", state => Apply(state, Zero(), Value(0), dayOfMonth: MonthDay(1), month: Value(1)))
    ];

    private static void Apply(
        CronBuilderState state,
        CronUnitField minute,
        CronUnitField hour,
        CronDayOfMonthField? dayOfMonth = null,
        CronUnitField? month = null,
        CronDayOfWeekField? dayOfWeek = null)
    {
        // 秒固定为 0：5 段表达式时该字段不输出，6 段时就是整秒触发
        state.Second = Zero();
        state.Minute = minute;
        state.Hour = hour;
        state.DayOfMonth = dayOfMonth ?? new CronDayOfMonthField();
        state.Month = month ?? new CronUnitField();
        state.DayOfWeek = dayOfWeek ?? new CronDayOfWeekField();
    }

    private static CronUnitField Every() => new() { Mode = CronUnitMode.Every };

    private static CronUnitField Zero() => new() { Mode = CronUnitMode.Specific, Specific = "0" };

    private static CronUnitField Value(int value)
        => new() { Mode = CronUnitMode.Specific, Specific = value.ToString() };

    private static CronUnitField Step(int step) => new() { Mode = CronUnitMode.Step, Step = step };

    private static CronDayOfWeekField Weekdays() => new() { Mode = CronDayOfWeekMode.Weekdays };

    private static CronDayOfWeekField WeekDay(int day)
        => new() { Mode = CronDayOfWeekMode.Specific, Specific = day.ToString() };

    private static CronDayOfMonthField MonthDay(int day)
        => new() { Mode = CronDayOfMonthMode.Specific, Specific = day.ToString() };
}
