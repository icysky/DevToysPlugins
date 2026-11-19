using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ColorSchemer.Core;

namespace ColorSchemer.Controls;

/// <summary>
/// 交互式色轮：外圈色相环负责 H，内部 SV 方块负责 S/V。
/// 点击或拖拽都会实时更新 <see cref="SelectedColor"/> 并抛出 <see cref="ColorPicked"/>。
/// </summary>
public sealed class ColorWheel : FrameworkElement
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor),
        typeof(Color),
        typeof(ColorWheel),
        new FrameworkPropertyMetadata(
            Colors.Red,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedColorChanged));

    private enum DragMode
    {
        None,
        Hue,
        SaturationValue
    }

    private DrawingGroup? _ring;
    private Size _ringSize;
    private double _ringInner;
    private double _ringOuter;

    private HsvColor _hsv = new(0, 100, 100);
    private DragMode _dragMode;
    private bool _syncing;

    public ColorWheel()
    {
        Cursor = Cursors.Hand;
        ToolTipService.SetInitialShowDelay(this, 0);
    }

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public event EventHandler<ColorPickedEventArgs>? ColorPicked;

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var wheel = (ColorWheel)d;

        if (!wheel._syncing && e.NewValue is Color color)
        {
            // 外部同步回来的颜色如果和当前 HSV 计算出的颜色一致，就保留内部 H/S/V，
            // 否则选灰度色时色相会被重置为 0，拖动色环标记会跳回顶部。
            RgbColor rgb = ToRgb(color);

            if (rgb != ColorMath.HsvToRgb(wheel._hsv))
            {
                wheel._hsv = ColorMath.RgbToHsv(rgb);
            }
        }

        wheel.InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;

        if (width < 60 || height < 60)
        {
            return;
        }

        var center = new Point(width / 2, height / 2);
        double outer = Math.Min(width, height) / 2 - 8;
        double inner = outer * 0.66;

        if (outer <= inner + 12)
        {
            return;
        }

        EnsureRing(center, inner, outer);
        dc.DrawDrawing(_ring);

        Rect square = GetSquareRect(center, inner);
        DrawSquare(dc, square);
        DrawMarkers(dc, center, inner, outer, square);
    }

    private void EnsureRing(Point center, double inner, double outer)
    {
        var size = new Size(ActualWidth, ActualHeight);

        if (_ring is not null
            && _ringSize == size
            && Math.Abs(_ringInner - inner) < 0.1
            && Math.Abs(_ringOuter - outer) < 0.1)
        {
            return;
        }

        var group = new DrawingGroup();

        for (int hue = 0; hue < 360; hue++)
        {
            RgbColor rgb = ColorMath.HsvToRgb(new HsvColor(hue, 100, 100));
            var brush = new SolidColorBrush(ToMediaColor(rgb));
            brush.Freeze();

            Geometry geometry = CreateWedge(center, inner, outer, hue - 0.65, hue + 0.65);
            geometry.Freeze();

            group.Children.Add(new GeometryDrawing(brush, null, geometry));
        }

        group.Freeze();
        _ring = group;
        _ringSize = size;
        _ringInner = inner;
        _ringOuter = outer;
    }

    private void DrawSquare(DrawingContext dc, Rect square)
    {
        RgbColor hueColor = ColorMath.HsvToRgb(new HsvColor(_hsv.H, 100, 100));
        var hueBrush = new SolidColorBrush(ToMediaColor(hueColor));
        hueBrush.Freeze();
        dc.DrawRectangle(hueBrush, null, square);

        var white = new LinearGradientBrush(
            Color.FromArgb(255, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255),
            new Point(0, 0.5),
            new Point(1, 0.5));
        white.Freeze();
        dc.DrawRectangle(white, null, square);

        var black = new LinearGradientBrush(
            Color.FromArgb(0, 0, 0, 0),
            Color.FromArgb(255, 0, 0, 0),
            new Point(0.5, 0),
            new Point(0.5, 1));
        black.Freeze();
        dc.DrawRectangle(black, null, square);

        var border = new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), 1);
        border.Freeze();
        dc.DrawRectangle(null, border, square);
    }

    private void DrawMarkers(DrawingContext dc, Point center, double inner, double outer, Rect square)
    {
        RgbColor rgb = ColorMath.HsvToRgb(_hsv);
        Color current = ToMediaColor(rgb);

        // 色相环上的位置
        Point ringPoint = Polar(center, (inner + outer) / 2, _hsv.H);
        var fill = new SolidColorBrush(current);
        fill.Freeze();
        var whitePen = new Pen(Brushes.White, 2.5);
        whitePen.Freeze();
        dc.DrawEllipse(fill, whitePen, ringPoint, 8, 8);

        // SV 方块中的位置
        double x = square.Left + _hsv.S / 100 * square.Width;
        double y = square.Top + (1 - _hsv.V / 100) * square.Height;
        var marker = new Point(x, y);
        var outline = new Pen(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), 1);
        outline.Freeze();
        dc.DrawEllipse(null, whitePen, marker, 7, 7);
        dc.DrawEllipse(null, outline, marker, 8.5, 8.5);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        Point point = e.GetPosition(this);
        _dragMode = HitTest(point);

        if (_dragMode == DragMode.None)
        {
            return;
        }

        CaptureMouse();
        ApplyPoint(point);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!IsMouseCaptured || _dragMode == DragMode.None)
        {
            return;
        }

        ApplyPoint(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        _dragMode = DragMode.None;
    }

    private DragMode HitTest(Point point)
    {
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double outer = Math.Min(ActualWidth, ActualHeight) / 2 - 8;
        double inner = outer * 0.66;
        double distance = (point - center).Length;

        if (distance >= inner - 2 && distance <= outer + 4)
        {
            return DragMode.Hue;
        }

        return GetSquareRect(center, inner).Contains(point)
            ? DragMode.SaturationValue
            : DragMode.None;
    }

    private void ApplyPoint(Point point)
    {
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double outer = Math.Min(ActualWidth, ActualHeight) / 2 - 8;
        double inner = outer * 0.66;
        Rect square = GetSquareRect(center, inner);

        if (_dragMode == DragMode.Hue)
        {
            double angle = Math.Atan2(point.Y - center.Y, point.X - center.X) * 180 / Math.PI + 90;

            if (angle < 0)
            {
                angle += 360;
            }

            _hsv = new HsvColor(angle, _hsv.S, _hsv.V);
        }
        else if (_dragMode == DragMode.SaturationValue && square.Width > 0)
        {
            double saturation = Math.Clamp((point.X - square.Left) / square.Width * 100, 0, 100);
            double value = Math.Clamp((1 - (point.Y - square.Top) / square.Height) * 100, 0, 100);
            _hsv = new HsvColor(_hsv.H, saturation, value);
        }
        else
        {
            return;
        }

        RgbColor rgb = ColorMath.HsvToRgb(_hsv);

        _syncing = true;
        SetCurrentValue(SelectedColorProperty, ToMediaColor(rgb));
        _syncing = false;

        ColorPicked?.Invoke(this, new ColorPickedEventArgs(rgb));
        InvalidateVisual();
    }

    private static Rect GetSquareRect(Point center, double inner)
    {
        double side = inner * 1.3;
        return new Rect(center.X - side / 2, center.Y - side / 2, side, side);
    }

    private static Geometry CreateWedge(Point center, double inner, double outer, double startAngle, double endAngle)
    {
        Point outerStart = Polar(center, outer, startAngle);
        Point outerEnd = Polar(center, outer, endAngle);
        Point innerEnd = Polar(center, inner, endAngle);
        Point innerStart = Polar(center, inner, startAngle);

        var geometry = new StreamGeometry();

        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(outerStart, true, true);
            context.ArcTo(outerEnd, new Size(outer, outer), 0, false, SweepDirection.Clockwise, true, false);
            context.LineTo(innerEnd, true, false);
            context.ArcTo(innerStart, new Size(inner, inner), 0, false, SweepDirection.Counterclockwise, true, false);
        }

        return geometry;
    }

    private static Point Polar(Point center, double radius, double angleDegrees)
    {
        double radians = (angleDegrees - 90) * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private static Color ToMediaColor(RgbColor color) => Color.FromRgb(color.R, color.G, color.B);

    private static RgbColor ToRgb(Color color) => new(color.R, color.G, color.B);
}
