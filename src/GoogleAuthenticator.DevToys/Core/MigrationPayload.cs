using System.Text;

namespace GoogleAuthenticator.DevToys.Core;

/// <summary>
/// 解析 Google Authenticator「转移账号」二维码里的 <c>otpauth-migration://offline?data=...</c>。
/// data 是 Base64Url 编码的 protobuf（OtpMigration.proto），这里用最小手写 protobuf 读取器解析，
/// 避免为了几十行代码引入 Google.Protobuf 依赖。
/// </summary>
internal static class MigrationPayload
{
    public const string Prefix = "otpauth-migration://";

    public static bool LooksLike(string? text)
        => !string.IsNullOrWhiteSpace(text)
           && text.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static List<OtpAccount> Parse(string text)
    {
        string trimmed = text.Trim();
        int queryStart = trimmed.IndexOf('?');
        if (queryStart < 0)
        {
            throw new FormatException("otpauth-migration 链接缺少 data 参数。");
        }

        string? data = null;
        foreach (string pair in trimmed[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            if (equals > 0 && pair[..equals].Equals("data", StringComparison.OrdinalIgnoreCase))
            {
                data = Uri.UnescapeDataString(pair[(equals + 1)..]);
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(data))
        {
            throw new FormatException("otpauth-migration 链接缺少 data 参数。");
        }

        byte[] protobuf = DecodeBase64Url(data);
        return ParseProtobuf(protobuf);
    }

    private static List<OtpAccount> ParseProtobuf(byte[] data)
    {
        List<OtpAccount> accounts = [];
        ProtoReader reader = new(data);

        while (reader.TryReadTag(out int field, out int wireType))
        {
            if (field == 1 && wireType == 2)
            {
                OtpAccount? account = ParseOtpParameters(reader.ReadLengthDelimited());
                if (account is not null)
                {
                    accounts.Add(account);
                }
            }
            else
            {
                reader.Skip(wireType);
            }
        }

        return accounts;
    }

    private static OtpAccount? ParseOtpParameters(byte[] data)
    {
        byte[] secret = [];
        string name = string.Empty;
        string issuer = string.Empty;
        int algorithm = 0;
        int digitCount = 0;
        int otpType = 0;
        long counter = 0;

        ProtoReader reader = new(data);
        while (reader.TryReadTag(out int field, out int wireType))
        {
            switch (field)
            {
                case 1 when wireType == 2:
                    secret = reader.ReadLengthDelimited();
                    break;
                case 2 when wireType == 2:
                    name = Encoding.UTF8.GetString(reader.ReadLengthDelimited());
                    break;
                case 3 when wireType == 2:
                    issuer = Encoding.UTF8.GetString(reader.ReadLengthDelimited());
                    break;
                case 4 when wireType == 0:
                    algorithm = (int)reader.ReadVarint();
                    break;
                case 5 when wireType == 0:
                    digitCount = (int)reader.ReadVarint();
                    break;
                case 6 when wireType == 0:
                    otpType = (int)reader.ReadVarint();
                    break;
                case 7 when wireType == 0:
                    counter = (long)reader.ReadVarint();
                    break;
                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        if (secret.Length == 0)
        {
            return null;
        }

        int digits = digitCount switch
        {
            2 => 8,
            _ => 6
        };

        OtpAccount account = new()
        {
            Type = otpType == 1 ? OtpType.Hotp : OtpType.Totp,
            Issuer = issuer.Trim(),
            Label = name.Trim(),
            Secret = Base32.Encode(secret),
            Digits = digits,
            Period = TotpEngine.DefaultPeriod,
            Algorithm = algorithm switch
            {
                2 => OtpAlgorithm.Sha256,
                3 => OtpAlgorithm.Sha512,
                _ => OtpAlgorithm.Sha1
            },
            Counter = counter < 0 ? 0 : counter
        };

        // 有些导出包里 name 写成 "发行方:账号"。
        int colon = account.Label.IndexOf(':');
        if (colon > 0)
        {
            string prefix = account.Label[..colon].Trim();
            if (string.IsNullOrEmpty(account.Issuer))
            {
                account.Issuer = prefix;
            }

            account.Label = account.Label[(colon + 1)..].Trim();
        }

        account.Normalize();
        return account;
    }

    private static byte[] DecodeBase64Url(string value)
    {
        string normalized = value.Replace('-', '+').Replace('_', '/').Trim();
        switch (normalized.Length % 4)
        {
            case 2:
                normalized += "==";
                break;
            case 3:
                normalized += "=";
                break;
        }

        try
        {
            return Convert.FromBase64String(normalized);
        }
        catch (FormatException)
        {
            throw new FormatException("otpauth-migration 的 data 不是合法的 Base64Url。");
        }
    }

    /// <summary>够用的 protobuf wire-format 读取器（只支持 varint / length-delimited / 跳过）。</summary>
    private ref struct ProtoReader(byte[] data)
    {
        private readonly byte[] _data = data;
        private int _position;

        public bool TryReadTag(out int field, out int wireType)
        {
            field = 0;
            wireType = 0;
            if (_position >= _data.Length)
            {
                return false;
            }

            ulong tag = ReadVarint();
            field = (int)(tag >> 3);
            wireType = (int)(tag & 0x07);
            return field > 0;
        }

        public ulong ReadVarint()
        {
            ulong result = 0;
            int shift = 0;
            while (_position < _data.Length)
            {
                byte b = _data[_position++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    break;
                }

                shift += 7;
                if (shift > 63)
                {
                    throw new FormatException("protobuf varint 过长。");
                }
            }

            return result;
        }

        public byte[] ReadLengthDelimited()
        {
            int length = (int)ReadVarint();
            if (length < 0 || _position + length > _data.Length)
            {
                throw new FormatException("protobuf 数据长度非法。");
            }

            byte[] slice = _data[_position..(_position + length)];
            _position += length;
            return slice;
        }

        public void Skip(int wireType)
        {
            switch (wireType)
            {
                case 0:
                    ReadVarint();
                    break;
                case 1:
                    _position += 8;
                    break;
                case 2:
                    ReadLengthDelimited();
                    break;
                case 5:
                    _position += 4;
                    break;
                default:
                    throw new FormatException($"不支持的 protobuf wire type：{wireType}");
            }
        }
    }
}
