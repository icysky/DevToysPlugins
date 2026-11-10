using System.Text;

namespace ChineseIpsum.DevToys.Core;

/// <summary>
/// 字符压力分组。勾选后才会混入，默认全关，保证"简单用例"不被污染。
/// </summary>
[Flags]
internal enum IpsumStressFlags
{
    None = 0,

    /// <summary>Emoji（含 ZWJ 组合序列、肤色修饰、国旗）。</summary>
    Emoji = 1 << 0,

    /// <summary>日文假名与韩文谚文。</summary>
    EastAsian = 1 << 1,

    /// <summary>阿拉伯文 / 希伯来文（RTL）。</summary>
    Rtl = 1 << 2,

    /// <summary>数学、箭头、制表、项目符号等符号字体。</summary>
    MathSymbols = 1 << 3,

    /// <summary>带变音符号的拉丁字母（含组合音标）。</summary>
    LatinExtended = 1 << 4,

    /// <summary>上标下标、分数、带圈数字、罗马数字、货币与单位。</summary>
    SpecialForms = 1 << 5,

    /// <summary>全角字母数字。</summary>
    FullWidth = 1 << 6,

    /// <summary>不可见字符：不换行空格、零宽空格、软连字符、LRM。</summary>
    Invisible = 1 << 7,

    /// <summary>超长不可断串（40~200 个字母，用来测 overflow-wrap / word-break）。</summary>
    LongRun = 1 << 8
}

/// <summary>结构化片段分组（有格式、无意义）。</summary>
[Flags]
internal enum IpsumFragmentFlags
{
    None = 0,
    Url = 1 << 0,
    Email = 1 << 1,

    /// <summary>UUID 或哈希。</summary>
    Identifier = 1 << 2,
    Version = 1 << 3,
    DateTime = 1 << 4,
    Amount = 1 << 5,
    FilePath = 1 << 6
}

/// <summary>
/// 高级分组：一次只显示一组，避免三组同时展开把结果框压扁。
/// </summary>
internal enum IpsumAdvancedGroup
{
    None = 0,
    Stress = 1,
    Fragment = 2,
    Shape = 3
}

/// <summary>密度档位（压力字符 / 结构化片段 / 标点）。</summary>
internal enum IpsumDensity
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>段落长短倾向。</summary>
internal enum IpsumParagraphShape
{
    Short = 0,
    Mixed = 1,
    Long = 2
}

/// <summary>段落前缀样式。</summary>
internal enum IpsumListStyle
{
    None = 0,
    Bullets = 1,
    Ordered = 2
}

/// <summary>外文词前的空格策略。</summary>
internal enum IpsumSpaceStyle
{
    None = 0,
    Single = 1,
    Multiple = 2
}

/// <summary>
/// 压力字符与结构化片段的"素材库"。
///
/// 这里刻意只用两类东西：Unicode 区间（假名、谚文、阿拉伯字母按区间取）与
/// 固定格式模板（URL / UUID / 版本号等），不使用任何真实语料或词表。
/// </summary>
internal static class StressContent
{
    private static readonly string[] Emoji =
    [
        "😀", "🚀", "🎉", "👍🏽", "🧑🏾‍💻", "👨‍👩‍👧‍👦", "🇨🇳", "🇯🇵", "✅", "⚠️", "🔥", "💡", "🧩", "🛠️"
    ];

    private const string Hiragana = "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわをん";
    private const string Katakana = "アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲン";
    private const string Hangul = "가나다라마바사아자차카타파하거고구기노누느니도두드디라러로루르리마모무므미바보부브비사소수스시";

    private const string ArabicChars = "ابتثجحخدذرزسشصضطظعغفقكلمنهوي";
    private const string HebrewChars = "אבגדהוזחטיכלמנסעפצקרשת";

    private static readonly string[] MathSymbols =
    [
        "∑", "∫", "√", "±", "≠", "≤", "≥", "∞", "≈", "∝", "→", "←", "↔", "⇒", "⇔",
        "┌┬┐", "└┴┘", "├┼┤", "•", "◦", "▪", "†", "‡", "※", "№", "§", "¶", "‰", "⌘", "⏎"
    ];

    private static readonly string[] LatinExtended =
    [
        "é", "ü", "ñ", "ø", "å", "ç", "ß", "œ", "š", "ž", "č", "ł", "đ", "ğ", "ı",
        "Å", "É", "Ö", "e\u0301", "a\u0308", "n\u0303"
    ];

    private static readonly string[] SpecialForms =
    [
        "¹²³", "₀₁₂", "½", "⅓", "¾", "⅔", "①", "②", "⑦", "⑩", "Ⅰ", "Ⅱ", "Ⅲ",
        "¥", "€", "£", "₩", "₹", "₿", "℃", "℉", "㎡", "m³", "⁰", "ⁿ", "‰"
    ];

    private const string FullWidthChars =
        "ＡＢＣＤＥＦＧＨＩＪＫＬＭＮＯＰＱＲＳＴＵＶＷＸＹＺａｂｃｄｅｆｇｈｉｊｋｌｍｎｏｐｑｒｓｔｕｖｗｘｙｚ０１２３４５６７８９";

    private static readonly string[] Invisibles = ["\u00A0", "\u200B", "\u00AD", "\u200E", "\u2060"];

    private static readonly string[] Tlds = ["com", "org", "net", "io", "dev", "co.uk", "com.cn"];

    private static readonly string[] Extensions = ["js", "ts", "json", "tar.gz", "log", "png", "pdf"];

    private static readonly string[] Consonants =
        ["b", "c", "d", "f", "g", "h", "j", "k", "l", "m", "n", "p", "r", "s", "t", "v", "w", "z",
         "ch", "sh", "th", "tr", "st", "pl", "gr", "br"];

    private static readonly string[] Vowels = ["a", "e", "i", "o", "u", "ai", "ea", "oo", "ou", "ia"];

    /// <summary>取一个压力字符（不含 <see cref="IpsumStressFlags.LongRun" />，那个要单独追加）。</summary>
    public static string NextToken(IpsumStressFlags flag, Random random) => flag switch
    {
        IpsumStressFlags.Emoji => Emoji[random.Next(Emoji.Length)],
        IpsumStressFlags.EastAsian => NextEastAsian(random),
        IpsumStressFlags.Rtl => NextRtl(random),
        IpsumStressFlags.MathSymbols => MathSymbols[random.Next(MathSymbols.Length)],
        IpsumStressFlags.LatinExtended => LatinExtended[random.Next(LatinExtended.Length)],
        IpsumStressFlags.SpecialForms => SpecialForms[random.Next(SpecialForms.Length)],
        IpsumStressFlags.FullWidth => NextFullWidth(random),
        IpsumStressFlags.Invisible => NextInvisible(random),
        _ => string.Empty
    };

    public static string NextInvisible(Random random) => Invisibles[random.Next(Invisibles.Length)];

    /// <summary>超长不可断串：40~200 个连续字母，用来暴露 overflow-wrap / word-break 缺失。</summary>
    public static string NextLongRun(Random random)
    {
        int length = random.Next(40, 200);
        StringBuilder builder = new(length);
        for (int i = 0; i < length; i++)
        {
            builder.Append((char)('a' + random.Next(26)));
        }

        return builder.ToString();
    }

    /// <summary>取一个结构化片段。</summary>
    public static string NextFragment(IpsumFragmentFlags flag, Random random) => flag switch
    {
        IpsumFragmentFlags.Url => NextUrl(random),
        IpsumFragmentFlags.Email => NextEmail(random),
        IpsumFragmentFlags.Identifier => random.Next(2) == 0 ? NextUuid(random) : NextHash(random),
        IpsumFragmentFlags.Version => NextVersion(random),
        IpsumFragmentFlags.DateTime => NextDateTime(random),
        IpsumFragmentFlags.Amount => NextAmount(random),
        IpsumFragmentFlags.FilePath => NextPath(random),
        _ => string.Empty
    };

    /// <summary>把若干开关里已启用的项挑一个出来。</summary>
    public static TEnum PickFlag<TEnum>(TEnum flags, Random random)
        where TEnum : struct, Enum
    {
        int flagBits = Convert.ToInt32(flags);
        List<TEnum> enabled = [];

        foreach (TEnum value in Enum.GetValues<TEnum>())
        {
            // 单个位才算候选（跳过 None 与组合值）
            int bits = Convert.ToInt32(value);
            if (bits != 0 && (bits & (bits - 1)) == 0 && (flagBits & bits) == bits)
            {
                enabled.Add(value);
            }
        }

        return enabled.Count == 0 ? default : enabled[random.Next(enabled.Count)];
    }

    private static string NextEastAsian(Random random)
    {
        int kind = random.Next(3);
        string pool = kind switch
        {
            0 => Hiragana,
            1 => Katakana,
            _ => Hangul
        };

        int length = random.Next(2, 6);
        StringBuilder builder = new(length);
        for (int i = 0; i < length; i++)
        {
            builder.Append(pool[random.Next(pool.Length)]);
        }

        return builder.ToString();
    }

    private static string NextRtl(Random random)
    {
        string pool = random.Next(2) == 0 ? ArabicChars : HebrewChars;
        int length = random.Next(3, 9);
        StringBuilder builder = new(length);
        for (int i = 0; i < length; i++)
        {
            builder.Append(pool[random.Next(pool.Length)]);
        }

        return builder.ToString();
    }

    private static string NextFullWidth(Random random)
    {
        int length = random.Next(2, 6);
        StringBuilder builder = new(length);
        for (int i = 0; i < length; i++)
        {
            builder.Append(FullWidthChars[random.Next(FullWidthChars.Length)]);
        }

        return builder.ToString();
    }

    private static string NextUrl(Random random)
    {
        string host = NextWord(random) + "." + Tlds[random.Next(Tlds.Length)];
        int segments = random.Next(1, 4);
        StringBuilder builder = new("https://");
        builder.Append(host);

        for (int i = 0; i < segments; i++)
        {
            builder.Append('/').Append(NextWord(random));
        }

        if (random.Next(2) == 0)
        {
            builder.Append("?id=").Append(NextHex(random, 8));
        }

        return builder.ToString();
    }

    private static string NextEmail(Random random)
        => NextWord(random) + "." + NextWord(random) + "@" + NextWord(random) + "." + Tlds[random.Next(Tlds.Length)];

    private static string NextUuid(Random random)
        => string.Join(
            '-',
            NextHex(random, 8),
            NextHex(random, 4),
            NextHex(random, 4),
            NextHex(random, 4),
            NextHex(random, 12));

    private static string NextHash(Random random)
        => NextHex(random, random.Next(3) switch { 0 => 32, 1 => 40, _ => 64 });

    private static string NextVersion(Random random)
    {
        StringBuilder builder = new("v");
        builder.Append(random.Next(0, 5)).Append('.').Append(random.Next(0, 20)).Append('.').Append(random.Next(0, 50));

        int kind = random.Next(100);
        if (kind < 25)
        {
            builder.Append("-beta.").Append(random.Next(1, 12));
        }
        else if (kind < 40)
        {
            builder.Append("+build.").Append(random.Next(100, 9999));
        }

        return builder.ToString();
    }

    private static string NextDateTime(Random random)
    {
        DateTime stamp = new DateTime(2020, 1, 1).AddMinutes(random.Next(0, 60 * 24 * 365 * 6));
        return random.Next(3) switch
        {
            0 => stamp.ToString("yyyy-MM-dd HH:mm:ss"),
            1 => stamp.ToString("yyyy/MM/dd"),
            _ => stamp.ToString("MM/dd/yyyy HH:mm")
        };
    }

    private static string NextAmount(Random random)
    {
        int form = random.Next(3);
        if (form == 0)
        {
            return random.Next(1, 9_999_999).ToString("N2");
        }

        if (form == 1)
        {
            // 99.99% 这类百分比：先算成 decimal 再格式化，避免整数除法
            decimal percent = random.Next(1, 100_000) / 100m;
            return percent.ToString("0.00") + "%";
        }

        return random.Next(1, 100_000).ToString("N0") + " " + (random.Next(2) == 0 ? "kg" : "km/h");
    }

    private static string NextPath(Random random)
    {
        string ext = Extensions[random.Next(Extensions.Length)];
        int depth = random.Next(1, 4);
        StringBuilder builder = new("/var/www");

        for (int i = 0; i < depth; i++)
        {
            builder.Append('/').Append(NextWord(random));
        }

        return builder.Append('/').Append(NextWord(random)).Append('.').Append(ext).ToString();
    }

    private static string NextWord(Random random)
    {
        int syllables = random.Next(1, 4);
        StringBuilder builder = new();
        for (int i = 0; i < syllables; i++)
        {
            builder.Append(Consonants[random.Next(Consonants.Length)]);
            builder.Append(Vowels[random.Next(Vowels.Length)]);
        }

        return builder.ToString();
    }

    private static string NextHex(Random random, int length)
    {
        StringBuilder builder = new(length);
        for (int i = 0; i < length; i++)
        {
            builder.Append("0123456789abcdef"[random.Next(16)]);
        }

        return builder.ToString();
    }
}
