using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.X509.Extension;
using BcX509Certificate = Org.BouncyCastle.X509.X509Certificate;

namespace TestCertGenerator.CertGen;

/// <summary>
/// SM2 国密证书生成（BouncyCastle）。
/// 参数与 RSA 版逐项对齐：同样的 DN、有效期、KU/EKU/SAN、CA 层级与导出物，
/// 区别仅在于密钥算法（sm2p256v1）与签名算法（SM3withSM2）。
/// </summary>
internal static class Sm2CertificateGenerator
{
    private const string SignatureAlgorithmName = "SM3withSM2";

    /// <summary>SM2 椭圆曲线 OID（GM/T 0006），公钥参数使用命名曲线而非显式参数。</summary>
    private static readonly DerObjectIdentifier Sm2CurveOid = new("1.2.156.10197.1.301");

    private const string Sm2CurveName = "sm2p256v1";

    public static GeneratedCertificate Generate(CertificateSpec spec)
    {
        ECNamedDomainParameters domain = CreateDomainParameters();
        AsymmetricCipherKeyPair issuerKeyPair = GenerateKeyPair(domain);
        AsymmetricCipherKeyPair subjectKeyPair = GenerateKeyPair(domain);

        // 与 .NET 的 X500DistinguishedName 编码顺序保持一致（DER 中通用名在最后）。
        var issuerName = new X509Name(true, spec.IssuerDn);
        var subjectName = new X509Name(true, spec.SubjectDn);

        SubjectPublicKeyInfo subjectPublicKeyInfo =
            SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(subjectKeyPair.Public);

        BcX509Certificate issuerCert = CreateIssuerCertificate(spec, issuerName, issuerKeyPair);
        BcX509Certificate cert = CreateEndEntityCertificate(
            spec, subjectName, subjectPublicKeyInfo, issuerCert, issuerKeyPair.Private);

        // 自检：确认签名链可验证、公私钥匹配，避免产出不可用证书。
        cert.Verify(issuerCert.GetPublicKey());

        byte[]? pfxBytes = spec.CreatePfx
            ? CreatePfx(spec, cert, subjectKeyPair.Private)
            : null;

        return new GeneratedCertificate(
            certificateDer: cert.GetEncoded(),
            issuerCertificateDer: issuerCert.GetEncoded(),
            pfxBytes: pfxBytes,
            certificatePem: ToPem("CERTIFICATE", cert.GetEncoded()),
            privateKeyPem: ToPem("PRIVATE KEY", PrivateKeyInfoFactory.CreatePrivateKeyInfo(subjectKeyPair.Private).GetEncoded()),
            keyDescription: CertificateAlgorithmInfo.GetKeyDescription(CertificateAlgorithm.Sm2, 0),
            signatureAlgorithm: SignatureAlgorithmName,
            windowsCertificate: null);
    }

    private static ECNamedDomainParameters CreateDomainParameters()
    {
        Org.BouncyCastle.Asn1.X9.X9ECParameters curve =
            Org.BouncyCastle.Asn1.X9.ECNamedCurveTable.GetByName(Sm2CurveName);
        return new ECNamedDomainParameters(Sm2CurveOid, curve);
    }

    private static AsymmetricCipherKeyPair GenerateKeyPair(ECDomainParameters domain)
    {
        var generator = new ECKeyPairGenerator();
        generator.Init(new ECKeyGenerationParameters(domain, new SecureRandom()));
        return generator.GenerateKeyPair();
    }

    private static BcX509Certificate CreateIssuerCertificate(
        CertificateSpec spec,
        X509Name issuerName,
        AsymmetricCipherKeyPair issuerKeyPair)
    {
        var generator = new X509V3CertificateGenerator();
        generator.SetSerialNumber(NewSerialNumber());
        generator.SetIssuerDN(issuerName);
        generator.SetSubjectDN(issuerName);
        generator.SetNotBefore(ToUtc(spec.NotBefore.AddDays(-1)));
        generator.SetNotAfter(ToUtc(spec.NotAfter.AddYears(3)));
        generator.SetSubjectPublicKeyInfo(
            SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(issuerKeyPair.Public));

        // 与 RSA 版一致：CA 无路径长度限制、扩展关键。
        generator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(true));
        generator.AddExtension(
            X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.KeyCertSign | KeyUsage.CrlSign));
        generator.AddExtension(
            X509Extensions.SubjectKeyIdentifier, false,
            X509ExtensionUtilities.CreateSubjectKeyIdentifier(
                SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(issuerKeyPair.Public)));

        return generator.Generate(new Asn1SignatureFactory(SignatureAlgorithmName, issuerKeyPair.Private));
    }

    private static BcX509Certificate CreateEndEntityCertificate(
        CertificateSpec spec,
        X509Name subjectName,
        SubjectPublicKeyInfo subjectPublicKeyInfo,
        BcX509Certificate issuerCert,
        AsymmetricKeyParameter issuerPrivateKey)
    {
        var generator = new X509V3CertificateGenerator();
        generator.SetSerialNumber(NewSerialNumber());
        generator.SetIssuerDN(issuerCert.SubjectDN);
        generator.SetSubjectDN(subjectName);
        generator.SetNotBefore(ToUtc(spec.NotBefore));
        generator.SetNotAfter(ToUtc(spec.NotAfter));
        generator.SetSubjectPublicKeyInfo(subjectPublicKeyInfo);

        generator.AddExtension(X509Extensions.BasicConstraints, false, new BasicConstraints(false));
        generator.AddExtension(
            X509Extensions.SubjectKeyIdentifier, false,
            X509ExtensionUtilities.CreateSubjectKeyIdentifier(subjectPublicKeyInfo));

        generator.AddExtension(
            X509Extensions.KeyUsage, spec.KeyUsageCritical, new KeyUsage(MapKeyUsage(spec.KeyUsage)));

        if (spec.ExtendedKeyUsageOids.Count > 0)
        {
            var purposes = spec.ExtendedKeyUsageOids
                .Select(oid => new DerObjectIdentifier(oid))
                .ToArray();
            generator.AddExtension(
                X509Extensions.ExtendedKeyUsage, spec.ExtendedKeyUsageCritical, new ExtendedKeyUsage(purposes));
        }

        if (spec.IncludeSubjectAlternativeName && spec.SubjectAlternativeNames.Count > 0)
        {
            GeneralName[] names =
            [
                .. spec.SubjectAlternativeNames.Select(CreateGeneralName)
            ];
            generator.AddExtension(X509Extensions.SubjectAlternativeName, false, new GeneralNames(names));
        }

        return generator.Generate(new Asn1SignatureFactory(SignatureAlgorithmName, issuerPrivateKey));
    }

    private static GeneralName CreateGeneralName(string entry)
    {
        if (IPAddress.TryParse(entry, out IPAddress? ipAddress))
        {
            return new GeneralName(GeneralName.IPAddress, ipAddress.ToString());
        }

        return new GeneralName(GeneralName.DnsName, entry);
    }

    private static byte[] CreatePfx(
        CertificateSpec spec,
        BcX509Certificate cert,
        AsymmetricKeyParameter privateKey)
    {
        // 使用 PKCS#12 传统方案（3DES + SHA-1），与 Java/OpenSSL/BC 工具链兼容性最好。
        var store = new Pkcs12StoreBuilder()
            .SetKeyAlgorithm(PkcsObjectIdentifiers.PbeWithShaAnd3KeyTripleDesCbc)
            .SetCertAlgorithm(PkcsObjectIdentifiers.PbeWithShaAnd3KeyTripleDesCbc)
            .Build();

        const string alias = "private";
        store.SetKeyEntry(alias, new AsymmetricKeyEntry(privateKey), [new X509CertificateEntry(cert)]);

        if (!string.IsNullOrWhiteSpace(spec.FriendlyName))
        {
            store.SetFriendlyName(alias, spec.FriendlyName);
        }

        using var output = new MemoryStream();
        store.Save(output, spec.PfxPassword.ToCharArray(), new SecureRandom());
        return output.ToArray();
    }

    private static int MapKeyUsage(X509KeyUsageFlags flags)
    {
        int usage = 0;

        if (flags.HasFlag(X509KeyUsageFlags.DigitalSignature)) usage |= KeyUsage.DigitalSignature;
        if (flags.HasFlag(X509KeyUsageFlags.NonRepudiation)) usage |= KeyUsage.NonRepudiation;
        if (flags.HasFlag(X509KeyUsageFlags.KeyEncipherment)) usage |= KeyUsage.KeyEncipherment;
        if (flags.HasFlag(X509KeyUsageFlags.DataEncipherment)) usage |= KeyUsage.DataEncipherment;
        if (flags.HasFlag(X509KeyUsageFlags.KeyAgreement)) usage |= KeyUsage.KeyAgreement;
        if (flags.HasFlag(X509KeyUsageFlags.KeyCertSign)) usage |= KeyUsage.KeyCertSign;
        if (flags.HasFlag(X509KeyUsageFlags.CrlSign)) usage |= KeyUsage.CrlSign;
        if (flags.HasFlag(X509KeyUsageFlags.EncipherOnly)) usage |= KeyUsage.EncipherOnly;
        if (flags.HasFlag(X509KeyUsageFlags.DecipherOnly)) usage |= KeyUsage.DecipherOnly;

        return usage;
    }

    private static BigInteger NewSerialNumber()
    {
        byte[] bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        bytes[0] &= 0x7F;
        if (bytes[0] == 0)
        {
            bytes[0] = 0x01;
        }

        return new BigInteger(1, bytes);
    }

    private static DateTime ToUtc(DateTimeOffset value) => value.UtcDateTime;

    private static string ToPem(string label, byte[] der)
    {
        var builder = new StringBuilder();
        builder.Append("-----BEGIN ").Append(label).Append("-----\n");

        string base64 = Convert.ToBase64String(der);
        for (int i = 0; i < base64.Length; i += 64)
        {
            builder.Append(base64, i, Math.Min(64, base64.Length - i)).Append('\n');
        }

        builder.Append("-----END ").Append(label).Append("-----\n");
        return builder.ToString();
    }
}
