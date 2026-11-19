namespace ColorSchemer.Core;

/// <summary>色板生成：底部大网格与明暗梯度。</summary>
public static class PaletteBuilder
{
    /// <summary>
    /// 生成「色相 × 明度」网格：列是色相（默认 12 列，每列 30°），行是明度（默认 5 行，从亮到暗）。
    /// 返回按行组织的颜色，方便界面按行绘制。
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<RgbColor>> BuildHueGrid(
        RgbColor baseColor,
        int columns = 12,
        int rows = 5)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);

        HsvColor hsv = ColorMath.RgbToHsv(baseColor);
        double saturation = Math.Max(hsv.S, 60);

        var grid = new List<IReadOnlyList<RgbColor>>(rows);

        for (int row = 0; row < rows; row++)
        {
            double value = rows == 1 ? 100 : 100 - row * (60.0 / (rows - 1));
            var line = new List<RgbColor>(columns);

            for (int column = 0; column < columns; column++)
            {
                double hue = hsv.H + column * (360.0 / columns);
                line.Add(ColorMath.HsvToRgb(new HsvColor(hue, saturation, value)));
            }

            grid.Add(line);
        }

        return grid;
    }

    /// <summary>从白到基色再到黑的渐变（两端不完全到纯白/纯黑，保留色相）。</summary>
    public static IReadOnlyList<RgbColor> BuildTintRamp(RgbColor baseColor, int steps = 7)
    {
        steps = Math.Max(2, steps);
        HsvColor hsv = ColorMath.RgbToHsv(baseColor);
        var colors = new List<RgbColor>(steps);

        for (int i = 0; i < steps; i++)
        {
            double t = i / (double)(steps - 1);
            double value = 100 - t * 85;
            double saturation = hsv.S * (1 - t * 0.35);
            colors.Add(ColorMath.HsvToRgb(new HsvColor(hsv.H, saturation, value)));
        }

        return colors;
    }

    /// <summary>把颜色排成 CSS 变量名（color-1、color-2…）。</summary>
    public static string ToCssVariables(IEnumerable<RgbColor> colors, string prefix = "color")
        => ColorMath.ToCssVariables(colors.Select((color, index) => ($"{prefix}-{index + 1}", color)));
}
