namespace ColorSchemer.Core;

/// <summary>配色规则。</summary>
public enum HarmonyRule
{
    /// <summary>互补（基色 + 180°）。</summary>
    Complementary,

    /// <summary>邻近（-60°、-30°、0°、+30°、+60°）。</summary>
    Analogous,

    /// <summary>三角（0°、120°、240°）。</summary>
    Triadic,

    /// <summary>分裂互补（0°、150°、210°）。</summary>
    SplitComplementary,

    /// <summary>四角（0°、90°、180°、270°）。</summary>
    Tetradic,

    /// <summary>单色（同色相，不同饱和度与明度）。</summary>
    Monochromatic,

    /// <summary>明暗梯度（同色相，从浅到深）。</summary>
    Shades,

    /// <summary>自定义角度（0°、+n°、-n°）。</summary>
    Custom
}

/// <summary>配色计算：返回基色及其和谐色（第一个永远是基色）。</summary>
public static class Harmony
{
    public static IReadOnlyList<RgbColor> Build(RgbColor baseColor, HarmonyRule rule, double customAngle = 30)
    {
        return rule switch
        {
            HarmonyRule.Complementary => Rotate(baseColor, [0, 180]),
            HarmonyRule.Analogous => Rotate(baseColor, [-60, -30, 0, 30, 60]),
            HarmonyRule.Triadic => Rotate(baseColor, [0, 120, 240]),
            HarmonyRule.SplitComplementary => Rotate(baseColor, [0, 150, 210]),
            HarmonyRule.Tetradic => Rotate(baseColor, [0, 90, 180, 270]),
            HarmonyRule.Monochromatic => Monochromatic(baseColor),
            HarmonyRule.Shades => Shades(baseColor),
            HarmonyRule.Custom => Rotate(baseColor, [0, customAngle, -customAngle]),
            _ => [baseColor]
        };
    }

    private static IReadOnlyList<RgbColor> Rotate(RgbColor baseColor, double[] offsets)
        => [.. offsets.Select(offset => ColorMath.Shift(baseColor, offset))];

    private static IReadOnlyList<RgbColor> Monochromatic(RgbColor baseColor)
    {
        HsvColor hsv = ColorMath.RgbToHsv(baseColor);

        // 同色相，饱和度与明度各取 5 档，基色放在中间
        double[] saturationScales = [0.35, 0.65, 1.0, 0.85, 0.55];
        double[] valueScales = [0.95, 1.0, 1.0, 0.8, 0.6];

        return
        [
            .. saturationScales
                .Select((s, i) => ColorMath.HsvToRgb(new HsvColor(hsv.H, hsv.S * s, hsv.V * valueScales[i])))
        ];
    }

    private static IReadOnlyList<RgbColor> Shades(RgbColor baseColor)
    {
        HsvColor hsv = ColorMath.RgbToHsv(baseColor);
        double saturation = Math.Min(hsv.S, 70);
        double[] values = [95, 80, 65, 50, 35];

        return [.. values.Select(v => ColorMath.HsvToRgb(new HsvColor(hsv.H, saturation, v)))];
    }

    /// <summary>给每种规则一个适合放进下拉框的中文名。</summary>
    public static string DisplayName(HarmonyRule rule) => rule switch
    {
        HarmonyRule.Complementary => "互补色",
        HarmonyRule.Analogous => "邻近色（±60°）",
        HarmonyRule.Triadic => "三角配色",
        HarmonyRule.SplitComplementary => "分裂互补",
        HarmonyRule.Tetradic => "四角配色",
        HarmonyRule.Monochromatic => "单色（同色相）",
        HarmonyRule.Shades => "明暗梯度",
        HarmonyRule.Custom => "自定义角度",
        _ => rule.ToString()
    };
}
