namespace GoogleAuthenticator.DevToys.Core.Storage;

/// <summary>
/// 账号本地持久化。数据文件位于 <c>%LOCALAPPDATA%\GoogleAuthenticator.DevToys\accounts.dat</c>，
/// Windows 上用 DPAPI 加密（绑定当前用户），其他平台退化为明文 JSON。
/// </summary>
internal sealed class AccountStore
{
    /// <summary>覆盖数据目录的环境变量（测试、便携部署用）。</summary>
    public const string DataDirectoryEnvironmentVariable = "GOOGLEAUTHENTICATOR_DEVTOYS_DATA_DIR";

    private static readonly byte[] Magic = "GALT"u8.ToArray();
    private const byte CurrentVersion = 1;
    private const byte FlagDpapi = 0x01;

    public AccountStore(string? directory = null)
    {
        Directory = string.IsNullOrWhiteSpace(directory) ? ResolveDefaultDirectory() : directory!;
        FilePath = Path.Combine(Directory, "accounts.dat");
    }

    public string Directory { get; }

    public string FilePath { get; }

    public List<OtpAccount> Accounts { get; private set; } = [];

    /// <summary>最近一次加载/保存的错误信息，供界面提示用。</summary>
    public string? LastError { get; private set; }

    public static string ResolveDefaultDirectory()
    {
        string? overridden = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden;
        }

        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.GetTempPath();
        }

        return Path.Combine(root, "GoogleAuthenticator.DevToys");
    }

    public void Load()
    {
        LastError = null;
        Accounts = [];

        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            byte[] raw = File.ReadAllBytes(FilePath);
            if (raw.Length < Magic.Length + 2 || !raw.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            {
                LastError = "本地账号文件格式无法识别，已忽略。";
                return;
            }

            byte version = raw[Magic.Length];
            if (version != CurrentVersion)
            {
                LastError = $"本地账号文件版本（{version}）不受支持，已忽略。";
                return;
            }

            byte flags = raw[Magic.Length + 1];
            byte[] payload = raw[(Magic.Length + 2)..];

            if ((flags & FlagDpapi) != 0)
            {
                if (!Dpapi.IsSupported)
                {
                    LastError = "本地账号文件由 Windows DPAPI 加密，当前平台无法解密。";
                    return;
                }

                payload = Dpapi.Unprotect(payload);
            }

            List<OtpAccount>? loaded = JsonConfig.Deserialize<List<OtpAccount>>(payload);
            if (loaded is not null)
            {
                foreach (OtpAccount account in loaded)
                {
                    account.Normalize();
                }

                Accounts = loaded;
            }
        }
        catch (Exception ex)
        {
            Accounts = [];
            LastError = $"读取本地账号失败：{ex.Message}";
        }
    }

    public bool Save()
    {
        LastError = null;

        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            byte[] payload = JsonConfig.SerializeToUtf8Bytes(Accounts);
            byte flags = 0;

            if (Dpapi.IsSupported)
            {
                payload = Dpapi.Protect(payload);
                flags |= FlagDpapi;
            }

            using MemoryStream buffer = new();
            buffer.Write(Magic);
            buffer.WriteByte(CurrentVersion);
            buffer.WriteByte(flags);
            buffer.Write(payload);

            string tempPath = FilePath + ".tmp";
            File.WriteAllBytes(tempPath, buffer.ToArray());
            File.Move(tempPath, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"保存本地账号失败：{ex.Message}";
            return false;
        }
    }
}
