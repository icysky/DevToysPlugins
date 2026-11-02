using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace GoogleAuthenticator.DevToys.Core;

/// <summary>
/// 二维码生成：把文本（如 <c>otpauth://</c> 链接）编码成 ImageSharp 图像，供 DevToys 的图片控件显示。
/// 与 <see cref="QrScanner" /> 配套，冒烟测试里做「编码 → 解码」往返校验。
/// </summary>
internal static class QrCodeRenderer
{
    /// <summary>每个二维码模块画多少像素（8 时 25×25 的码约 200px，足够手机扫）。</summary>
    public const int DefaultModuleSize = 8;

    public static Image<Rgba32> Render(string content, int moduleSize = DefaultModuleSize)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("二维码内容不能为空。", nameof(content));
        }

        if (moduleSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleSize), "模块尺寸必须大于 0。");
        }

        BarcodeWriterGeneric writer = new()
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                // otpauth 链接里 issuer 可能是中文，统一按 UTF-8 编码
                CharacterSet = "UTF-8",
                ErrorCorrection = ErrorCorrectionLevel.M,
                Margin = 4,
                // 0 表示不缩放：让 ZXing 输出「1 像素 = 1 模块」的矩阵，再由下面按 moduleSize 放大
                Width = 0,
                Height = 0
            }
        };

        BitMatrix matrix = writer.Encode(content);
        return ToImage(matrix, moduleSize);
    }

    /// <summary>把 ZXing 的位矩阵按 <paramref name="moduleSize" /> 放大成黑白图像。</summary>
    public static Image<Rgba32> ToImage(BitMatrix matrix, int moduleSize = DefaultModuleSize)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        int width = matrix.Width * moduleSize;
        int height = matrix.Height * moduleSize;
        Rgba32 dark = new(0, 0, 0);
        Rgba32 light = new(255, 255, 255);

        Image<Rgba32> image = new(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                int matrixY = y / moduleSize;
                for (int x = 0; x < row.Length; x++)
                {
                    int matrixX = x / moduleSize;
                    bool isDark = matrixX < matrix.Width
                                  && matrixY < matrix.Height
                                  && matrix[matrixX, matrixY];
                    row[x] = isDark ? dark : light;
                }
            }
        });

        return image;
    }
}
