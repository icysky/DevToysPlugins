using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ZXing;
using ZXing.Common;

namespace GoogleAuthenticator.DevToys.Core;

/// <summary>二维码识别：输入图片字节或 ImageSharp 图像，输出其中的文本。</summary>
internal static class QrScanner
{
    public static string? Decode(byte[] imageBytes)
    {
        if (imageBytes.Length == 0)
        {
            return null;
        }

        using Image<Rgba32> image = Image.Load<Rgba32>(imageBytes);
        return Decode(image);
    }

    public static string? Decode(Image<Rgba32> image)
    {
        ArgumentNullException.ThrowIfNull(image);

        int width = image.Width;
        int height = image.Height;
        if (width < 8 || height < 8)
        {
            return null;
        }

        byte[] luminance = new byte[width * height];
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                int offset = y * width;
                for (int x = 0; x < row.Length; x++)
                {
                    Rgba32 pixel = row[x];
                    // ITU-R BT.601 亮度
                    int value = ((pixel.R * 299) + (pixel.G * 587) + (pixel.B * 114)) / 1000;
                    luminance[offset + x] = (byte)value;
                }
            }
        });

        BarcodeReaderGeneric reader = new()
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = [BarcodeFormat.QR_CODE]
            }
        };

        Result? result = reader.Decode(new GrayLuminanceSource(luminance, width, height));
        if (result is null && Math.Max(width, height) > 1600)
        {
            // 超大图先缩放再试一次，避免噪点影响定位。
            using Image<Rgba32> resized = image.Clone();
            resized.Mutate(context => context.Resize(width / 2, height / 2));
            return Decode(resized);
        }

        return result?.Text;
    }

    private sealed class GrayLuminanceSource(byte[] gray, int width, int height) : LuminanceSource(width, height)
    {
        private readonly byte[] _gray = gray;

        public override byte[] Matrix => _gray;

        public override byte[] getRow(int y, byte[] row)
        {
            int width = Width;
            if (row is null || row.Length < width)
            {
                row = new byte[width];
            }

            Array.Copy(_gray, y * width, row, 0, width);
            return row;
        }
    }
}
