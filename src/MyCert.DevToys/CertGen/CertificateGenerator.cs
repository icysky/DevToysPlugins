namespace TestCertGenerator.CertGen;

/// <summary>
/// 证书生成入口：按密钥算法分派到 RSA 或 SM2 实现，输出物保持一致。
/// </summary>
public static class CertificateGenerator
{
    public static GeneratedCertificate Generate(CertificateSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (string.IsNullOrWhiteSpace(spec.SubjectDn) || string.IsNullOrWhiteSpace(spec.IssuerDn))
        {
            throw new InvalidOperationException("证书主题与颁发者不能为空。");
        }

        if (spec.CreatePfx && string.IsNullOrEmpty(spec.PfxPassword))
        {
            throw new InvalidOperationException("已选择导出 PFX，请填写 PFX 密码。");
        }

        return spec.Algorithm switch
        {
            CertificateAlgorithm.Sm2 => Sm2CertificateGenerator.Generate(spec),
            _ => RsaCertificateGenerator.Generate(spec)
        };
    }
}
