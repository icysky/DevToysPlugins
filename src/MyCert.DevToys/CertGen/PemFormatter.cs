using System.Text;

namespace TestCertGenerator.CertGen;

/// <summary>
/// PEM 文本的统一格式化。
///
/// 为什么需要它：.NET 的 <c>PemEncoding</c>（<c>ExportCertificatePem()</c> /
/// <c>ExportPkcs8PrivateKeyPem()</c>）产出的是 LF 换行，但**结尾不带换行**。
/// 于是把两段 PEM 直接拼在一起（例如证书链）就会出现
/// <c>-----END CERTIFICATE----------BEGIN CERTIFICATE-----</c>，
/// OpenSSL / nginx（含 Tongsuo）会报 <c>bad end line</c>。
///
/// 这里统一成：**只用 LF**、64 字符折行、**每块结尾必带一个换行**。
/// LF 是 PEM 的通用形式——OpenSSL、nginx/Tongsuo、Java、IIS 都接受；
/// 反之 CRLF 在 nginx/Tongsuo 上会被判为非法字符。
/// </summary>
internal static class PemFormatter
{
    /// <summary>PEM 一律使用 LF，避免 Windows 上的 CR 被 OpenSSL/nginx 当成正文内容。</summary>
    public const string LineEnding = "\n";

    /// <summary>把 DER 编码成标准 PEM 文本（LF、64 列、结尾换行）。</summary>
    public static string Create(string label, ReadOnlySpan<byte> der)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var builder = new StringBuilder(der.Length * 2);
        builder.Append("-----BEGIN ").Append(label).Append("-----").Append(LineEnding);

        string base64 = Convert.ToBase64String(der);
        for (int offset = 0; offset < base64.Length; offset += 64)
        {
            builder.Append(base64, offset, Math.Min(64, base64.Length - offset)).Append(LineEnding);
        }

        builder.Append("-----END ").Append(label).Append("-----").Append(LineEnding);
        return builder.ToString();
    }

    /// <summary>
    /// 规范化任意来源的 PEM 文本：去掉 BOM 与 CR、去掉行尾空白、去掉首尾空行，
    /// 并保证以单个 LF 结尾。用于把外部传入（或旧版本生成）的 PEM 变成可安全拼接的形式。
    /// </summary>
    public static string Normalize(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            return string.Empty;
        }

        string text = pem.Replace("\r\n", LineEnding, StringComparison.Ordinal)
                         .Replace('\r', '\n')
                         .TrimStart('\uFEFF')
                         .Trim();

        return text.Length == 0 ? string.Empty : text + LineEnding;
    }

    /// <summary>
    /// 拼接多个 PEM 块（证书链）。每块都先规范化，块与块之间只隔一个换行，
    /// 不会再出现 <c>-----END---------BEGIN-----</c> 粘连。
    /// </summary>
    public static string Concat(params string?[] blocks)
    {
        var builder = new StringBuilder();
        foreach (string? block in blocks)
        {
            string normalized = Normalize(block);
            if (normalized.Length > 0)
            {
                builder.Append(normalized);
            }
        }

        return builder.ToString();
    }
}
