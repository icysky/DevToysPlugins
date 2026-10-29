namespace GoogleAuthenticator.DevToys.Core;

/// <summary>口令类型。</summary>
internal enum OtpType
{
    /// <summary>基于时间（RFC 6238）。</summary>
    Totp = 0,

    /// <summary>基于计数器（RFC 4226）。</summary>
    Hotp = 1
}

/// <summary>HMAC 摘要算法。</summary>
internal enum OtpAlgorithm
{
    Sha1 = 0,
    Sha256 = 1,
    Sha512 = 2
}

/// <summary>
/// 一个动态口令账号。字段与 <c>otpauth://</c> 参数一一对应，便于无损导出/导入。
/// </summary>
/// <remarks>
/// 展示用的派生信息一律做成方法而不是属性：属性要加 <c>[JsonIgnore]</c> 才能不被序列化，
/// 而 <c>[JsonIgnore]</c> 会把 <c>System.Text.Json</c> 拉进插件元数据扫描（详见 <see cref="JsonConfig" /> 的说明）。
/// </remarks>
internal sealed class OtpAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public OtpType Type { get; set; } = OtpType.Totp;

    /// <summary>发行方，例如 <c>Google</c>。</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>账号标识，例如 <c>alice@example.com</c>。</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Base32 密钥（大写、无填充）。</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>验证码位数，6~12。默认为 6（RFC 6238 默认值）。</summary>
    public int Digits { get; set; } = TotpEngine.DefaultDigits;

    /// <summary>TOTP 时间步长（秒），默认 30。</summary>
    public int Period { get; set; } = TotpEngine.DefaultPeriod;

    public OtpAlgorithm Algorithm { get; set; } = OtpAlgorithm.Sha1;

    /// <summary>HOTP 计数器，TOTP 时忽略。</summary>
    public long Counter { get; set; }

    /// <summary>创建时间（Unix 秒），用于稳定排序。</summary>
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public string GetDisplayTitle()
    {
        if (!string.IsNullOrWhiteSpace(Issuer))
        {
            return Issuer;
        }

        return string.IsNullOrWhiteSpace(Label) ? "未命名账号" : Label;
    }

    public string GetDisplaySubtitle()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(Label) ? string.Empty : Label;
    }

    public string GetAlgorithmDisplay() => Algorithm switch
    {
        OtpAlgorithm.Sha256 => "SHA256",
        OtpAlgorithm.Sha512 => "SHA512",
        _ => "SHA1"
    };

    public string GetMetaDisplay() => Type == OtpType.Hotp
        ? $"HOTP · {Digits} 位 · {GetAlgorithmDisplay()}"
        : $"TOTP · {Digits} 位 · {Period} 秒 · {GetAlgorithmDisplay()}";

    public OtpAccount Clone() => new()
    {
        Id = Id,
        Type = Type,
        Issuer = Issuer,
        Label = Label,
        Secret = Secret,
        Digits = Digits,
        Period = Period,
        Algorithm = Algorithm,
        Counter = Counter,
        CreatedAt = CreatedAt
    };

    /// <summary>规范化可变字段，保证后续计算与导出稳定。</summary>
    public void Normalize()
    {
        Secret = Base32.Normalize(Secret);
        Issuer = (Issuer ?? string.Empty).Trim();
        Label = (Label ?? string.Empty).Trim();
        Digits = Math.Clamp(Digits, TotpEngine.MinDigits, TotpEngine.MaxDigits);
        if (Period is < 1 or > 86400)
        {
            Period = TotpEngine.DefaultPeriod;
        }

        if (Counter < 0)
        {
            Counter = 0;
        }
    }
}
