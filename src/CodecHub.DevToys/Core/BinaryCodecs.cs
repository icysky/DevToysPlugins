using System.Numerics;
using System.Text;
using static CodecHub.DevToys.Core.CodecPrimitives;

namespace CodecHub.DevToys.Core;

/// <summary>Base64 / Base64url / Base32 / Base58 / Hex 字节编解码。</summary>
public static class BinaryCodecs
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const string Base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    public static string Base64Encode(byte[] bytes, bool urlSafe, bool padding)
    {
        string text = Convert.ToBase64String(bytes);
        if (urlSafe)
        {
            text = text.Replace('+', '-').Replace('/', '_');
        }

        if (!padding)
        {
            text = text.TrimEnd('=');
        }

        return text;
    }

    public static byte[] Base64Decode(string text, bool urlSafe)
    {
        string compact = new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (urlSafe)
        {
            compact = compact.Replace('-', '+').Replace('_', '/');
        }

        int pad = compact.Length % 4;
        if (pad != 0)
        {
            compact += new string('=', 4 - pad);
        }

        try
        {
            return Convert.FromBase64String(compact);
        }
        catch (FormatException ex)
        {
            throw new CodecException($"不是合法的{(urlSafe ? " Base64url" : " Base64")}：{ex.Message}");
        }
    }

    public static string Base32Encode(byte[] bytes)
    {
        var sb = new StringBuilder();
        int buffer = 0;
        int bits = 0;
        foreach (byte b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Base32Alphabet[(buffer >> bits) & 31]);
            }
        }

        if (bits > 0)
        {
            sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        }

        while (sb.Length % 8 != 0)
        {
            sb.Append('=');
        }

        return sb.ToString();
    }

    public static byte[] Base32Decode(string text)
    {
        string compact = new(text.Where(c => !char.IsWhiteSpace(c) && c != '=').ToArray());
        var bytes = new List<byte>();
        int buffer = 0;
        int bits = 0;
        foreach (char c in compact)
        {
            int digit = Base32Alphabet.IndexOf(char.ToUpperInvariant(c));
            if (digit < 0)
            {
                throw new CodecException($"字符“{c}”不是合法的 Base32 字符（A~Z、2~7）。");
            }

            buffer = (buffer << 5) | digit;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
                buffer &= (1 << bits) - 1;
            }
        }

        return bytes.ToArray();
    }

    public static string Base58Encode(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        int zeros = 0;
        while (zeros < bytes.Length && bytes[zeros] == 0)
        {
            zeros++;
        }

        var value = new BigInteger(bytes.AsSpan(zeros), isUnsigned: true, isBigEndian: true);
        var digits = new List<char>();
        while (value > BigInteger.Zero)
        {
            value = BigInteger.DivRem(value, 58, out BigInteger remainder);
            digits.Add(Base58Alphabet[(int)remainder]);
        }

        digits.Reverse();
        return new string('1', zeros) + new string(digits.ToArray());
    }

    public static byte[] Base58Decode(string text)
    {
        string compact = new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (compact.Length == 0)
        {
            return [];
        }

        int zeros = 0;
        while (zeros < compact.Length && compact[zeros] == '1')
        {
            zeros++;
        }

        BigInteger value = BigInteger.Zero;
        for (int i = zeros; i < compact.Length; i++)
        {
            int digit = Base58Alphabet.IndexOf(compact[i]);
            if (digit < 0)
            {
                throw new CodecException($"字符“{compact[i]}”不是合法的 Base58 字符（不含 0、O、I、l）。");
            }

            value = value * 58 + digit;
        }

        byte[] body = value == BigInteger.Zero && zeros == compact.Length
            ? []
            : value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (zeros == 0)
        {
            return body;
        }

        byte[] result = new byte[zeros + body.Length];
        body.CopyTo(result, zeros);
        return result;
    }

    public static string HexEncode(byte[] bytes, bool upper, string separator)
        => CodecPrimitives.HexEncode(bytes, upper, separator);

    public static byte[] HexDecode(string text, string separator)
    {
        if (!string.IsNullOrEmpty(separator))
        {
            text = text.Replace(separator, string.Empty, StringComparison.Ordinal);
        }

        return CodecPrimitives.HexDecode(text);
    }
}
