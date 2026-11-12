using System.Globalization;

namespace Cron.DevToys.Core;

/// <summary>Cron 解析结果。</summary>
public sealed class CronParseResult
{
    private CronParseResult(CronSchedule? schedule, string? error)
    {
        Schedule = schedule;
        Error = error;
    }

    public CronSchedule? Schedule { get; }

    public string? Error { get; }

    public bool Success => Schedule is not null;

    public static CronParseResult Ok(CronSchedule schedule) => new(schedule, null);

    public static CronParseResult Fail(string error) => new(null, error);
}

/// <summary>
/// 解析后的 Cron 表达式：负责字段匹配与「下次执行时间」推算。
/// 支持标准 5 段（分 时 日 月 周）、Quartz 6 段（秒 分 时 日 月 周），
/// 以及 <c>@yearly</c> 之类的宏。
/// </summary>
public sealed class CronSchedule
{
    /// <summary>推算下次执行时间时最多向后看多少年（用于判定永远不可能命中的表达式）。</summary>
    public const int SearchYears = 8;

    private static readonly Dictionary<string, string> Macros = new(StringComparer.OrdinalIgnoreCase)
    {
        ["@yearly"] = "0 0 1 1 *",
        ["@annually"] = "0 0 1 1 *",
        ["@monthly"] = "0 0 1 * *",
        ["@weekly"] = "0 0 * * 0",
        ["@daily"] = "0 0 * * *",
        ["@midnight"] = "0 0 * * *",
        ["@hourly"] = "0 * * * *"
    };

    private CronSchedule(
        string expression,
        bool hasSeconds,
        CronField seconds,
        CronField minutes,
        CronField hours,
        CronField daysOfMonth,
        CronField months,
        CronField daysOfWeek)
    {
        Expression = expression;
        HasSeconds = hasSeconds;
        Seconds = seconds;
        Minutes = minutes;
        Hours = hours;
        DaysOfMonth = daysOfMonth;
        Months = months;
        DaysOfWeek = daysOfWeek;
    }

    /// <summary>规范化后的表达式文本（宏会被展开）。</summary>
    public string Expression { get; }

    public bool HasSeconds { get; }

    public CronField Seconds { get; }

    public CronField Minutes { get; }

    public CronField Hours { get; }

    public CronField DaysOfMonth { get; }

    public CronField Months { get; }

    public CronField DaysOfWeek { get; }

    public int FieldCount => HasSeconds ? 6 : 5;

    public static CronParseResult Parse(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return CronParseResult.Fail("表达式为空。");
        }

        string text = expression.Trim();

        if (text.StartsWith('@'))
        {
            if (!Macros.TryGetValue(text, out string? expanded))
            {
                return CronParseResult.Fail(
                    $"不支持「{text}」。可用宏：@yearly、@annually、@monthly、@weekly、@daily、@midnight、@hourly。");
            }

            text = expanded;
        }

        string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (5 or 6))
        {
            return CronParseResult.Fail(
                $"需要 5 段（分 时 日 月 周）或 6 段（秒 分 时 日 月 周）表达式，当前是 {parts.Length} 段。");
        }

        bool hasSeconds = parts.Length == 6;
        int index = 0;

        CronField seconds;
        if (hasSeconds)
        {
            if (!CronFieldParser.TryParse(parts[index++], CronFieldKind.Second, out seconds, out string secondError))
            {
                return CronParseResult.Fail(secondError);
            }
        }
        else
        {
            // 5 段表达式等价于「秒固定为 0」
            seconds = CreateFixed(CronFieldKind.Second, 0, "0");
        }

        if (!CronFieldParser.TryParse(parts[index++], CronFieldKind.Minute, out CronField minutes, out string error)
            || !CronFieldParser.TryParse(parts[index++], CronFieldKind.Hour, out CronField hours, out error)
            || !CronFieldParser.TryParse(parts[index++], CronFieldKind.DayOfMonth, out CronField daysOfMonth, out error)
            || !CronFieldParser.TryParse(parts[index++], CronFieldKind.Month, out CronField months, out error)
            || !CronFieldParser.TryParse(parts[index++], CronFieldKind.DayOfWeek, out CronField daysOfWeek, out error))
        {
            return CronParseResult.Fail(error);
        }

        if (daysOfMonth.IsUnspecified && daysOfWeek.IsUnspecified)
        {
            return CronParseResult.Fail("「日」和「周」不能同时写成 ?，至少要有一个指定。");
        }

        string normalized = string.Join(' ', parts);
        return CronParseResult.Ok(new CronSchedule(
            normalized, hasSeconds, seconds, minutes, hours, daysOfMonth, months, daysOfWeek));
    }

    /// <summary>
    /// 推算出严格晚于 <paramref name="after"/> 的若干个执行时间。
    /// </summary>
    /// <param name="searchLimitReached">为 true 表示在 <see cref="SearchYears"/> 年内没能凑够数量。</param>
    public IReadOnlyList<DateTime> GetNextOccurrences(DateTime after, int count, out bool searchLimitReached)
    {
        var results = new List<DateTime>(Math.Clamp(count, 0, 200));
        searchLimitReached = false;

        if (count <= 0)
        {
            return results;
        }

        DateTime start = RoundUp(after);
        DateTime lastDay = start.Date.AddYears(SearchYears);
        DateTime day = start.Date;
        bool hitLimit = false;

        while (results.Count < count)
        {
            if (day > lastDay)
            {
                hitLimit = true;
                break;
            }

            if (MonthMatches(day) && DayMatches(day))
            {
                foreach (int hour in Hours.Values)
                {
                    if (day == start.Date && hour < start.Hour)
                    {
                        continue;
                    }

                    foreach (int minute in Minutes.Values)
                    {
                        if (day == start.Date && hour == start.Hour && minute < start.Minute)
                        {
                            continue;
                        }

                        foreach (int second in Seconds.Values)
                        {
                            if (day == start.Date && hour == start.Hour && minute == start.Minute && second < start.Second)
                            {
                                continue;
                            }

                            DateTime candidate = new(day.Year, day.Month, day.Day, hour, minute, second, after.Kind);
                            if (candidate < start)
                            {
                                continue;
                            }

                            results.Add(candidate);
                            if (results.Count >= count)
                            {
                                break;
                            }
                        }

                        if (results.Count >= count)
                        {
                            break;
                        }
                    }

                    if (results.Count >= count)
                    {
                        break;
                    }
                }
            }

            day = day.AddDays(1);
        }

        searchLimitReached = hitLimit || results.Count < count;
        return results;
    }

    public string Describe() => CronDescriber.Describe(this);

    private DateTime RoundUp(DateTime after)
    {
        DateTime value = new(
            after.Year, after.Month, after.Day,
            after.Hour, after.Minute, HasSeconds ? after.Second : 0,
            after.Kind);

        if (!HasSeconds)
        {
            return value.AddMinutes(1);
        }

        return value.AddSeconds(1);
    }

    private bool MonthMatches(DateTime day) => Months.Matches(day.Month);

    private bool DayMatches(DateTime day)
    {
        bool domSpecified = !DaysOfMonth.IsAny && !DaysOfMonth.IsUnspecified;
        bool dowSpecified = !DaysOfWeek.IsAny && !DaysOfWeek.IsUnspecified;

        bool domOk = !domSpecified || DayOfMonthMatches(day);
        bool dowOk = !dowSpecified || DayOfWeekMatches(day);

        // Quartz 的 ? 表示「这一项不参与判断」
        if (DaysOfMonth.IsUnspecified)
        {
            return dowOk;
        }

        if (DaysOfWeek.IsUnspecified)
        {
            return domOk;
        }

        if (!domSpecified && !dowSpecified)
        {
            return true;
        }

        if (!domSpecified)
        {
            return dowOk;
        }

        if (!dowSpecified)
        {
            return domOk;
        }

        // 标准 cron：日与周同时受限时取「或」
        return domOk || dowOk;
    }

    private bool DayOfMonthMatches(DateTime day)
    {
        if (DaysOfMonth.Matches(day.Day))
        {
            return true;
        }

        int daysInMonth = DateTime.DaysInMonth(day.Year, day.Month);

        if (DaysOfMonth.LastDayOfMonth && day.Day == daysInMonth - DaysOfMonth.LastDayOffset)
        {
            return true;
        }

        if (DaysOfMonth.LastWeekdayOfMonth && IsLastWeekdayOfMonth(day))
        {
            return true;
        }

        if (DaysOfMonth.NearestWeekday is int target && day.Day == NearestWeekdayOf(year: day.Year, month: day.Month, target))
        {
            return true;
        }

        return false;
    }

    private bool DayOfWeekMatches(DateTime day)
    {
        int weekDay = (int)day.DayOfWeek;

        if (DaysOfWeek.Matches(weekDay))
        {
            return true;
        }

        if (DaysOfWeek.NthWeekdayMap.TryGetValue(weekDay, out int nth) && ((day.Day - 1) / 7) + 1 == nth)
        {
            return true;
        }

        if (DaysOfWeek.LastWeekdayValues.Contains(weekDay) && day.AddDays(7).Month != day.Month)
        {
            return true;
        }

        return false;
    }

    private static bool IsWeekday(DateTime day)
        => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>是否为当月最后一个工作日（同月后面不再有工作日）。</summary>
    private static bool IsLastWeekdayOfMonth(DateTime day)
    {
        if (!IsWeekday(day))
        {
            return false;
        }

        DateTime cursor = day.AddDays(1);
        while (cursor.Month == day.Month)
        {
            if (IsWeekday(cursor))
            {
                return false;
            }

            cursor = cursor.AddDays(1);
        }

        return true;
    }

    /// <summary>「15W」：最接近 15 日的工作日，且不跨月。</summary>
    private static int NearestWeekdayOf(int year, int month, int target)
    {
        int daysInMonth = DateTime.DaysInMonth(year, month);
        int day = Math.Clamp(target, 1, daysInMonth);
        DateTime date = new(year, month, day);

        if (date.DayOfWeek == DayOfWeek.Saturday)
        {
            date = day > 1 ? date.AddDays(-1) : date.AddDays(2);
        }
        else if (date.DayOfWeek == DayOfWeek.Sunday)
        {
            date = day < daysInMonth ? date.AddDays(1) : date.AddDays(-2);
        }

        return date.Day;
    }

    private static CronField CreateFixed(CronFieldKind kind, int value, string text)
    {
        (int min, int max) = CronFieldParser.RangeOf(kind);
        CronField field = new(kind, min, max, text);
        field.ValueSet.Add(value);
        return field;
    }

    /// <summary>按当前区域格式化时间；格式串非法时抛 <see cref="FormatException"/>。</summary>
    public static string FormatTime(DateTime time, string? format)
    {
        string actual = string.IsNullOrWhiteSpace(format) ? "yyyy-MM-dd ddd HH:mm:ss" : format;
        return time.ToString(actual, CultureInfo.CurrentCulture);
    }
}
