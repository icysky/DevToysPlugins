namespace ColorSchemer.Core;

/// <summary>RGB 颜色（0~255）。</summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static RgbColor FromBytes(int r, int g, int b)
        => new(ClampByte(r), ClampByte(g), ClampByte(b));

    public string Hex => ColorMath.ToHex(this);

    public override string ToString() => Hex;

    private static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);
}

/// <summary>HSV 颜色：H 0~360，S/V 0~100。</summary>
public readonly record struct HsvColor(double H, double S, double V);

/// <summary>HSL 颜色：H 0~360，S/L 0~100。</summary>
public readonly record struct HslColor(double H, double S, double L);

/// <summary>颜色转换与常用计算，全部为纯函数，便于回归测试。</summary>
public static class ColorMath
{
    public static string ToHex(RgbColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>支持 #RGB、#RRGGBB、#AARRGGBB（忽略 Alpha）以及不带 # 的写法。</summary>
    public static bool TryParseHex(string? text, out RgbColor color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim().TrimStart('#');

        if (value.Length == 3)
        {
            value = string.Concat(value.Select(c => new string(c, 2)));
        }
        else if (value.Length == 8)
        {
            value = value[2..];
        }

        if (value.Length != 6 || !value.All(Uri.IsHexDigit))
        {
            return false;
        }

        color = new RgbColor(
            Convert.ToByte(value[0..2], 16),
            Convert.ToByte(value[2..4], 16),
            Convert.ToByte(value[4..6], 16));

        return true;
    }

    public static HsvColor RgbToHsv(RgbColor color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0;

        if (delta > 1e-9)
        {
            if (Math.Abs(max - r) < 1e-9)
            {
                h = 60 * (((g - b) / delta) % 6);
            }
            else if (Math.Abs(max - g) < 1e-9)
            {
                h = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                h = 60 * (((r - g) / delta) + 4);
            }
        }

        if (h < 0)
        {
            h += 360;
        }

        double s = max <= 1e-9 ? 0 : delta / max;
        return new HsvColor(h % 360, s * 100, max * 100);
    }

    public static RgbColor HsvToRgb(HsvColor hsv)
    {
        double h = NormalizeHue(hsv.H);
        double s = Math.Clamp(hsv.S, 0, 100) / 100;
        double v = Math.Clamp(hsv.V, 0, 100) / 100;

        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60 % 2) - 1));
        double m = v - c;

        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };

        return RgbColor.FromBytes(
            (int)Math.Round((r + m) * 255),
            (int)Math.Round((g + m) * 255),
            (int)Math.Round((b + m) * 255));
    }

    public static HslColor RgbToHsl(RgbColor color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        double lightness = (max + min) / 2;

        double h = 0;
        double s = 0;

        if (delta > 1e-9)
        {
            s = lightness is > 0 and < 1 ? delta / (1 - Math.Abs(2 * lightness - 1)) : 0;

            if (Math.Abs(max - r) < 1e-9)
            {
                h = 60 * (((g - b) / delta) % 6);
            }
            else if (Math.Abs(max - g) < 1e-9)
            {
                h = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                h = 60 * (((r - g) / delta) + 4);
            }
        }

        if (h < 0)
        {
            h += 360;
        }

        return new HslColor(h % 360, s * 100, lightness * 100);
    }

    public static RgbColor HslToRgb(HslColor hsl)
    {
        double h = NormalizeHue(hsl.H);
        double s = Math.Clamp(hsl.S, 0, 100) / 100;
        double l = Math.Clamp(hsl.L, 0, 100) / 100;

        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h / 60 % 2) - 1));
        double m = l - c / 2;

        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };

        return RgbColor.FromBytes(
            (int)Math.Round((r + m) * 255),
            (int)Math.Round((g + m) * 255),
            (int)Math.Round((b + m) * 255));
    }

    /// <summary>按 HSV 调整（角度偏移 + 饱和度/明度乘数），常用于生成配色。</summary>
    public static RgbColor Shift(RgbColor color, double hueOffset, double saturationScale = 1, double valueScale = 1)
    {
        HsvColor hsv = RgbToHsv(color);
        return HsvToRgb(new HsvColor(
            hsv.H + hueOffset,
            hsv.S * saturationScale,
            hsv.V * valueScale));
    }

    public static RgbColor Lerp(RgbColor from, RgbColor to, double amount)
    {
        double t = Math.Clamp(amount, 0, 1);
        return RgbColor.FromBytes(
            (int)Math.Round(from.R + (to.R - from.R) * t),
            (int)Math.Round(from.G + (to.G - from.G) * t),
            (int)Math.Round(from.B + (to.B - from.B) * t));
    }

    /// <summary>WCAG 相对亮度。</summary>
    public static double RelativeLuminance(RgbColor color)
    {
        static double Channel(byte value)
        {
            double c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    /// <summary>WCAG 对比度，范围 1~21。</summary>
    public static double ContrastRatio(RgbColor a, RgbColor b)
    {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);
        double lighter = Math.Max(la, lb);
        double darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>根据背景亮度选黑或白文字，返回 true 表示用黑字。</summary>
    public static bool UseDarkForeground(RgbColor background)
        => RelativeLuminance(background) > 0.5;

    public static string ToCssVariable(string name, RgbColor color)
        => $"--{name}: {color.Hex};";

    public static string ToCssVariables(IEnumerable<(string Name, RgbColor Color)> colors)
        => ":root {\n" + string.Join("\n", colors.Select(c => "  " + ToCssVariable(c.Name, c.Color))) + "\n}";

    private static double NormalizeHue(double hue)
    {
        double h = hue % 360;
        return h < 0 ? h + 360 : h;
    }
}
