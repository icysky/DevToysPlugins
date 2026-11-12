namespace Cron.DevToys.Core;

/// <summary>Cron 表达式的字段种类。</summary>
public enum CronFieldKind
{
    Second,
    Minute,
    Hour,
    DayOfMonth,
    Month,
    DayOfWeek
}

/// <summary>
/// 解析后的单个字段：保留展开后的取值集合，同时保留 Quartz 风格的
/// L / W / # 等特殊规则（这些规则不能简单展开成取值集合）。
/// </summary>
public sealed class CronField
{
    private readonly SortedSet<int> _values = [];

    internal CronField(CronFieldKind kind, int min, int max, string text)
    {
        Kind = kind;
        Min = min;
        Max = max;
        Text = text;
    }

    public CronFieldKind Kind { get; }

    public int Min { get; }

    public int Max { get; }

    /// <summary>用户输入里的原始文本（已去空格），用于描述里识别 <c>*/n</c> 这类写法。</summary>
    public string Text { get; }

    /// <summary>是否为 <c>*</c>（步长为 1 的通配）。</summary>
    public bool IsAny { get; internal set; }

    /// <summary>是否为 <c>?</c>（Quartz 的「不指定」）。</summary>
    public bool IsUnspecified { get; internal set; }

    /// <summary>日字段：<c>L</c>（当月最后一天）。</summary>
    public bool LastDayOfMonth { get; internal set; }

    /// <summary>日字段：<c>L-n</c> 的 n（倒数第 n+1 天）。</summary>
    public int LastDayOffset { get; internal set; }

    /// <summary>日字段：<c>LW</c>（当月最后一个工作日）。</summary>
    public bool LastWeekdayOfMonth { get; internal set; }

    /// <summary>日字段：<c>15W</c>（最接近 15 日的工作日）。</summary>
    public int? NearestWeekday { get; internal set; }

    /// <summary>周字段：<c>5#2</c>（当月第 2 个周五），key 为星期（0=周日），value 为第几个。</summary>
    internal SortedDictionary<int, int> NthWeekdays { get; } = [];

    /// <summary>周字段：<c>5L</c>（当月最后一个周五）。</summary>
    internal SortedSet<int> LastWeekdays { get; } = [];

    public IReadOnlyCollection<int> Values => _values;

    public IReadOnlyDictionary<int, int> NthWeekdayMap => NthWeekdays;

    public IReadOnlyCollection<int> LastWeekdayValues => LastWeekdays;

    internal SortedSet<int> ValueSet => _values;

    /// <summary>既不是 <c>*</c> 也不是 <c>?</c>。</summary>
    public bool IsRestricted => !IsAny && !IsUnspecified;

    public bool HasSpecialRules =>
        LastDayOfMonth
        || LastWeekdayOfMonth
        || NearestWeekday.HasValue
        || NthWeekdays.Count > 0
        || LastWeekdays.Count > 0;

    /// <summary>限定为单一值（描述器用来输出「09:30」这种精确时间）。</summary>
    public bool IsSingleValue => IsRestricted && !HasSpecialRules && _values.Count == 1;

    public int SingleValue => _values.Min;

    public bool Matches(int value) => _values.Contains(value);

    /// <summary>取值是否构成等差数列（如 <c>0,5,10,…,55</c>）。</summary>
    public bool TryGetStep(out int start, out int step)
    {
        start = 0;
        step = 1;
        if (!IsRestricted || HasSpecialRules || _values.Count < 2)
        {
            return false;
        }

        int[] values = [.. _values];
        start = values[0];
        step = values[1] - values[0];

        // step == 1 就是连续区间，交给 TryGetRange 描述（避免出现「每 1 分钟」）
        if (step <= 1)
        {
            return false;
        }

        for (int i = 2; i < values.Length; i++)
        {
            if (values[i] - values[i - 1] != step)
            {
                return false;
            }
        }

        // 只有整段等差数列（一直到边界）才算「每 N」，否则只是恰好等差的列表
        return values[^1] + step > Max;
    }

    /// <summary>取值是否为连续区间（如 <c>1-5</c>）；返回区间起止。</summary>
    public bool TryGetRange(out int start, out int end)
    {
        start = 0;
        end = 0;
        if (!IsRestricted || HasSpecialRules || _values.Count < 2)
        {
            return false;
        }

        int[] values = [.. _values];
        start = values[0];
        end = values[^1];
        if (end - start + 1 != values.Length)
        {
            return false;
        }

        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] != values[i - 1] + 1)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>把字段文本解析成 <see cref="CronField"/>。</summary>
internal static class CronFieldParser
{
    private static readonly string[] MonthNames =
        ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    private static readonly string[] DayNames =
        ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];

    public static bool TryParse(string text, CronFieldKind kind, out CronField field, out string error)
    {
        (int min, int max) = RangeOf(kind);
        field = new CronField(kind, min, max, text);
        error = string.Empty;

        foreach (string part in text.Split(','))
        {
            string token = part.Trim();
            if (token.Length == 0)
            {
                error = $"「{LabelOf(kind)}」字段里有空项（连续逗号？）。";
                return false;
            }

            if (!ParseToken(field, token, out string tokenError))
            {
                error = $"「{LabelOf(kind)}」字段中的「{token}」无效：{tokenError}";
                return false;
            }
        }

        if (field.ValueSet.Count == 0 && !field.HasSpecialRules && !field.IsUnspecified)
        {
            error = $"「{LabelOf(kind)}」字段没有解析出任何值。";
            return false;
        }

        return true;
    }

    public static string LabelOf(CronFieldKind kind) => kind switch
    {
        CronFieldKind.Second => "秒",
        CronFieldKind.Minute => "分",
        CronFieldKind.Hour => "时",
        CronFieldKind.DayOfMonth => "日",
        CronFieldKind.Month => "月",
        _ => "周"
    };

    public static (int Min, int Max) RangeOf(CronFieldKind kind) => kind switch
    {
        CronFieldKind.Second => (0, 59),
        CronFieldKind.Minute => (0, 59),
        CronFieldKind.Hour => (0, 23),
        CronFieldKind.DayOfMonth => (1, 31),
        CronFieldKind.Month => (1, 12),
        _ => (0, 6)
    };

    private static bool ParseToken(CronField field, string token, out string error)
    {
        error = string.Empty;
        string text = token.ToUpperInvariant();

        if (text == "?")
        {
            if (field.Kind is not (CronFieldKind.DayOfMonth or CronFieldKind.DayOfWeek))
            {
                error = "? 只能用在「日」或「周」字段。";
                return false;
            }

            field.IsUnspecified = true;
            return true;
        }

        if (text == "*")
        {
            field.IsAny = true;
            AddRange(field, field.Min, field.Max, 1);
            return true;
        }

        if (field.Kind == CronFieldKind.DayOfMonth)
        {
            if (text == "L")
            {
                field.LastDayOfMonth = true;
                return true;
            }

            if (text == "LW")
            {
                field.LastWeekdayOfMonth = true;
                return true;
            }

            if (text.StartsWith("L-", StringComparison.Ordinal))
            {
                if (!int.TryParse(text[2..], out int offset) || offset <= 0 || offset > 30)
                {
                    error = "L-n 的 n 必须是 1~30 的整数。";
                    return false;
                }

                field.LastDayOfMonth = true;
                field.LastDayOffset = offset;
                return true;
            }

            if (text.EndsWith('W') && text.Length > 1)
            {
                if (!TryParseNumber(text[..^1], field.Kind, out int day, out error))
                {
                    return false;
                }

                if (day < 1 || day > 31)
                {
                    error = "W 前的日期必须是 1~31。";
                    return false;
                }

                field.NearestWeekday = day;
                return true;
            }
        }

        if (field.Kind == CronFieldKind.DayOfWeek)
        {
            int hash = text.IndexOf('#');
            if (hash > 0)
            {
                if (!TryParseValue(text[..hash], field.Kind, out int day, out error))
                {
                    return false;
                }

                if (!int.TryParse(text[(hash + 1)..], out int nth) || nth < 1 || nth > 5)
                {
                    error = "# 后面必须是 1~5（当月第几个星期几）。";
                    return false;
                }

                if (!field.NthWeekdays.TryAdd(Normalize(field, day), nth))
                {
                    error = "同一个星期几只能出现一次 #n。";
                    return false;
                }

                return true;
            }

            if (text.EndsWith('L') && text.Length > 1)
            {
                if (!TryParseValue(text[..^1], field.Kind, out int day, out error))
                {
                    return false;
                }

                field.LastWeekdays.Add(Normalize(field, day));
                return true;
            }
        }

        int slash = text.IndexOf('/');
        string baseText = slash >= 0 ? text[..slash] : text;
        int step = 1;
        if (slash >= 0)
        {
            if (!int.TryParse(text[(slash + 1)..], out step) || step <= 0)
            {
                error = "步长必须是正整数。";
                return false;
            }
        }

        if (baseText.Length == 0)
        {
            error = "步长前缺少起始值。";
            return false;
        }

        if (baseText == "*")
        {
            if (step == 1)
            {
                field.IsAny = true;
            }

            AddRange(field, field.Min, field.Max, step);
            return true;
        }

        // 支持 5/15 这种「从 5 开始每 15」的写法
        int dash = baseText.IndexOf('-');
        if (dash > 0)
        {
            if (!TryParseValue(baseText[..dash], field.Kind, out int start, out error)
                || !TryParseValue(baseText[(dash + 1)..], field.Kind, out int end, out error))
            {
                return false;
            }

            if (start > end)
            {
                error = $"范围 {start}-{end} 的起始值大于结束值。";
                return false;
            }

            AddRange(field, start, end, step);
            return true;
        }

        if (!TryParseValue(baseText, field.Kind, out int value, out error))
        {
            return false;
        }

        if (slash >= 0)
        {
            AddRange(field, value, field.Max, step);
        }
        else
        {
            field.ValueSet.Add(Normalize(field, value));
        }

        return true;
    }

    private static void AddRange(CronField field, int start, int end, int step)
    {
        for (int value = start; value <= end; value += step)
        {
            field.ValueSet.Add(Normalize(field, value));
        }
    }

    /// <summary>周日既可以写 0 也可以写 7，统一成 0。</summary>
    private static int Normalize(CronField field, int value)
        => field.Kind == CronFieldKind.DayOfWeek && value == 7 ? 0 : value;

    private static bool TryParseValue(string text, CronFieldKind kind, out int value, out string error)
    {
        error = string.Empty;
        value = 0;

        if (kind == CronFieldKind.Month)
        {
            int index = Array.IndexOf(MonthNames, text.ToUpperInvariant());
            if (index >= 0)
            {
                value = index + 1;
                return true;
            }
        }

        if (kind == CronFieldKind.DayOfWeek)
        {
            int index = Array.IndexOf(DayNames, text.ToUpperInvariant());
            if (index >= 0)
            {
                value = index;
                return true;
            }
        }

        return TryParseNumber(text, kind, out value, out error);
    }

    private static bool TryParseNumber(string text, CronFieldKind kind, out int value, out string error)
    {
        error = string.Empty;
        if (!int.TryParse(text, out value))
        {
            error = kind switch
            {
                CronFieldKind.Month => "无法识别的值（支持 1~12 或 JAN~DEC）。",
                CronFieldKind.DayOfWeek => "无法识别的值（支持 0~7 或 SUN~SAT）。",
                _ => "不是有效的整数。"
            };
            return false;
        }

        (int min, int max) = RangeOf(kind);
        bool valid = kind == CronFieldKind.DayOfWeek
            ? value >= 0 && value <= 7
            : value >= min && value <= max;

        if (!valid)
        {
            error = kind == CronFieldKind.DayOfWeek
                ? "取值范围是 0~7（0 和 7 都表示周日）。"
                : $"取值范围是 {min}~{max}。";
            return false;
        }

        return true;
    }
}
