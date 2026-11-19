using ColorSchemer.Core;

int passed = 0;
int failed = 0;

Console.WriteLine("=== ColorSchemer 颜色核心冒烟测试 ===");
Console.WriteLine();

Index("HEX 解析");
Check("#RRGGBB", () =>
{
    bool ok = ColorMath.TryParseHex("#FF8000", out RgbColor color);
    Console.WriteLine($"        {color.Hex}");
    return ok && color is { R: 255, G: 128, B: 0 };
});

Check("#RGB 简写", () =>
{
    bool ok = ColorMath.TryParseHex("#F00", out RgbColor color);
    return ok && color == new RgbColor(255, 0, 0);
});

Check("不带 # / 8 位 ARGB", () =>
{
    return ColorMath.TryParseHex("00FF00", out RgbColor a) && a == new RgbColor(0, 255, 0)
           && ColorMath.TryParseHex("#80FF0000", out RgbColor b) && b == new RgbColor(255, 0, 0);
});

Check("非法输入", () => !ColorMath.TryParseHex("xyz", out _) && !ColorMath.TryParseHex("#12345", out _));

Index("RGB / HSV / HSL 互转");
RgbColor[] samples =
[
    new(255, 0, 0), new(0, 255, 0), new(0, 0, 255),
    new(255, 255, 255), new(0, 0, 0), new(128, 128, 128),
    new(255, 128, 0), new(18, 52, 86), new(240, 240, 240)
];

foreach (RgbColor sample in samples)
{
    Check($"HSV 往返 {sample.Hex}", () =>
    {
        RgbColor roundTrip = ColorMath.HsvToRgb(ColorMath.RgbToHsv(sample));
        Console.WriteLine($"        {sample.Hex} -> {roundTrip.Hex}");
        return Distance(sample, roundTrip) <= 2;
    });

    Check($"HSL 往返 {sample.Hex}", () =>
    {
        RgbColor roundTrip = ColorMath.HslToRgb(ColorMath.RgbToHsl(sample));
        Console.WriteLine($"        {sample.Hex} -> {roundTrip.Hex}");
        return Distance(sample, roundTrip) <= 2;
    });
}

Check("红色 HSV = 0°/100%/100%", () =>
{
    HsvColor hsv = ColorMath.RgbToHsv(new RgbColor(255, 0, 0));
    return Math.Abs(hsv.H) < 0.01 && Math.Abs(hsv.S - 100) < 0.01 && Math.Abs(hsv.V - 100) < 0.01;
});

Check("Shift 180° 得互补色", () =>
{
    RgbColor complement = ColorMath.Shift(new RgbColor(255, 0, 0), 180);
    Console.WriteLine($"        {complement.Hex}");
    return complement is { R: 0, G: 255, B: 255 };
});

Index("配色规则");
Check("互补 2 色", () => Harmony.Build(new RgbColor(255, 0, 0), HarmonyRule.Complementary).Count == 2);
Check("邻近 5 色", () => Harmony.Build(new RgbColor(255, 0, 0), HarmonyRule.Analogous).Count == 5);
Check("三角 3 色", () => Harmony.Build(new RgbColor(255, 0, 0), HarmonyRule.Triadic).Count == 3);
Check("分裂互补 3 色", () => Harmony.Build(new RgbColor(255, 0, 0), HarmonyRule.SplitComplementary).Count == 3);
Check("四角 4 色", () => Harmony.Build(new RgbColor(255, 0, 0), HarmonyRule.Tetradic).Count == 4);
Check("单色 5 色且色相不变", () =>
{
    IReadOnlyList<RgbColor> colors = Harmony.Build(new RgbColor(30, 144, 255), HarmonyRule.Monochromatic);
    double baseHue = ColorMath.RgbToHsv(new RgbColor(30, 144, 255)).H;
    Console.WriteLine("        " + string.Join(" ", colors.Select(c => c.Hex)));
    return colors.Count == 5 && colors.All(c => Math.Abs(ColorMath.RgbToHsv(c).H - baseHue) < 1.5);
});

Check("明暗梯度从浅到深", () =>
{
    IReadOnlyList<RgbColor> shades = Harmony.Build(new RgbColor(30, 144, 255), HarmonyRule.Shades);
    double first = ColorMath.RelativeLuminance(shades[0]);
    double last = ColorMath.RelativeLuminance(shades[^1]);
    Console.WriteLine("        " + string.Join(" ", shades.Select(c => c.Hex)));
    return shades.Count == 5 && first > last;
});

Check("自定义角度包含基色", () =>
{
    IReadOnlyList<RgbColor> colors = Harmony.Build(new RgbColor(10, 20, 30), HarmonyRule.Custom, 45);
    return colors.Count == 3 && colors[0] == new RgbColor(10, 20, 30);
});

Index("色板与对比度");
Check("色板网格 12×5", () =>
{
    IReadOnlyList<IReadOnlyList<RgbColor>> grid = PaletteBuilder.BuildHueGrid(new RgbColor(255, 0, 0));
    Console.WriteLine($"        行={grid.Count} 列={grid[0].Count} 首格={grid[0][0].Hex}");
    return grid.Count == 5 && grid.All(row => row.Count == 12);
});

Check("色板第一行第一列是基色色相", () =>
{
    IReadOnlyList<IReadOnlyList<RgbColor>> grid = PaletteBuilder.BuildHueGrid(new RgbColor(0, 128, 255));
    double baseHue = ColorMath.RgbToHsv(new RgbColor(0, 128, 255)).H;
    double cellHue = ColorMath.RgbToHsv(grid[0][0]).H;
    return Math.Abs(baseHue - cellHue) < 1.5;
});

Check("梯度长度与亮度递减", () =>
{
    IReadOnlyList<RgbColor> ramp = PaletteBuilder.BuildTintRamp(new RgbColor(200, 20, 60), 7);
    return ramp.Count == 7
           && ColorMath.RelativeLuminance(ramp[0]) > ColorMath.RelativeLuminance(ramp[^1]);
});

Check("黑白对比度接近 21", () =>
{
    double ratio = ColorMath.ContrastRatio(new RgbColor(0, 0, 0), new RgbColor(255, 255, 255));
    Console.WriteLine($"        {ratio:F2}");
    return Math.Abs(ratio - 21) < 0.1;
});

Check("CSS 变量输出", () =>
{
    string css = PaletteBuilder.ToCssVariables([new RgbColor(255, 0, 0), new RgbColor(0, 255, 0)]);
    Console.WriteLine(css);
    return css.Contains("--color-1: #FF0000;", StringComparison.Ordinal)
           && css.Contains("--color-2: #00FF00;", StringComparison.Ordinal)
           && css.StartsWith(":root {", StringComparison.Ordinal);
});

Console.WriteLine();
Console.WriteLine($"===== 通过 {passed} 项，失败 {failed} 项 =====");
Console.WriteLine(failed == 0 ? "全部通过 ✅" : "存在失败 ❌");
return failed == 0 ? 0 : 1;

static int Distance(RgbColor a, RgbColor b)
    => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

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
