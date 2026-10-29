using System.Text;

namespace GoogleAuthenticator.Smoke;

internal static class Helpers
{
    /// <summary>用 ZXing 生成二维码 PNG，供 QrScanner 反向解析（端到端校验）。</summary>
    public static byte[] RenderQrPng(string text, int pixelSize = 360)
    {
        ZXing.BarcodeWriterGeneric writer = new()
        {
            Format = ZXing.BarcodeFormat.QR_CODE,
            Options = new ZXing.Common.EncodingOptions
            {
                Height = pixelSize,
                Width = pixelSize,
                Margin = 2
            }
        };

        ZXing.Common.BitMatrix matrix = writer.Encode(text);
        using SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image =
            new(matrix.Width, matrix.Height);

        for (int y = 0; y < matrix.Height; y++)
        {
            for (int x = 0; x < matrix.Width; x++)
            {
                image[x, y] = matrix[x, y]
                    ? new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 0, 0)
                    : new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 255, 255);
            }
        }

        using MemoryStream stream = new();
        image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return stream.ToArray();
    }

    /// <summary>渲染一张普通渐变图，用于验证「图里没有二维码时返回 null」。</summary>
    public static byte[] RenderGradientPng(int width = 200, int height = 200)
    {
        using SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(
                    (byte)(x * 255 / width),
                    (byte)(y * 255 / height),
                    (byte)((x + y) * 255 / (width + height)));
            }
        }

        using MemoryStream stream = new();
        image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return stream.ToArray();
    }

    /// <summary>手工拼一个 Google Authenticator 转移链接里的 protobuf 载荷。</summary>
    public static string BuildMigrationUri(
        byte[] secret,
        string name,
        string issuer,
        int algorithm,
        int digitCount,
        int otpType,
        long counter = 0,
        int version = 1)
    {
        List<byte> parameters = [];
        WriteTag(parameters, 1, 2);
        WriteBytes(parameters, secret);
        WriteTag(parameters, 2, 2);
        WriteBytes(parameters, Encoding.UTF8.GetBytes(name));
        WriteTag(parameters, 3, 2);
        WriteBytes(parameters, Encoding.UTF8.GetBytes(issuer));
        WriteTag(parameters, 4, 0);
        WriteVarint(parameters, (ulong)algorithm);
        WriteTag(parameters, 5, 0);
        WriteVarint(parameters, (ulong)digitCount);
        WriteTag(parameters, 6, 0);
        WriteVarint(parameters, (ulong)otpType);
        if (counter > 0)
        {
            WriteTag(parameters, 7, 0);
            WriteVarint(parameters, (ulong)counter);
        }

        List<byte> payload = [];
        WriteTag(payload, 1, 2);
        WriteBytes(payload, [.. parameters]);
        WriteTag(payload, 2, 0);
        WriteVarint(payload, (ulong)version);

        return "otpauth-migration://offline?data=" + Convert.ToBase64String([.. payload])
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void WriteTag(List<byte> buffer, int field, int wireType)
        => WriteVarint(buffer, (ulong)((field << 3) | wireType));

    private static void WriteBytes(List<byte> buffer, byte[] value)
    {
        WriteVarint(buffer, (ulong)value.Length);
        buffer.AddRange(value);
    }

    private static void WriteVarint(List<byte> buffer, ulong value)
    {
        while (value >= 0x80)
        {
            buffer.Add((byte)(value | 0x80));
            value >>= 7;
        }

        buffer.Add((byte)value);
    }
}
