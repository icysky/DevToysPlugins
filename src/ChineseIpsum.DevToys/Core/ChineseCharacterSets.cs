using System.Text;

namespace ChineseIpsum.DevToys.Core;

/// <summary>
/// 常用字 / 生僻字字表。
///
/// 直接取 Unicode 基本区（U+4E00–U+9FA5）当"常用字"是错的——那两万多个字里大量是生僻字，
/// 所以「不勾生僻字」也会满屏怪字。这里改用 GB2312 的编码分区，不需要内置任何字表文件：
///
/// - 一级汉字（0xB0A1–0xD7F9，3755 字）：日常常用字
/// - 二级汉字（0xD8A1–0xF7FE，3008 字）：次常用 / 生僻
///
/// GB2312 通过 <see cref="CodePagesEncodingProvider" /> 提供（.NET 8 共享框架自带该程序集）；
/// 万一取不到，退回"基本区 = 常用字"，此时生僻字开关只体现为是否混入 CJK 扩展区字符。
/// </summary>
internal static class ChineseCharacterSets
{
    private static readonly Lazy<Sets> LazySets = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>日常常用字（GB2312 一级汉字）。</summary>
    public static IReadOnlyList<char> Common => LazySets.Value.Common;

    /// <summary>次常用 / 生僻字（GB2312 二级汉字）。</summary>
    public static IReadOnlyList<char> Rare => LazySets.Value.Rare;

    /// <summary>是否成功用上了 GB2312 分区（false 表示走了回退逻辑）。</summary>
    public static bool UsesGb2312 => LazySets.Value.Gb2312;

    private static Sets Build()
    {
        List<char> common = [];
        List<char> rare = [];

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // 用异常回退，这样非法组合会抛 DecoderFallbackException，直接跳过
            Encoding encoding = Encoding.GetEncoding(
                936,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);

            byte[] buffer = new byte[2];
            for (int lead = 0xB0; lead <= 0xF7; lead++)
            {
                for (int trail = 0xA1; trail <= 0xFE; trail++)
                {
                    buffer[0] = (byte)lead;
                    buffer[1] = (byte)trail;

                    string text;
                    try
                    {
                        text = encoding.GetString(buffer);
                    }
                    catch (DecoderFallbackException)
                    {
                        continue;
                    }

                    if (text.Length == 1 && IsHan(text[0]))
                    {
                        // 一级区为常用字，二级区为生僻字
                        (lead <= 0xD7 ? common : rare).Add(text[0]);
                    }
                }
            }
        }
        catch
        {
            common.Clear();
            rare.Clear();
        }

        if (common.Count == 0)
        {
            // 回退：没有码表可用时，用 Unicode 基本区当常用字
            for (int code = 0x4E00; code <= 0x9FA5; code++)
            {
                common.Add((char)code);
            }

            return new Sets([.. common], [], Gb2312: false);
        }

        return new Sets([.. common], [.. rare], Gb2312: true);
    }

    private static bool IsHan(char value)
        => value is >= (char)0x4E00 and <= (char)0x9FFF
           || value is >= (char)0x3400 and <= (char)0x4DBF;

    private sealed record Sets(char[] Common, char[] Rare, bool Gb2312);
}
