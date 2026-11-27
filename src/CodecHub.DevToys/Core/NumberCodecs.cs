using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.RegularExpressions;
using static CodecHub.DevToys.Core.CodecPrimitives;

namespace CodecHub.DevToys.Core;

/// <summary>进制、补码/字节序、IPv4、Unix 时间戳、GUID 格式。</summary>
public static class NumberCodecs
{
    public static string ConvertRadix(string input, int from, int to)
        => FormatRadix(ParseRadix(input, from), to);

    public static IReadOnlyList<CodecView> RadixViews(BigInteger value) =>
    [
        new("二进制", FormatRadix(value, 2)),
        new("八进制", FormatRadix(value, 8)),
        new("十进制", FormatRadix(value, 10)),
        new("十六进制", FormatRadix(value, 16))
    ];

    public static string EncodeTwosComplement(BigInteger value, int bitWidth, bool signed, ByteOrder byteOrder)
    {
        int width = ValidateBitWidth(bitWidth);
        BigInteger modulus = BigInteger.One << width;
        if (signed && (value < -(BigInteger.One << (width - 1)) || value > (BigInteger.One << (width - 1)) - 1))
        {
            throw new CodecException($"{value} 超出 {width} 位有符号整数范围。");
        }

        if (!signed && (value < 0 || value > modulus - 1))
        {
            throw new CodecException($"{value} 超出 {width} 位无符号整数范围。");
        }

        BigInteger wrapped = ((value % modulus) + modulus) % modulus;
        byte[] all = BitConverter.GetBytes((ulong)wrapped);
        byte[] bytes = new byte[width / 8];
        Array.Copy(all, bytes, bytes.Length);
        if (byteOrder == ByteOrder.Big)
        {
            Array.Reverse(bytes);
        }

        return HexEncode(bytes);
    }

    public static BigInteger DecodeTwosComplement(string input, int bitWidth, bool signed, ByteOrder byteOrder)
    {
        int width = ValidateBitWidth(bitWidth);
        byte[] bytes = HexDecode(input);
        int expected = width / 8;
        if (bytes.Length > expected)
        {
            throw new CodecException($"输入有 {bytes.Length} 字节，超过 {width} 位（{expected} 字节）。");
        }

        byte[] padded = new byte[expected];
        if (byteOrder == ByteOrder.Big)
        {
            Array.Copy(bytes, 0, padded, expected - bytes.Length, bytes.Length);
            Array.Reverse(padded);
        }
        else
        {
            Array.Copy(bytes, 0, padded, 0, bytes.Length);
        }

        Array.Resize(ref padded, 8);
        ulong raw = BitConverter.ToUInt64(padded, 0);
        BigInteger modulus = BigInteger.One << width;
        BigInteger unsigned = (BigInteger)raw % modulus;
        if (!signed || unsigned < (BigInteger.One << (width - 1)))
        {
            return unsigned;
        }

        return unsigned - modulus;
    }

    public static IReadOnlyList<CodecView> TwosComplementViews(BigInteger value, int bitWidth, bool signed, ByteOrder byteOrder)
    {
        int width = ValidateBitWidth(bitWidth);
        BigInteger modulus = BigInteger.One << width;
        BigInteger unsigned = ((value % modulus) + modulus) % modulus;
        BigInteger signedValue = unsigned >= (BigInteger.One << (width - 1)) ? unsigned - modulus : unsigned;
        string binary = FormatRadix(unsigned, 2).PadLeft(width, '0');
        string bytes = string.Join(" ", Enumerable.Range(0, width / 8)
            .Select(i => (unsigned >> (i * 8)) & 0xFF));

        return
        [
            new("有符号十进制", signedValue.ToString(CultureInfo.InvariantCulture)),
            new("无符号十进制", unsigned.ToString(CultureInfo.InvariantCulture)),
            new($"二进制（{width} 位补码）", binary),
            new("字节序列（低位在前）", bytes),
            new("按字节序排列的 Hex", EncodeTwosComplement(unsigned, width, false, byteOrder))
        ];
    }

    public static string EncodeIp(string input)
    {
        byte[] bytes = ParseIpv4(input);
        BigInteger value = BigInteger.Zero;
        foreach (byte b in bytes)
        {
            value = (value << 8) | b;
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    public static string DecodeIp(string input)
    {
        string text = input.Trim();
        BigInteger value = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ParseRadix(text, 16)
            : ParseRadix(text, 10);
        if (value < 0 || value > uint.MaxValue)
        {
            throw new CodecException("IPv4 整数必须在 0~4294967295 之间。");
        }

        uint number = (uint)value;
        byte[] bigEndian = BitConverter.GetBytes(number);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bigEndian);
        }

        return new IPAddress(bigEndian).ToString();
    }

    public static IReadOnlyList<CodecView> IpViews(string ipv4)
    {
        byte[] bytes = ParseIpv4(ipv4);
        BigInteger network = BigInteger.Zero;
        BigInteger host = BigInteger.Zero;
        foreach (byte b in bytes)
        {
            network = (network << 8) | b;
        }

        for (int i = bytes.Length - 1; i >= 0; i--)
        {
            host = (host << 8) | bytes[i];
        }

        return
        [
            new("整数（网络序）", network.ToString(CultureInfo.InvariantCulture)),
            new("整数（主机序）", host.ToString(CultureInfo.InvariantCulture)),
            new("Hex（网络序）", "0x" + HexEncode(bytes)),
            new("二进制", string.Join(" ", bytes.Select(b => Convert.ToString(b, 2).PadLeft(8, '0'))))
        ];
    }

    public static string TimeToEpoch(string input, string unit, bool utc)
    {
        DateTimeOffset dto = ParseDateTimeOffset(input, utc);
        long ticks = dto.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        return unit switch
        {
            "s" => (ticks / TimeSpan.TicksPerSecond).ToString(CultureInfo.InvariantCulture),
            "ms" => (ticks / TimeSpan.TicksPerMillisecond).ToString(CultureInfo.InvariantCulture),
            "us" => (ticks / 10).ToString(CultureInfo.InvariantCulture),
            "ns" => (ticks * 100).ToString(CultureInfo.InvariantCulture),
            _ => throw new CodecException($"不支持的时间单位：{unit}（可用 s / ms / us / ns）")
        };
    }

    public static DateTimeOffset EpochToDateTimeOffset(string input, string unit)
    {
        if (!BigInteger.TryParse(input.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger value))
        {
            throw new CodecException($"不是合法的时间戳：{input}");
        }

        BigInteger ticks = unit switch
        {
            "s" => value * TimeSpan.TicksPerSecond,
            "ms" => value * TimeSpan.TicksPerMillisecond,
            "us" => value * 10,
            "ns" => value / 100,
            _ => throw new CodecException($"不支持的时间单位：{unit}（可用 s / ms / us / ns）")
        };

        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            throw new CodecException("时间戳超出可表示的日期范围。");
        }

        return DateTimeOffset.UnixEpoch.AddTicks((long)ticks);
    }

    public static string EpochToTime(string input, string unit, bool utc)
        => DateTimeText(EpochToDateTimeOffset(input, unit), utc);

    public static IReadOnlyList<CodecView> TimeViews(DateTimeOffset dto) =>
    [
        new("UTC", DateTimeText(dto, true)),
        new("本地时区", DateTimeText(dto, false)),
        new("ISO 8601", dto.ToString("O", CultureInfo.InvariantCulture)),
        new("秒", (dto.ToUnixTimeMilliseconds() / 1000).ToString(CultureInfo.InvariantCulture)),
        new("毫秒", dto.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture))
    ];

    public static string NormalizeGuid(string input)
    {
        if (!Guid.TryParse(input.Trim(), out Guid guid))
        {
            throw new CodecException("不是合法的 GUID（支持 N / D / B / P / X 五种格式）。");
        }

        return guid.ToString("D");
    }

    public static IReadOnlyList<CodecView> GuidViews(Guid guid)
    {
        byte[] bytes = guid.ToByteArray();
        return
        [
            new("N（32 位无连字符）", guid.ToString("N")),
            new("B（花括号）", guid.ToString("B")),
            new("P（圆括号）", guid.ToString("P")),
            new("X（十六进制字面量）", guid.ToString("X")),
            new("RFC 字节序 Hex", guid.ToString("N").ToUpperInvariant()),
            new("Windows 混合序 Hex", HexEncode(bytes))
        ];
    }

    private static int ValidateBitWidth(int bitWidth)
    {
        if (bitWidth is < 8 or > 64 || bitWidth % 8 != 0)
        {
            throw new CodecException("位宽必须是 8~64 之间 8 的倍数。");
        }

        return bitWidth;
    }

    private static byte[] ParseIpv4(string input)
    {
        string[] parts = input.Trim().Split('.');
        if (parts.Length != 4)
        {
            throw new CodecException("IPv4 必须是 a.b.c.d 四段式。");
        }

        byte[] bytes = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            if (!byte.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out bytes[i]))
            {
                throw new CodecException($"“{parts[i]}”不是合法的 IPv4 段（0~255）。");
            }
        }

        return bytes;
    }

    public static DateTimeOffset ParseDateTimeOffset(string input, bool utc)
    {
        string text = input.Trim();
        if (Regex.IsMatch(text, @"(Z|[+-]\d{2}:?\d{2})$", RegexOptions.IgnoreCase) &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset explicitDto))
        {
            return explicitDto;
        }

        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime dt))
        {
            throw new CodecException($"无法识别日期时间：“{input}”。推荐 yyyy-MM-dd HH:mm:ss 或 ISO 8601。");
        }

        if (dt.Kind == DateTimeKind.Utc || utc)
        {
            return new DateTimeOffset(dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : dt, TimeSpan.Zero);
        }

        return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
    }
}
