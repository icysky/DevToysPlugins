using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace GoogleAuthenticator.DevToys.Core;

/// <summary>
/// RFC 4226 (HOTP) / RFC 6238 (TOTP) 计算。
/// </summary>
/// <remarks>
/// 位数说明：
/// <list type="bullet">
/// <item>6~8 位：使用 RFC 4226 §5.4 标准的 31 位动态截断（dynamic truncation），
/// 与 Google Authenticator、Microsoft Authenticator、1Password 等标准验证器完全互通。</item>
/// <item>9~12 位：超出 RFC 范围（31 位截断最多只能安全表达 9 位十进制）。
/// 此时改用「完整 HMAC 大端无符号整数 mod 10^digits」的扩展截断，需要验证方使用同一算法。</item>
/// </list>
/// </remarks>
internal static class TotpEngine
{
    public const int DefaultDigits = 6;
    public const int DefaultPeriod = 30;
    public const int MinDigits = 6;
    public const int MaxDigits = 12;

    /// <summary>标准 RFC 截断能安全表达的位数上限。</summary>
    public const int StandardMaxDigits = 8;

    public static long GetCounter(OtpAccount account, DateTimeOffset timestamp)
    {
        if (account.Type == OtpType.Hotp)
        {
            return Math.Max(0, account.Counter);
        }

        int period = account.Period <= 0 ? DefaultPeriod : account.Period;
        return timestamp.ToUnixTimeSeconds() / period;
    }

    public static string Compute(OtpAccount account, DateTimeOffset timestamp)
        => Compute(account, GetCounter(account, timestamp));

    public static string Compute(OtpAccount account, long counter)
    {
        byte[] key = Base32.Decode(account.Secret);
        string code = Compute(key, account.Algorithm, counter, account.Digits);
        CryptographicOperations.ZeroMemory(key);
        return code;
    }

    public static string Compute(byte[] key, OtpAlgorithm algorithm, long counter, int digits)
    {
        digits = Math.Clamp(digits, MinDigits, MaxDigits);

        byte[] counterBytes = new byte[8];
        ulong value = unchecked((ulong)counter);
        for (int i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(value & 0xFF);
            value >>= 8;
        }

        byte[] hash = algorithm switch
        {
            OtpAlgorithm.Sha256 => HMACSHA256.HashData(key, counterBytes),
            OtpAlgorithm.Sha512 => HMACSHA512.HashData(key, counterBytes),
            _ => HMACSHA1.HashData(key, counterBytes)
        };

        return Truncate(hash, digits);
    }

    /// <summary>把 HMAC 结果截断成指定位数的十进制字符串。</summary>
    public static string Truncate(byte[] hash, int digits)
    {
        digits = Math.Clamp(digits, MinDigits, MaxDigits);

        if (digits <= 9)
        {
            int offset = hash[^1] & 0x0F;
            long binary =
                ((long)(hash[offset] & 0x7F) << 24)
                | ((long)hash[offset + 1] << 16)
                | ((long)hash[offset + 2] << 8)
                | hash[offset + 3];

            long modulus = Pow10(digits);
            return (binary % modulus).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
        }

        // 扩展截断：把整段 HMAC 当作大端无符号整数再取模。
        BigInteger big = new(hash, isUnsigned: true, isBigEndian: true);
        BigInteger extendedModulus = BigInteger.Pow(10, digits);
        return (big % extendedModulus).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }

    private static long Pow10(int exponent)
    {
        long result = 1;
        for (int i = 0; i < exponent; i++)
        {
            result *= 10;
        }

        return result;
    }

    public static int RemainingSeconds(OtpAccount account, DateTimeOffset timestamp)
    {
        int period = account.Period <= 0 ? DefaultPeriod : account.Period;
        long elapsed = timestamp.ToUnixTimeSeconds() % period;
        return (int)(period - elapsed);
    }

    /// <summary>
    /// 按 Google Authenticator 的习惯把验证码分组显示：<c>123 456</c> / <c>1234 5678</c>。
    /// </summary>
    public static string FormatCode(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length < 4)
        {
            return code;
        }

        int split = code.Length / 2;
        StringBuilder builder = new(code.Length + 1);
        builder.Append(code, 0, split);
        builder.Append(' ');
        builder.Append(code, split, code.Length - split);
        return builder.ToString();
    }

    /// <summary>生成一个随机 Base32 密钥（默认 160 位，与 Google Authenticator 一致）。</summary>
    public static string GenerateSecret(int byteLength = 20)
        => Base32.Encode(RandomNumberGenerator.GetBytes(Math.Max(10, byteLength)));

    public static string AlgorithmToUriValue(OtpAlgorithm algorithm) => algorithm switch
    {
        OtpAlgorithm.Sha256 => "SHA256",
        OtpAlgorithm.Sha512 => "SHA512",
        _ => "SHA1"
    };

    public static OtpAlgorithm AlgorithmFromUriValue(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "SHA256" => OtpAlgorithm.Sha256,
        "SHA512" => OtpAlgorithm.Sha512,
        _ => OtpAlgorithm.Sha1
    };
}
