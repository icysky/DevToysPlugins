using System.Collections.ObjectModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ColorSchemer.Controls;
using ColorSchemer.Core;
using ColorSchemer.Models;
using ColorSchemer.Services;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingSize = System.Drawing.Size;
using WpfColor = System.Windows.Media.Color;

namespace ColorSchemer;

public partial class MainWindow : Window
{
    private static readonly RgbColor DefaultColor = new(229, 57, 53);

    private readonly ObservableCollection<ColorItem> _matching = [];
    private readonly ObservableCollection<ColorItem> _favorites = [];
    private readonly FavoritesStore _favoritesStore = new();

    private RgbColor _color = DefaultColor;
    private HarmonyRule _rule = HarmonyRule.Complementary;
    private double _customAngle = 30;
    private bool _updating;

    public MainWindow()
    {
        InitializeComponent();

        MatchingList.ItemsSource = _matching;
        FavoritesList.ItemsSource = _favorites;

        HarmonyCombo.ItemsSource = Enum.GetValues<HarmonyRule>()
            .Select(rule => new HarmonyOption(Harmony.DisplayName(rule), rule))
            .ToList();
        HarmonyCombo.SelectedIndex = 0;

        LoadFavorites();
        SetColor(DefaultColor);
    }

    // ---------------------------------------------------------------- 状态与界面

    private void SetColor(RgbColor color)
    {
        _color = color;
        UpdateUi();
    }

    private void UpdateUi()
    {
        _updating = true;

        try
        {
            HsvColor hsv = ColorMath.RgbToHsv(_color);

            BasePreview.Background = new SolidColorBrush(WpfColor.FromRgb(_color.R, _color.G, _color.B));
            HexBox.Text = _color.Hex;

            RSlider.Value = _color.R;
            GSlider.Value = _color.G;
            BSlider.Value = _color.B;
            RBox.Text = _color.R.ToString();
            GBox.Text = _color.G.ToString();
            BBox.Text = _color.B.ToString();

            HSlider.Value = hsv.H;
            SSlider.Value = hsv.S;
            VSlider.Value = hsv.V;
            HBox.Text = Math.Round(hsv.H).ToString("0");
            SBox.Text = Math.Round(hsv.S).ToString("0");
            VBox.Text = Math.Round(hsv.V).ToString("0");

            Wheel.SelectedColor = WpfColor.FromRgb(_color.R, _color.G, _color.B);

            double black = ColorMath.ContrastRatio(_color, new RgbColor(0, 0, 0));
            double white = ColorMath.ContrastRatio(_color, new RgbColor(255, 255, 255));
            ContrastText.Text = $"对比度（WCAG）：黑字 {black:F1}:1 · 白字 {white:F1}:1";

            Palette.SetColors(PaletteBuilder.BuildHueGrid(_color));
            RebuildMatching();
        }
        finally
        {
            _updating = false;
        }
    }

    private void RebuildMatching()
    {
        IReadOnlyList<RgbColor> colors = Harmony.Build(_color, _rule, _customAngle);
        string[] labels = BuildLabels(_rule, colors.Count);

        _matching.Clear();

        for (int i = 0; i < colors.Count; i++)
        {
            _matching.Add(CreateItem(labels[i], colors[i]));
        }
    }

    private static string[] BuildLabels(HarmonyRule rule, int count) => rule switch
    {
        HarmonyRule.Complementary => ["基色", "互补"],
        HarmonyRule.Analogous => ["-60°", "-30°", "基色", "+30°", "+60°"],
        HarmonyRule.Triadic => ["基色", "+120°", "-120°"],
        HarmonyRule.SplitComplementary => ["基色", "+150°", "-150°"],
        HarmonyRule.Tetradic => ["基色", "+90°", "互补", "-90°"],
        HarmonyRule.Monochromatic => ["浅色 1", "浅色 2", "基色", "深色 1", "深色 2"],
        HarmonyRule.Shades => ["明度 95%", "80%", "65%", "50%", "35%"],
        HarmonyRule.Custom => ["基色", "+角度", "-角度"],
        _ => [.. Enumerable.Range(1, count).Select(i => $"颜色 {i}")]
    };

    private static ColorItem CreateItem(string label, RgbColor color)
    {
        var brush = new SolidColorBrush(WpfColor.FromRgb(color.R, color.G, color.B));
        brush.Freeze();

        return new ColorItem
        {
            Label = label,
            Hex = color.Hex,
            Rgb = $"RGB({color.R}, {color.G}, {color.B})",
            Brush = brush,
            Color = color
        };
    }

    // ---------------------------------------------------------------- RGB / HSV / HEX

    private void OnRgbSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || HexBox is null)
        {
            return;
        }

        SetColor(new RgbColor(
            (byte)Math.Round(RSlider.Value),
            (byte)Math.Round(GSlider.Value),
            (byte)Math.Round(BSlider.Value)));
    }

    private void OnHsvSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || HexBox is null)
        {
            return;
        }

        SetColor(ColorMath.HsvToRgb(new HsvColor(HSlider.Value, SSlider.Value, VSlider.Value)));
    }

    private void OnRgbBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitRgbBoxes();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            UpdateUi();
        }
    }

    private void OnRgbBoxLostFocus(object sender, RoutedEventArgs e) => CommitRgbBoxes();

    private void CommitRgbBoxes()
    {
        if (_updating)
        {
            return;
        }

        if (int.TryParse(RBox.Text, out int r)
            && int.TryParse(GBox.Text, out int g)
            && int.TryParse(BBox.Text, out int b))
        {
            SetColor(new RgbColor(
                (byte)Math.Clamp(r, 0, 255),
                (byte)Math.Clamp(g, 0, 255),
                (byte)Math.Clamp(b, 0, 255)));
        }
        else
        {
            UpdateUi();
        }
    }

    private void OnHsvBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHsvBoxes();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            UpdateUi();
        }
    }

    private void OnHsvBoxLostFocus(object sender, RoutedEventArgs e) => CommitHsvBoxes();

    private void CommitHsvBoxes()
    {
        if (_updating)
        {
            return;
        }

        if (double.TryParse(HBox.Text, out double h)
            && double.TryParse(SBox.Text, out double s)
            && double.TryParse(VBox.Text, out double v))
        {
            SetColor(ColorMath.HsvToRgb(new HsvColor(h, s, v)));
        }
        else
        {
            UpdateUi();
        }
    }

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHex();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            UpdateUi();
        }
    }

    private void OnHexLostFocus(object sender, RoutedEventArgs e) => CommitHex();

    private void CommitHex()
    {
        if (_updating)
        {
            return;
        }

        if (ColorMath.TryParseHex(HexBox.Text, out RgbColor color))
        {
            SetColor(color);
        }
        else
        {
            UpdateUi();
        }
    }

    // ---------------------------------------------------------------- 交互

    private void OnWheelPicked(object sender, ColorPickedEventArgs e)
    {
        if (!_updating)
        {
            SetColor(e.Color);
        }
    }

    private void OnPalettePicked(object sender, ColorPickedEventArgs e)
    {
        if (!_updating)
        {
            SetColor(e.Color);
        }
    }

    private void OnMatchingSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || MatchingList.SelectedItem is not ColorItem item)
        {
            return;
        }

        SetColor(item.Color);
        MatchingList.SelectedItem = null;
    }

    private void OnFavoriteSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || FavoritesList.SelectedItem is not ColorItem item)
        {
            return;
        }

        SetColor(item.Color);
        FavoritesList.SelectedItem = null;
    }

    private void OnHarmonyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HarmonyCombo.SelectedItem is not HarmonyOption option)
        {
            return;
        }

        _rule = option.Rule;
        CustomAnglePanel.Visibility = _rule == HarmonyRule.Custom ? Visibility.Visible : Visibility.Collapsed;

        if (!_updating)
        {
            RebuildMatching();
        }
    }

    private void OnCustomAngleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitCustomAngle();
            e.Handled = true;
        }
    }

    private void OnCustomAngleLostFocus(object sender, RoutedEventArgs e) => CommitCustomAngle();

    private void CommitCustomAngle()
    {
        if (double.TryParse(CustomAngleBox.Text, out double angle))
        {
            _customAngle = Math.Clamp(angle, 1, 180);
            CustomAngleBox.Text = _customAngle.ToString("0");
        }
        else
        {
            CustomAngleBox.Text = _customAngle.ToString("0");
        }

        if (!_updating)
        {
            RebuildMatching();
        }
    }

    // ---------------------------------------------------------------- 复制 / 收藏 / 取色

    /// <summary>点击大色块时复制当前 HEX（顶部不再单独放复制按钮）。</summary>
    private void OnCopyHex(object sender, RoutedEventArgs e) => CopyText(_color.Hex);

    /// <summary>行内复制按钮：弹出与 ColorSchemer 类似的复制菜单。</summary>
    private void OnCopyColorMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not ColorItem item)
        {
            return;
        }

        string hex = item.Color.Hex;
        int matchingIndex = _matching.IndexOf(item);
        int favoriteIndex = _favorites.IndexOf(item);

        string variableName = matchingIndex >= 0
            ? $"color-{matchingIndex + 1}"
            : favoriteIndex >= 0
                ? $"favorite-{favoriteIndex + 1}"
                : "color";

        HslColor hsl = ColorMath.RgbToHsl(item.Color);

        var menu = new ContextMenu
        {
            PlacementTarget = element,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        menu.Items.Add(CopyMenuItem(hex.TrimStart('#')));
        menu.Items.Add(CopyMenuItem(hex));
        menu.Items.Add(CopyMenuItem($"{item.Color.R}, {item.Color.G}, {item.Color.B}"));
        menu.Items.Add(new Separator());
        menu.Items.Add(CopyMenuItem($"hsl({Math.Round(hsl.H)}, {Math.Round(hsl.S)}%, {Math.Round(hsl.L)}%)"));
        menu.Items.Add(CopyMenuItem($"--{variableName}: {hex};"));

        menu.IsOpen = true;
    }

    private static MenuItem CopyMenuItem(string text)
    {
        var item = new MenuItem { Header = text, ToolTip = "点击复制" };
        item.Click += (_, _) => CopyText(text);
        return item;
    }

    private static void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // 剪贴板被其它程序占用时忽略
        }
    }

    private void OnAddFavorite(object sender, RoutedEventArgs e)
    {
        if (_favorites.Any(item => item.Hex == _color.Hex))
        {
            return;
        }

        _favorites.Insert(0, CreateItem("收藏", _color));
        SaveFavorites();
    }

    private void OnRemoveFavorite(object sender, RoutedEventArgs e)
    {
        if (FavoritesList.SelectedItem is not ColorItem item)
        {
            return;
        }

        _favorites.Remove(item);
        SaveFavorites();
    }

    private void LoadFavorites()
    {
        foreach (string hex in _favoritesStore.Load())
        {
            if (ColorMath.TryParseHex(hex, out RgbColor color))
            {
                _favorites.Add(CreateItem("收藏", color));
            }
        }
    }

    private void SaveFavorites() => _favoritesStore.Save(_favorites.Select(item => item.Hex));

    private void OnPickScreen(object sender, RoutedEventArgs e) => PickFromScreen();

    private void PickFromScreen()
    {
        using DrawingBitmap screen = CaptureVirtualScreen();
        var picker = new EyedropperWindow(screen) { Owner = this };

        if (picker.ShowDialog() == true && picker.Result is RgbColor color)
        {
            SetColor(color);
        }
    }

    private static DrawingBitmap CaptureVirtualScreen()
    {
        int left = GetSystemMetrics(SystemMetricXVirtualScreen);
        int top = GetSystemMetrics(SystemMetricYVirtualScreen);
        int width = Math.Max(1, GetSystemMetrics(SystemMetricCxVirtualScreen));
        int height = Math.Max(1, GetSystemMetrics(SystemMetricCyVirtualScreen));

        var bitmap = new DrawingBitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(left, top, 0, 0, new DrawingSize(width, height), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private const int SystemMetricXVirtualScreen = 76;
    private const int SystemMetricYVirtualScreen = 77;
    private const int SystemMetricCxVirtualScreen = 78;
    private const int SystemMetricCyVirtualScreen = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
