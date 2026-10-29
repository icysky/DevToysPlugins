using System.Globalization;
using System.Text;

namespace GoogleAuthenticator.DevToys.Core;

/// <summary>
/// <c>otpauth://</c> 链接解析与生成（Key Uri Format）。
/// </summary>
internal static class OtpAuthUri
{
    public const string Prefix = "otpauth://";

    public static bool LooksLike(string? text)
        => !string.IsNullOrWhiteSpace(text)
           && text.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 解析 otpauth 链接。失败时抛出 <see cref="FormatException" />，消息可直接展示给用户。
    /// </summary>
    public static OtpAccount Parse(string text)
    {
        string trimmed = text.Trim();
        if (!LooksLike(trimmed))
        {
            throw new FormatException("不是 otpauth:// 链接。");
        }

        string body = trimmed[Prefix.Length..];
        int queryStart = body.IndexOf('?');
        string pathPart = queryStart < 0 ? body : body[..queryStart];
        string queryPart = queryStart < 0 ? string.Empty : body[(queryStart + 1)..];

        int slash = pathPart.IndexOf('/');
        if (slash <= 0)
        {
            throw new FormatException("otpauth 链接缺少类型（totp 或 hotp）。");
        }

        string typeText = pathPart[..slash].Trim().ToLowerInvariant();
        string labelPart = pathPart[(slash + 1)..];

        OtpType type = typeText switch
        {
            "totp" => OtpType.Totp,
            "hotp" => OtpType.Hotp,
            _ => throw new FormatException($"不支持的动态口令类型：{typeText}")
        };

        Dictionary<string, string> query = ParseQuery(queryPart);

        if (!query.TryGetValue("secret", out string? secretText) || !Base32.TryDecode(secretText, out _))
        {
            throw new FormatException("otpauth 链接缺少合法的 secret 参数。");
        }

        string label = Uri.UnescapeDataString(labelPart.Replace('+', ' ')).Trim();
        string issuer = query.TryGetValue("issuer", out string? issuerValue)
            ? Uri.UnescapeDataString(issuerValue.Replace('+', ' ')).Trim()
            : string.Empty;

        // 标签里通常写成 "发行方:账号"，若 query 没给 issuer 就从前缀拆出来。
        int colon = label.IndexOf(':');
        if (colon > 0)
        {
            string labelIssuer = label[..colon].Trim();
            if (string.IsNullOrEmpty(issuer))
            {
                issuer = labelIssuer;
            }

            label = label[(colon + 1)..].Trim();
        }

        int digits = TotpEngine.DefaultDigits;
        if (query.TryGetValue("digits", out string? digitsText)
            && int.TryParse(digitsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedDigits))
        {
            digits = Math.Clamp(parsedDigits, TotpEngine.MinDigits, TotpEngine.MaxDigits);
        }

        int period = TotpEngine.DefaultPeriod;
        if (query.TryGetValue("period", out string? periodText)
            && int.TryParse(periodText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPeriod)
            && parsedPeriod is >= 1 and <= 86400)
        {
            period = parsedPeriod;
        }

        long counter = 0;
        if (query.TryGetValue("counter", out string? counterText)
            && long.TryParse(counterText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedCounter)
            && parsedCounter > 0)
        {
            counter = parsedCounter;
        }

        OtpAccount account = new()
        {
            Type = type,
            Issuer = issuer,
            Label = label,
            Secret = Base32.Normalize(secretText),
            Digits = digits,
            Period = period,
            Algorithm = TotpEngine.AlgorithmFromUriValue(query.GetValueOrDefault("algorithm")),
            Counter = counter
        };

        account.Normalize();
        return account;
    }

    /// <summary>把账号序列化成 otpauth 链接（导出为纯文本清单时使用）。</summary>
    public static string Build(OtpAccount account)
    {
        StringBuilder builder = new();
        builder.Append(Prefix);
        builder.Append(account.Type == OtpType.Hotp ? "hotp/" : "totp/");

        string label = string.IsNullOrWhiteSpace(account.Label) ? "account" : account.Label;
        string fullLabel = string.IsNullOrWhiteSpace(account.Issuer) ? label : $"{account.Issuer}:{label}";
        builder.Append(Uri.EscapeDataString(fullLabel));

        builder.Append("?secret=").Append(account.Secret);
        if (!string.IsNullOrWhiteSpace(account.Issuer))
        {
            builder.Append("&issuer=").Append(Uri.EscapeDataString(account.Issuer));
        }

        builder.Append("&algorithm=").Append(TotpEngine.AlgorithmToUriValue(account.Algorithm));
        builder.Append("&digits=").Append(account.Digits.ToString(CultureInfo.InvariantCulture));

        if (account.Type == OtpType.Hotp)
        {
            builder.Append("&counter=").Append(account.Counter.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            builder.Append("&period=").Append(account.Period.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            string key = Uri.UnescapeDataString(pair[..equals]);
            string value = Uri.UnescapeDataString(pair[(equals + 1)..]);
            result[key] = value;
        }

        return result;
    }
}
