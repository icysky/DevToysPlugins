namespace Cron.DevToys.Core;

/// <summary>秒 / 分 / 时 / 月这类「纯数值」字段的生成方式。</summary>
public enum CronUnitMode
{
    /// <summary>*</summary>
    Every,

    /// <summary>*/n</summary>
    Step,

    /// <summary>用户直接填写的多种写法（1,15 / 1-5 / JAN-MAR 等）</summary>
    Specific
}

/// <summary>「日」字段的生成方式。</summary>
public enum CronDayOfMonthMode
{
    Every,
    Step,
    Specific,

    /// <summary>L</summary>
    LastDay,

    /// <summary>LW</summary>
    LastWeekday,

    /// <summary>15W</summary>
    NearestWeekday
}

/// <summary>「周」字段的生成方式。</summary>
public enum CronDayOfWeekMode
{
    Every,

    /// <summary>?（Quartz 语义，交给「日」字段判断）</summary>
    Unspecified,

    Specific,

    /// <summary>周一至周五</summary>
    Weekdays,

    /// <summary>周六、周日</summary>
    Weekend,

    /// <summary>5#2 当月第 2 个周五</summary>
    NthWeekday,

    /// <summary>5L 当月最后一个周五</summary>
    LastWeekday
}

/// <summary>一个「纯数值」字段的生成配置。</summary>
public sealed class CronUnitField
{
    public CronUnitMode Mode { get; set; } = CronUnitMode.Every;

    public int Step { get; set; } = 5;

    public string Specific { get; set; } = string.Empty;

    public string ToExpression()
    {
        return Mode switch
        {
            CronUnitMode.Step => $"*/{Math.Max(1, Step)}",
            CronUnitMode.Specific => Normalize(Specific),
            _ => "*"
        };
    }

    internal static string Normalize(string? text)
        => string.IsNullOrWhiteSpace(text) ? "*" : text.Trim();
}

/// <summary>「日」字段的生成配置。</summary>
public sealed class CronDayOfMonthField
{
    public CronDayOfMonthMode Mode { get; set; } = CronDayOfMonthMode.Every;

    public int Step { get; set; } = 2;

    public string Specific { get; set; } = "1";

    /// <summary>「15W」里的 15。</summary>
    public int NearestDay { get; set; } = 15;

    public string ToExpression()
    {
        return Mode switch
        {
            CronDayOfMonthMode.Step => $"*/{Math.Max(1, Step)}",
            CronDayOfMonthMode.Specific => CronUnitField.Normalize(Specific),
            CronDayOfMonthMode.LastDay => "L",
            CronDayOfMonthMode.LastWeekday => "LW",
            CronDayOfMonthMode.NearestWeekday => $"{Math.Clamp(NearestDay, 1, 31)}W",
            _ => "*"
        };
    }
}

/// <summary>「周」字段的生成配置。</summary>
public sealed class CronDayOfWeekField
{
    public CronDayOfWeekMode Mode { get; set; } = CronDayOfWeekMode.Every;

    public string Specific { get; set; } = "1";

    /// <summary>0=周日 … 6=周六。</summary>
    public int Weekday { get; set; } = 1;

    /// <summary>第几个（1~5）。</summary>
    public int Nth { get; set; } = 1;

    public string ToExpression()
    {
        int day = Math.Clamp(Weekday, 0, 6);

        return Mode switch
        {
            CronDayOfWeekMode.Unspecified => "?",
            CronDayOfWeekMode.Specific => CronUnitField.Normalize(Specific),
            CronDayOfWeekMode.Weekdays => "1-5",
            CronDayOfWeekMode.Weekend => "0,6",
            CronDayOfWeekMode.NthWeekday => $"{day}#{Math.Clamp(Nth, 1, 5)}",
            CronDayOfWeekMode.LastWeekday => $"{day}L",
            _ => "*"
        };
    }
}

/// <summary>生成器的完整配置。</summary>
public sealed class CronBuilderState
{
    public bool IncludeSeconds { get; set; }

    public CronUnitField Second { get; set; } = new() { Mode = CronUnitMode.Specific, Specific = "0" };

    public CronUnitField Minute { get; set; } = new() { Mode = CronUnitMode.Specific, Specific = "0" };

    public CronUnitField Hour { get; set; } = new() { Mode = CronUnitMode.Specific, Specific = "9" };

    public CronDayOfMonthField DayOfMonth { get; set; } = new();

    public CronUnitField Month { get; set; } = new();

    public CronDayOfWeekField DayOfWeek { get; set; } = new();
}

/// <summary>把生成器配置拼成 Cron 表达式。</summary>
public static class CronExpressionBuilder
{
    public static string Build(CronBuilderState state)
    {
        var parts = new List<string>(6);

        if (state.IncludeSeconds)
        {
            parts.Add(state.Second.ToExpression());
        }

        parts.Add(state.Minute.ToExpression());
        parts.Add(state.Hour.ToExpression());
        parts.Add(state.DayOfMonth.ToExpression());
        parts.Add(state.Month.ToExpression());
        parts.Add(state.DayOfWeek.ToExpression());

        return string.Join(' ', parts);
    }
}
