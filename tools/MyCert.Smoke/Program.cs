// 证书插件回归测试：重点是 PEM 文本规范。
//
// 背景：.NET 的 ExportCertificatePem() 产出 LF 但**结尾不带换行**，直接把两段 PEM 拼成
// 证书链会出现 "-----END CERTIFICATE----------BEGIN CERTIFICATE-----"，nginx/Tongsuo 报
// "bad end line"。这里把 PEM 的换行、结尾、拼接顺序都固定成断言，防止以后改回去。
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using TestCertGenerator.CertGen;

int passed = 0;
int failed = 0;

Console.WriteLine("=== MyCert 回归（PEM 文本规范 / 证书链拼接） ===");
Console.WriteLine();

CertificateSpec rsaSpec = new()
{
    Algorithm = CertificateAlgorithm.Rsa,
    SubjectDn = "CN=Leaf For Pem Test, O=Test",
    IssuerDn = "CN=Root CA For Pem Test, O=Test",
    RsaKeySize = 2048,
    KeyUsage = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
    KeyUsageCritical = true,
    ExtendedKeyUsageOids = ["1.3.6.1.5.5.7.3.1"],
    IncludeSubjectAlternativeName = true,
    SubjectAlternativeNames = ["localhost"],
    NotBefore = DateTimeOffset.UtcNow.AddDays(-1),
    NotAfter = DateTimeOffset.UtcNow.AddYears(1),
    CreatePfx = false
};

using GeneratedCertificate rsa = CertificateGenerator.Generate(rsaSpec);

Section("RSA 证书 PEM");
Check("不含 CR（纯 LF）", () => !rsa.CertificatePem.Contains('\r'));
Check("以换行结尾", () => rsa.CertificatePem.EndsWith('\n'));
Check("单块 BEGIN/END", () => Count(rsa.CertificatePem, "-----BEGIN ") == 1 && Count(rsa.CertificatePem, "-----END ") == 1);
Check("每行不超过 64 字符", () => AllBodyLinesWithin(rsa.CertificatePem, 64));
Check("PEM 能被 X509Certificate2 解析且主题正确", () =>
{
    using X509Certificate2 cert = X509Certificate2.CreateFromPem(rsa.CertificatePem);
    return cert.Subject.Contains("Leaf For Pem Test", StringComparison.Ordinal);
});

Section("RSA 私钥 PEM");
Check("不含 CR（纯 LF）", () => !rsa.PrivateKeyPem.Contains('\r'));
Check("以换行结尾", () => rsa.PrivateKeyPem.EndsWith('\n'));
Check("PKCS#8 标签", () => rsa.PrivateKeyPem.StartsWith("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal));
Check("能被 RSA 读回", () =>
{
    using RSA key = RSA.Create();
    key.ImportFromPem(rsa.PrivateKeyPem);
    return key.KeySize == 2048;
});

Section("证书链（fullchain）");
Check("包含两张证书", () => Count(rsa.CertificateChainPem, "-----BEGIN CERTIFICATE-----") == 2);
Check("没有 END/BEGIN 粘连", () => !rsa.CertificateChainPem.Contains("----------", StringComparison.Ordinal));
Check("以换行结尾", () => rsa.CertificateChainPem.EndsWith('\n'));
Check("第一块是终端证书、第二块是 CA", () =>
{
    string[] blocks = Regex.Split(rsa.CertificateChainPem.Trim(), "(?=-----BEGIN CERTIFICATE-----)")
        .Where(b => b.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal))
        .ToArray();

    if (blocks.Length != 2)
    {
        return false;
    }

    using X509Certificate2 leaf = X509Certificate2.CreateFromPem(blocks[0]);
    using X509Certificate2 ca = X509Certificate2.CreateFromPem(blocks[1]);
    return leaf.Subject.Contains("Leaf For Pem Test", StringComparison.Ordinal)
           && ca.Subject.Contains("Root CA For Pem Test", StringComparison.Ordinal);
});
Check("链里每张证书都能单独解析", () =>
{
    foreach (Match match in Regex.Matches(
                 rsa.CertificateChainPem,
                 "-----BEGIN CERTIFICATE-----[\\s\\S]*?-----END CERTIFICATE-----"))
    {
        X509Certificate2.CreateFromPem(match.Value).Dispose();
    }

    return true;
});

Section("PemFormatter");
Check("Create 生成结尾换行且无 CR", () =>
{
    string pem = PemFormatter.Create("CERTIFICATE", rsa.CertificateDer);
    return pem.EndsWith('\n') && !pem.Contains('\r');
});
Check("Normalize 把 CRLF 归一成 LF", () =>
{
    string crlf = rsa.CertificatePem.Replace("\n", "\r\n", StringComparison.Ordinal);
    string normalized = PemFormatter.Normalize(crlf);
    return !normalized.Contains('\r') && normalized.EndsWith('\n');
});
Check("Normalize 给缺换行的输入补上结尾换行", () =>
{
    string trimmed = rsa.CertificatePem.TrimEnd('\n');
    return PemFormatter.Normalize(trimmed).EndsWith('\n');
});
Check("Concat 拼接两段不粘连", () =>
{
    string combined = PemFormatter.Concat(rsa.CertificatePem.TrimEnd('\n'), rsa.CertificatePem);
    return Count(combined, "-----BEGIN ") == 2
           && !combined.Contains("----------", StringComparison.Ordinal);
});
Check("Concat 忽略空块", () =>
    PemFormatter.Concat(null, string.Empty, rsa.CertificatePem).Length == rsa.CertificatePem.Length);

Section("SM2 证书 PEM");
Check("SM2 生成成功且 PEM 规范", () =>
{
    CertificateSpec sm2Spec = new()
    {
        Algorithm = CertificateAlgorithm.Sm2,
        SubjectDn = "CN=SM2 Leaf, O=Test",
        IssuerDn = "CN=SM2 Root CA, O=Test",
        KeyUsage = X509KeyUsageFlags.DigitalSignature,
        ExtendedKeyUsageOids = ["1.3.6.1.5.5.7.3.2"],
        NotBefore = DateTimeOffset.UtcNow.AddDays(-1),
        NotAfter = DateTimeOffset.UtcNow.AddYears(1)
    };

    using GeneratedCertificate sm2 = CertificateGenerator.Generate(sm2Spec);
    return sm2.CertificatePem.StartsWith("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)
           && !sm2.CertificatePem.Contains('\r')
           && sm2.CertificatePem.EndsWith('\n')
           && sm2.CertificateChainPem.EndsWith('\n')
           && !sm2.CertificateChainPem.Contains("----------", StringComparison.Ordinal)
           && sm2.PrivateKeyPem.StartsWith("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal);
});

Console.WriteLine();
Console.WriteLine($"结果：通过 {passed}，失败 {failed}");
return failed == 0 ? 0 : 1;

// ---------------------------------------------------------------- 辅助

void Section(string title)
{
    Console.WriteLine();
    Console.WriteLine($"-- {title}");
}

void Check(string name, Func<bool> condition)
{
    try
    {
        if (condition())
        {
            passed++;
            Console.WriteLine($"  [PASS] {name}");
            return;
        }

        failed++;
        Console.WriteLine($"  [FAIL] {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"  [FAIL] {name} -> {ex.GetType().Name}: {ex.Message}");
    }
}

static int Count(string text, string needle)
    => Regex.Matches(text, Regex.Escape(needle)).Count;

static bool AllBodyLinesWithin(string pem, int maxLength)
{
    foreach (string line in pem.Split('\n'))
    {
        string trimmed = line.TrimEnd('\r');
        if (trimmed.Length == 0 || trimmed.StartsWith("-----", StringComparison.Ordinal))
        {
            continue;
        }

        if (trimmed.Length > maxLength)
        {
            return false;
        }
    }

    return true;
}
