using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ColorSchemer.Core;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingBitmapData = System.Drawing.Imaging.BitmapData;
using DrawingColor = System.Drawing.Color;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingGraphicsUnit = System.Drawing.GraphicsUnit;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using DrawingImageLockMode = System.Drawing.Imaging.ImageLockMode;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangle = System.Drawing.Rectangle;
using WpfColor = System.Windows.Media.Color;
using WpfImage = System.Windows.Controls.Image;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace ColorSchemer.Controls;

/// <summary>
/// 全屏取色器：先把屏幕冻结成位图，鼠标移动时显示放大镜，单击取中心像素。
/// 冻结屏幕可以避免放大镜遮住待取色的区域，也避免捕获到覆盖层自身。
/// </summary>
public sealed class EyedropperWindow : Window
{
    private const int SampleSize = 21;
    private const int ZoomSize = 176;

    private readonly DrawingBitmap _screen;
    private readonly WriteableBitmap _loupeBitmap;

    private readonly Canvas _loupeCanvas = new();
    private readonly StackPanel _loupePanel = new();
    private readonly TextBlock _hexLabel = new();
    private readonly WpfRectangle _cursorVertical = new();
    private readonly WpfRectangle _cursorHorizontal = new();
    private readonly TextBlock _hint = new();

    private int _pixelX;
    private int _pixelY;
    private RgbColor _current;

    public EyedropperWindow(DrawingBitmap screen)
    {
        _screen = screen;
        _loupeBitmap = new WriteableBitmap(SampleSize, SampleSize, 96, 96, PixelFormats.Bgra32, null);

        Title = "屏幕取色";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Cursor = Cursors.Cross;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Content = BuildContent();

        MouseMove += OnMouseMove;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseRightButtonDown += (_, _) => Cancel();
        KeyDown += OnKeyDown;
    }

    public RgbColor? Result { get; private set; }

    private UIElement BuildContent()
    {
        var root = new Grid { Background = Brushes.Transparent };

        var screenImage = new WpfImage
        {
            Source = ToBitmapSource(_screen),
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        root.Children.Add(screenImage);

        _hint.Text = "单击取色 · 右键 / Esc 取消";
        _hint.Foreground = Brushes.White;
        _hint.FontSize = 13;

        var hintBorder = new Border
        {
            Background = new SolidColorBrush(WpfColor.FromArgb(190, 17, 24, 39)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 8, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 24, 0, 0),
            Child = _hint,
            IsHitTestVisible = false
        };
        root.Children.Add(hintBorder);

        root.Children.Add(_loupeCanvas);

        _cursorVertical.Width = 1;
        _cursorVertical.Height = 26;
        _cursorVertical.Fill = new SolidColorBrush(WpfColor.FromArgb(200, 255, 255, 255));

        _cursorHorizontal.Width = 26;
        _cursorHorizontal.Height = 1;
        _cursorHorizontal.Fill = new SolidColorBrush(WpfColor.FromArgb(200, 255, 255, 255));

        _loupeCanvas.Children.Add(_cursorVertical);
        _loupeCanvas.Children.Add(_cursorHorizontal);

        var loupeImage = new WpfImage
        {
            Source = _loupeBitmap,
            Width = ZoomSize,
            Height = ZoomSize,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(loupeImage, BitmapScalingMode.NearestNeighbor);

        var grid = new Grid();
        grid.Children.Add(loupeImage);

        var crossVertical = new WpfRectangle
        {
            Width = 1,
            Fill = new SolidColorBrush(WpfColor.FromArgb(160, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var crossHorizontal = new WpfRectangle
        {
            Height = 1,
            Fill = new SolidColorBrush(WpfColor.FromArgb(160, 0, 0, 0)),
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(crossVertical);
        grid.Children.Add(crossHorizontal);

        var centerOuter = new WpfRectangle
        {
            Width = 18,
            Height = 18,
            Stroke = new SolidColorBrush(WpfColor.FromArgb(220, 0, 0, 0)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var centerInner = new WpfRectangle
        {
            Width = 10,
            Height = 10,
            Stroke = Brushes.White,
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(centerOuter);
        grid.Children.Add(centerInner);

        _hexLabel.FontFamily = new FontFamily("Consolas");
        _hexLabel.FontSize = 12;
        _hexLabel.Foreground = Brushes.White;
        _hexLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _hexLabel.Margin = new Thickness(0, 6, 0, 0);

        _loupePanel.Children.Add(new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(WpfColor.FromArgb(255, 17, 24, 39)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5),
            Child = grid
        });
        _loupePanel.Children.Add(new Border
        {
            Background = new SolidColorBrush(WpfColor.FromArgb(200, 17, 24, 39)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = _hexLabel
        });

        _loupeCanvas.Children.Add(_loupePanel);

        return root;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        WpfPoint position = e.GetPosition(this);

        double scaleX = _screen.Width / Math.Max(1, ActualWidth);
        double scaleY = _screen.Height / Math.Max(1, ActualHeight);

        _pixelX = Math.Clamp((int)(position.X * scaleX), 0, _screen.Width - 1);
        _pixelY = Math.Clamp((int)(position.Y * scaleY), 0, _screen.Height - 1);

        DrawingColor pixel = _screen.GetPixel(_pixelX, _pixelY);
        _current = new RgbColor(pixel.R, pixel.G, pixel.B);

        _hexLabel.Text = $"{_current.Hex}  RGB({_current.R}, {_current.G}, {_current.B})";

        UpdateLoupe();
        UpdateCursor(position);
        PositionLoupe(position);
    }

    private void UpdateLoupe()
    {
        int half = SampleSize / 2;
        int sourceX = Math.Clamp(_pixelX - half, 0, Math.Max(0, _screen.Width - SampleSize));
        int sourceY = Math.Clamp(_pixelY - half, 0, Math.Max(0, _screen.Height - SampleSize));

        using var crop = new DrawingBitmap(SampleSize, SampleSize, DrawingPixelFormat.Format32bppArgb);

        using (DrawingGraphics graphics = DrawingGraphics.FromImage(crop))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            graphics.DrawImage(
                _screen,
                new DrawingRectangle(0, 0, SampleSize, SampleSize),
                new DrawingRectangle(sourceX, sourceY, SampleSize, SampleSize),
                DrawingGraphicsUnit.Pixel);
        }

        DrawingBitmapData data = crop.LockBits(
            new DrawingRectangle(0, 0, SampleSize, SampleSize),
            DrawingImageLockMode.ReadOnly,
            DrawingPixelFormat.Format32bppArgb);

        try
        {
            _loupeBitmap.Lock();

            for (int row = 0; row < SampleSize; row++)
            {
                IntPtr source = IntPtr.Add(data.Scan0, row * data.Stride);
                IntPtr destination = IntPtr.Add(
                    _loupeBitmap.BackBuffer,
                    row * _loupeBitmap.BackBufferStride);

                CopyMemory(destination, source, (uint)(SampleSize * 4));
            }

            _loupeBitmap.AddDirtyRect(new Int32Rect(0, 0, SampleSize, SampleSize));
            _loupeBitmap.Unlock();
        }
        finally
        {
            crop.UnlockBits(data);
        }
    }

    private void UpdateCursor(WpfPoint position)
    {
        Canvas.SetLeft(_cursorVertical, position.X - 0.5);
        Canvas.SetTop(_cursorVertical, position.Y - 13);
        Canvas.SetLeft(_cursorHorizontal, position.X - 13);
        Canvas.SetTop(_cursorHorizontal, position.Y - 0.5);
    }

    private void PositionLoupe(WpfPoint position)
    {
        _loupePanel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        System.Windows.Size size = _loupePanel.DesiredSize;

        double left = position.X + 22;
        double top = position.Y + 22;

        if (left + size.Width > ActualWidth)
        {
            left = position.X - size.Width - 22;
        }

        if (top + size.Height > ActualHeight)
        {
            top = position.Y - size.Height - 22;
        }

        Canvas.SetLeft(_loupePanel, Math.Max(4, left));
        Canvas.SetTop(_loupePanel, Math.Max(4, top));
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Result = _current;
        DialogResult = true;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
        }
    }

    private void Cancel()
    {
        Result = null;
        DialogResult = false;
        Close();
    }

    private static BitmapSource ToBitmapSource(DrawingBitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, DrawingImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
    private static extern void CopyMemory(IntPtr destination, IntPtr source, uint length);
}
