namespace TestCertGenerator.CertGen;

/// <summary>
/// 证书密钥算法。
/// </summary>
public enum CertificateAlgorithm
{
    /// <summary>RSA（国际算法，SHA-256 摘要）。</summary>
    Rsa = 0,

    /// <summary>SM2（国密算法，SM3 摘要，sm2p256v1 曲线）。</summary>
    Sm2 = 1
}

/// <summary>算法相关的展示文本。</summary>
public static class CertificateAlgorithmInfo
{
    public static string GetKeyDescription(CertificateAlgorithm algorithm, int rsaKeySize)
    {
        return algorithm == CertificateAlgorithm.Sm2
            ? "SM2 256 位（sm2p256v1）"
            : $"RSA {rsaKeySize} 位";
    }

    public static string GetSignatureAlgorithm(CertificateAlgorithm algorithm)
    {
        return algorithm == CertificateAlgorithm.Sm2 ? "SM3withSM2" : "SHA256withRSA";
    }

    public static string GetProfileValue(CertificateAlgorithm algorithm)
    {
        return algorithm == CertificateAlgorithm.Sm2 ? "SM2" : "RSA";
    }

    public static CertificateAlgorithm ParseProfileValue(string? value)
    {
        return string.Equals(value, "SM2", StringComparison.OrdinalIgnoreCase)
            ? CertificateAlgorithm.Sm2
            : CertificateAlgorithm.Rsa;
    }
}
