namespace Cron.DevToys.Core;

/// <summary>把解析后的 Cron 表达式翻成中文说明。</summary>
public static class CronDescriber
{
    private static readonly string[] WeekNames = ["日", "一", "二", "三", "四", "五", "六"];

    public static string Describe(CronSchedule schedule)
    {
        string date = DescribeDate(schedule);
        string time = DescribeTime(schedule);

        if (date.Length == 0)
        {
            return StartsWithEvery(time) ? time : $"每天 {time}";
        }

        if (time.Length == 0)
        {
            return date;
        }

        return $"{date} {time}";
    }

    private static bool StartsWithEvery(string text)
        => text.StartsWith('每') || text.StartsWith("第", StringComparison.Ordinal);

    // ------------------------------------------------------------------ 时间

    private static string DescribeTime(CronSchedule schedule)
    {
        // 6 段表达式里的「秒 = 0」等价于 5 段的整分触发，描述时忽略，
        // 否则每一条都会变成「每分钟第 0 秒」。
        bool zeroSeconds = schedule.HasSeconds
                           && schedule.Seconds.IsSingleValue
                           && schedule.Seconds.SingleValue == 0;

        if (schedule.Hours.IsSingleValue && schedule.Minutes.IsSingleValue)
        {
            if (!schedule.HasSeconds)
            {
                return Clock(schedule.Hours.SingleValue, schedule.Minutes.SingleValue, null);
            }

            if (schedule.Seconds.IsSingleValue)
            {
                return Clock(schedule.Hours.SingleValue, schedule.Minutes.SingleValue, schedule.Seconds.SingleValue);
            }

            return $"{Clock(schedule.Hours.SingleValue, schedule.Minutes.SingleValue, null)} 内{DescribeSeconds(schedule.Seconds)}";
        }

        bool secondsRestricted = schedule.HasSeconds && schedule.Seconds.IsRestricted && !zeroSeconds;
        if (secondsRestricted)
        {
            string secondText = DescribeSeconds(schedule.Seconds);
            if (Unrestricted(schedule.Minutes) && Unrestricted(schedule.Hours))
            {
                return secondText;
            }

            return $"{DescribeHourMinute(schedule)}内{secondText}";
        }

        if (Unrestricted(schedule.Minutes) && Unrestricted(schedule.Hours))
        {
            return schedule.HasSeconds ? "每秒" : "每分钟";
        }

        return DescribeHourMinute(schedule);
    }

    private static string DescribeHourMinute(CronSchedule schedule)
    {
        CronField minutes = schedule.Minutes;
        CronField hours = schedule.Hours;

        if (Unrestricted(hours))
        {
            if (minutes.IsSingleValue)
            {
                return $"每小时第 {minutes.SingleValue} 分";
            }

            if (minutes.TryGetRange(out int from, out int to))
            {
                return $"每小时第 {from}~{to} 分";
            }

            if (minutes.TryGetStep(out _, out int step))
            {
                return $"每 {step} 分钟";
            }

            return $"每小时第 {JoinValues(minutes)} 分";
        }

        if (Unrestricted(minutes))
        {
            if (hours.IsSingleValue)
            {
                return $"{hours.SingleValue} 点内每分钟";
            }

            if (hours.TryGetRange(out int from, out int to))
            {
                return $"{from:00}~{to:00} 点内每分钟";
            }

            if (hours.TryGetStep(out _, out int step))
            {
                return $"每 {step} 小时";
            }

            return $"{JoinValues(hours)} 点内每分钟";
        }

        // 时、分都受限
        if (hours.IsSingleValue)
        {
            if (minutes.TryGetRange(out int from, out int to))
            {
                return $"{hours.SingleValue} 点内第 {from}~{to} 分";
            }

            if (minutes.TryGetStep(out _, out int step))
            {
                return $"{hours.SingleValue} 点内每 {step} 分钟";
            }

            return $"{hours.SingleValue} 点内第 {JoinValues(minutes)} 分";
        }

        if (minutes.IsSingleValue)
        {
            if (hours.TryGetRange(out int from, out int to))
            {
                return $"{from:00}~{to:00} 点的第 {minutes.SingleValue} 分";
            }

            if (hours.TryGetStep(out _, out int step))
            {
                return $"每 {step} 小时的第 {minutes.SingleValue} 分";
            }

            return $"{JoinValues(hours)} 点的第 {minutes.SingleValue} 分";
        }

        if (hours.TryGetRange(out int hourFrom, out int hourTo) && minutes.TryGetStep(out _, out int minuteStep))
        {
            return $"{hourFrom:00}~{hourTo:00} 点内每 {minuteStep} 分钟";
        }

        return $"第 {JoinValues(minutes)} 分、{JoinValues(hours)} 点";
    }

    private static string DescribeSeconds(CronField seconds)
    {
        if (seconds.TryGetRange(out int from, out int to))
        {
            return $"第 {from}~{to} 秒";
        }

        if (seconds.TryGetStep(out _, out int step))
        {
            return $"每 {step} 秒";
        }

        if (seconds.IsSingleValue)
        {
            return $"第 {seconds.SingleValue} 秒";
        }

        return $"第 {JoinValues(seconds)} 秒";
    }

    private static string Clock(int hour, int minute, int? second)
        => second is null
            ? $"{hour:00}:{minute:00}"
            : $"{hour:00}:{minute:00}:{second.Value:00}";

    // ------------------------------------------------------------------ 日期

    private static string DescribeDate(CronSchedule schedule)
    {
        string month = DescribeMonth(schedule.Months);
        string dayOfMonth = DescribeDayOfMonth(schedule.DaysOfMonth);
        string dayOfWeek = DescribeDayOfWeek(schedule.DaysOfWeek);

        var parts = new List<string>();

        if (dayOfMonth.Length > 0)
        {
            if (month.Length > 0)
            {
                parts.Add(month.StartsWith('每') ? $"{month}的 {dayOfMonth}" : $"每年 {month} {dayOfMonth}");
            }
            else if (dayOfMonth.StartsWith('最') || dayOfMonth.StartsWith("倒数", StringComparison.Ordinal))
            {
                parts.Add($"每月{dayOfMonth}");
            }
            else
            {
                parts.Add($"每月 {dayOfMonth}");
            }
        }
        else if (month.Length > 0)
        {
            parts.Add(month.StartsWith('每') ? month : $"每年 {month}");
        }

        if (dayOfWeek.Length > 0)
        {
            parts.Add(dayOfWeek);
        }

        return string.Join("或", parts);
    }

    private static string DescribeMonth(CronField months)
    {
        if (!months.IsRestricted || months.HasSpecialRules)
        {
            return string.Empty;
        }

        if (months.IsSingleValue)
        {
            return $"{months.SingleValue} 月";
        }

        if (months.TryGetRange(out int from, out int to))
        {
            return $"{from}~{to} 月";
        }

        if (months.TryGetStep(out _, out int step))
        {
            return $"每 {step} 个月";
        }

        return $"{JoinValues(months)} 月";
    }

    private static string DescribeDayOfMonth(CronField days)
    {
        if (days.IsUnspecified || days.IsAny)
        {
            return string.Empty;
        }

        if (days.LastDayOfMonth)
        {
            return days.LastDayOffset == 0 ? "最后一天" : $"倒数第 {days.LastDayOffset + 1} 天";
        }

        if (days.LastWeekdayOfMonth)
        {
            return "最后一个工作日";
        }

        if (days.NearestWeekday is int nearest)
        {
            return $"最接近 {nearest} 日的工作日";
        }

        if (days.IsSingleValue)
        {
            return $"{days.SingleValue} 日";
        }

        if (days.TryGetRange(out int from, out int to))
        {
            return $"{from}~{to} 日";
        }

        if (days.TryGetStep(out int start, out int step))
        {
            return $"从 {start} 日起每 {step} 天";
        }

        return $"{JoinValues(days)} 日";
    }

    private static string DescribeDayOfWeek(CronField weeks)
    {
        if (weeks.IsUnspecified || weeks.IsAny)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        int[] values = [.. weeks.Values];

        if (values.Length == 5 && values.SequenceEqual([1, 2, 3, 4, 5]))
        {
            parts.Add("工作日");
        }
        else if (values.Length == 2 && values.Contains(0) && values.Contains(6))
        {
            parts.Add("周末");
        }
        else if (values.Length == 1)
        {
            parts.Add($"每周{WeekNames[values[0]]}");
        }
        else if (values.Length > 1)
        {
            parts.Add(weeks.TryGetRange(out int from, out int to)
                ? $"每周{WeekNames[from]}~{WeekNames[to]}"
                : "每周" + string.Join("、", values.Select(v => WeekNames[v])));
        }

        foreach ((int day, int nth) in weeks.NthWeekdayMap)
        {
            parts.Add($"每月第 {nth} 个周{WeekNames[day]}");
        }

        foreach (int day in weeks.LastWeekdayValues)
        {
            parts.Add($"每月最后一个周{WeekNames[day]}");
        }

        return string.Join("、", parts);
    }

    // ------------------------------------------------------------------ 工具

    private static bool Unrestricted(CronField field) => field.IsAny || field.IsUnspecified;

    private static string JoinValues(CronField field)
        => string.Join("、", field.Values);
}
