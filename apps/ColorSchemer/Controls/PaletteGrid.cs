using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ColorSchemer.Core;

namespace ColorSchemer.Controls;

/// <summary>色板网格：按行列绘制色块，点击取色。</summary>
public sealed class PaletteGrid : FrameworkElement
{
    private IReadOnlyList<IReadOnlyList<RgbColor>> _colors = [];

    public event EventHandler<ColorPickedEventArgs>? ColorPicked;

    public void SetColors(IReadOnlyList<IReadOnlyList<RgbColor>> colors)
    {
        _colors = colors;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 0 || height <= 0 || _colors.Count == 0 || _colors[0].Count == 0)
        {
            return;
        }

        int rows = _colors.Count;
        int columns = _colors[0].Count;
        double cellWidth = width / columns;
        double cellHeight = height / rows;

        for (int row = 0; row < rows; row++)
        {
            IReadOnlyList<RgbColor> line = _colors[row];

            for (int column = 0; column < line.Count; column++)
            {
                RgbColor color = line[column];
                var brush = new SolidColorBrush(ToMediaColor(color));
                brush.Freeze();

                var rect = new Rect(
                    column * cellWidth + 0.5,
                    row * cellHeight + 0.5,
                    Math.Max(0, cellWidth - 1),
                    Math.Max(0, cellHeight - 1));

                dc.DrawRectangle(brush, null, rect);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (TryHitTest(e.GetPosition(this), out RgbColor color))
        {
            ToolTip = color.Hex;
            ToolTipService.SetInitialShowDelay(this, 150);
            ToolTipService.SetShowDuration(this, 4000);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (TryHitTest(e.GetPosition(this), out RgbColor color))
        {
            ColorPicked?.Invoke(this, new ColorPickedEventArgs(color));
            e.Handled = true;
        }
    }

    private bool TryHitTest(Point point, out RgbColor color)
    {
        color = default;

        if (_colors.Count == 0 || _colors[0].Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return false;
        }

        int columns = _colors[0].Count;
        int rows = _colors.Count;

        int column = (int)(point.X / (ActualWidth / columns));
        int row = (int)(point.Y / (ActualHeight / rows));

        if (column < 0 || column >= columns || row < 0 || row >= rows)
        {
            return false;
        }

        color = _colors[row][column];
        return true;
    }

    private static Color ToMediaColor(RgbColor color) => Color.FromRgb(color.R, color.G, color.B);
}
