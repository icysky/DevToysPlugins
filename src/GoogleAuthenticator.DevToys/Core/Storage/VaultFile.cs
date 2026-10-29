using System.Security.Cryptography;
using System.Text;

namespace GoogleAuthenticator.DevToys.Core.Storage;

/// <summary>导出文件的头部信息（无需密码即可读取，用于判断是否加密）。</summary>
internal sealed class VaultHeader
{
    public bool IsEncrypted { get; init; }

    public int Version { get; init; }
}

/// <summary>导出文件内容。</summary>
internal sealed class VaultPayload
{
    public string Schema { get; set; } = "googleauthenticator.devtoys/1";

    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;

    public string Application { get; set; } = "GoogleAuthenticator.DevToys";

    public List<OtpAccount> Accounts { get; set; } = [];
}

internal sealed class VaultFormatException(string message) : Exception(message);

internal sealed class VaultPasswordRequiredException() : Exception("该备份文件已加密，请输入密码。");

internal sealed class VaultPasswordException() : Exception("密码错误，无法解密备份文件。");

/// <summary>
/// 备份文件容器（扩展名 <c>.gauth</c>）。
/// <para>
/// 布局：<c>"GAEX"</c> | version(1) | flags(1) | 之后是负载。
/// flags 不含加密位时，负载直接是 UTF-8 JSON；
/// 含加密位时为 <c>salt(16) | iv(16) | ciphertext | hmac(32)</c>，
/// 密钥由 PBKDF2-HMAC-SHA256(密码, salt, 210000 轮, 64 字节) 派生，前 32 字节做 AES-256-CBC 密钥，
/// 后 32 字节做 HMAC-SHA256 完整性校验（覆盖头部 + salt + iv + ciphertext）。
/// </para>
/// </summary>
internal static class VaultFile
{
    public const string FileExtension = ".gauth";

    private static readonly byte[] Magic = "GAEX"u8.ToArray();
    private const byte CurrentVersion = 1;
    private const byte FlagEncrypted = 0x01;
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int MacSize = 32;
    private const int Pbkdf2Iterations = 210_000;

    public static bool LooksLikeVault(ReadOnlySpan<byte> data)
        => data.Length >= Magic.Length + 2 && data[..Magic.Length].SequenceEqual(Magic);

    public static VaultHeader ReadHeader(byte[] data)
    {
        ValidateHeader(data);
        return new VaultHeader
        {
            Version = data[Magic.Length],
            IsEncrypted = (data[Magic.Length + 1] & FlagEncrypted) != 0
        };
    }

    /// <summary>生成备份文件内容。<paramref name="password" /> 为空表示不加密。</summary>
    public static byte[] Create(IEnumerable<OtpAccount> accounts, string? password)
    {
        VaultPayload payload = new() { Accounts = [.. accounts] };
        byte[] plain = JsonConfig.SerializeToUtf8Bytes(payload);

        bool encrypt = !string.IsNullOrEmpty(password);

        using MemoryStream output = new();
        output.Write(Magic);
        output.WriteByte(CurrentVersion);
        output.WriteByte(encrypt ? FlagEncrypted : (byte)0);

        if (!encrypt)
        {
            output.Write(plain);
            return output.ToArray();
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] iv = RandomNumberGenerator.GetBytes(IvSize);
        (byte[] encryptionKey, byte[] macKey) = DeriveKeys(password!, salt);

        byte[] cipher;
        using (Aes aes = Aes.Create())
        {
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = encryptionKey;
            aes.IV = iv;
            using ICryptoTransform encryptor = aes.CreateEncryptor();
            cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
        }

        output.Write(salt);
        output.Write(iv);
        output.Write(cipher);

        byte[] mac = HMACSHA256.HashData(macKey, output.ToArray());
        output.Write(mac);

        CryptographicOperations.ZeroMemory(encryptionKey);
        CryptographicOperations.ZeroMemory(macKey);
        CryptographicOperations.ZeroMemory(plain);

        return output.ToArray();
    }

    /// <summary>读取备份文件。<paramref name="password" /> 仅在文件加密时需要。</summary>
    public static VaultPayload Read(byte[] data, string? password)
    {
        ValidateHeader(data);
        bool encrypted = (data[Magic.Length + 1] & FlagEncrypted) != 0;
        int offset = Magic.Length + 2;

        if (!encrypted)
        {
            VaultPayload? plainPayload = Deserialize(data.AsSpan(offset));
            return plainPayload ?? throw new VaultFormatException("备份文件内容为空。");
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new VaultPasswordRequiredException();
        }

        if (data.Length < offset + SaltSize + IvSize + MacSize + 1)
        {
            throw new VaultFormatException("备份文件已损坏（长度不足）。");
        }

        byte[] salt = data[offset..(offset + SaltSize)];
        offset += SaltSize;
        byte[] iv = data[offset..(offset + IvSize)];
        offset += IvSize;

        int cipherLength = data.Length - offset - MacSize;
        if (cipherLength <= 0)
        {
            throw new VaultFormatException("备份文件已损坏（密文为空）。");
        }

        byte[] cipher = data[offset..(offset + cipherLength)];
        byte[] mac = data[(offset + cipherLength)..];

        (byte[] encryptionKey, byte[] macKey) = DeriveKeys(password, salt);

        byte[] expectedMac = HMACSHA256.HashData(macKey, data.AsSpan(0, offset + cipherLength).ToArray());
        CryptographicOperations.ZeroMemory(macKey);

        if (!CryptographicOperations.FixedTimeEquals(mac, expectedMac))
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
            throw new VaultPasswordException();
        }

        byte[] plain;
        try
        {
            using Aes aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = encryptionKey;
            aes.IV = iv;
            using ICryptoTransform decryptor = aes.CreateDecryptor();
            plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        }
        catch (CryptographicException)
        {
            throw new VaultFormatException("备份文件无法解密（内容可能已损坏）。");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
        }

        VaultPayload? payload;
        try
        {
            payload = Deserialize(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }

        return payload ?? throw new VaultFormatException("备份文件内容为空。");
    }

    private static VaultPayload? Deserialize(ReadOnlySpan<byte> json)
    {
        try
        {
            return JsonConfig.Deserialize<VaultPayload>(json);
        }
        catch (Exception ex)
        {
            throw new VaultFormatException($"备份文件不是有效的 JSON：{ex.Message}");
        }
    }

    private static (byte[] EncryptionKey, byte[] MacKey) DeriveKeys(string password, byte[] salt)
    {
        byte[] material = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            64);

        byte[] encryptionKey = material[..32];
        byte[] macKey = material[32..];
        CryptographicOperations.ZeroMemory(material);
        return (encryptionKey, macKey);
    }

    private static void ValidateHeader(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < Magic.Length + 2 || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new VaultFormatException("不是本插件导出的备份文件（文件头不匹配）。");
        }

        byte version = data[Magic.Length];
        if (version != CurrentVersion)
        {
            throw new VaultFormatException($"备份文件版本（{version}）不受支持。");
        }
    }
}
