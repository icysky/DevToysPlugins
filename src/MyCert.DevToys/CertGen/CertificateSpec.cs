using System.Security.Cryptography.X509Certificates;

namespace TestCertGenerator.CertGen;

/// <summary>
/// 一次证书生成的完整参数（RSA 与 SM2 共用，保证两种算法产出结构一致）。
/// </summary>
public sealed class CertificateSpec
{
    public CertificateAlgorithm Algorithm { get; init; }

    public string SubjectDn { get; init; } = string.Empty;

    public string IssuerDn { get; init; } = string.Empty;

    public int RsaKeySize { get; init; } = 2048;

    public X509KeyUsageFlags KeyUsage { get; init; }

    public bool KeyUsageCritical { get; init; }

    public IReadOnlyList<string> ExtendedKeyUsageOids { get; init; } = [];

    public bool ExtendedKeyUsageCritical { get; init; }

    public bool IncludeSubjectAlternativeName { get; init; }

    public IReadOnlyList<string> SubjectAlternativeNames { get; init; } = [];

    public DateTimeOffset NotBefore { get; init; }

    public DateTimeOffset NotAfter { get; init; }

    public bool CreatePfx { get; init; }

    public string PfxPassword { get; init; } = string.Empty;

    public string FriendlyName { get; init; } = string.Empty;
}

/// <summary>
/// 证书生成结果。无论 RSA 还是 SM2，都提供同样的导出物。
/// </summary>
public sealed class GeneratedCertificate : IDisposable
{
    public GeneratedCertificate(
        byte[] certificateDer,
        byte[] issuerCertificateDer,
        byte[]? pfxBytes,
        string certificatePem,
        string privateKeyPem,
        string keyDescription,
        string signatureAlgorithm,
        System.Security.Cryptography.X509Certificates.X509Certificate2? windowsCertificate)
    {
        CertificateDer = certificateDer;
        IssuerCertificateDer = issuerCertificateDer;
        PfxBytes = pfxBytes;
        CertificatePem = certificatePem;
        PrivateKeyPem = privateKeyPem;
        KeyDescription = keyDescription;
        SignatureAlgorithm = signatureAlgorithm;
        WindowsCertificate = windowsCertificate;
    }

    /// <summary>终端实体证书 DER。</summary>
    public byte[] CertificateDer { get; }

    /// <summary>颁发者 CA 证书 DER。</summary>
    public byte[] IssuerCertificateDer { get; }

    /// <summary>PKCS#12 内容；未勾选导出 PFX 时为 null。</summary>
    public byte[]? PfxBytes { get; }

    /// <summary>证书 PEM。</summary>
    public string CertificatePem { get; }

    /// <summary>私钥 PEM（PKCS#8）。</summary>
    public string PrivateKeyPem { get; }

    /// <summary>密钥描述，例如 "RSA 2048 位" / "SM2 256 位（sm2p256v1）"。</summary>
    public string KeyDescription { get; }

    /// <summary>签名算法描述，例如 "SHA256withRSA" / "SM3withSM2"。</summary>
    public string SignatureAlgorithm { get; }

    /// <summary>
    /// Windows 可用的证书对象（仅 RSA）。SM2 证书无法由 Windows 加解密栈承载，
    /// 因此为 null，相关功能需走 BouncyCastle。
    /// </summary>
    public System.Security.Cryptography.X509Certificates.X509Certificate2? WindowsCertificate { get; }

    public void Dispose()
    {
        WindowsCertificate?.Dispose();
    }
}
