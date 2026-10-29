using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TestCertGenerator.CertGen;

/// <summary>
/// RSA 证书生成（保持原有行为：SHA-256 + PKCS#1、16 字节随机序列号、
/// CA 提前 1 天生效且比终端证书多 3 年、扩展顺序与关键性不变）。
/// </summary>
internal static class RsaCertificateGenerator
{
    public static GeneratedCertificate Generate(CertificateSpec spec)
    {
        using RSA issuerRsa = RSA.Create(spec.RsaKeySize);
        using RSA subjectRsa = RSA.Create(spec.RsaKeySize);

        using X509Certificate2 issuerCert = CreateIssuerCertificate(spec, issuerRsa);
        X509Certificate2 cert = CreateIssuedEndEntityCertificate(spec, issuerCert, subjectRsa);

        try
        {
            byte[]? pfxBytes = null;
            if (spec.CreatePfx)
            {
                pfxBytes = cert.Export(X509ContentType.Pfx, spec.PfxPassword);
            }

            return new GeneratedCertificate(
                certificateDer: cert.Export(X509ContentType.Cert),
                issuerCertificateDer: issuerCert.Export(X509ContentType.Cert),
                pfxBytes: pfxBytes,
                certificatePem: cert.ExportCertificatePem(),
                privateKeyPem: subjectRsa.ExportPkcs8PrivateKeyPem(),
                keyDescription: CertificateAlgorithmInfo.GetKeyDescription(CertificateAlgorithm.Rsa, spec.RsaKeySize),
                signatureAlgorithm: CertificateAlgorithmInfo.GetSignatureAlgorithm(CertificateAlgorithm.Rsa),
                windowsCertificate: cert);
        }
        catch
        {
            cert.Dispose();
            throw;
        }
    }

    private static X509Certificate2 CreateIssuerCertificate(
        CertificateSpec spec,
        RSA issuerRsa)
    {
        var issuerRequest = new CertificateRequest(
            new X500DistinguishedName(spec.IssuerDn),
            issuerRsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        issuerRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        issuerRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        issuerRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(issuerRequest.PublicKey, false));

        return issuerRequest.CreateSelfSigned(spec.NotBefore.AddDays(-1), spec.NotAfter.AddYears(3));
    }

    private static X509Certificate2 CreateIssuedEndEntityCertificate(
        CertificateSpec spec,
        X509Certificate2 issuerCert,
        RSA subjectRsa)
    {
        var request = new CertificateRequest(
            new X500DistinguishedName(spec.SubjectDn),
            subjectRsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        request.CertificateExtensions.Add(new X509KeyUsageExtension(spec.KeyUsage, spec.KeyUsageCritical));

        var eku = new OidCollection();
        foreach (string oidValue in spec.ExtendedKeyUsageOids)
        {
            eku.Add(new Oid(oidValue));
        }

        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, spec.ExtendedKeyUsageCritical));

        if (spec.IncludeSubjectAlternativeName)
        {
            var sanBuilder = new SubjectAlternativeNameBuilder();
            foreach (string entry in spec.SubjectAlternativeNames)
            {
                if (IPAddress.TryParse(entry, out IPAddress? ipAddress))
                {
                    sanBuilder.AddIpAddress(ipAddress);
                }
                else
                {
                    sanBuilder.AddDnsName(entry);
                }
            }

            request.CertificateExtensions.Add(sanBuilder.Build());
        }

        byte[] serial = new byte[16];
        RandomNumberGenerator.Fill(serial);
        X509Certificate2 issued = request.Create(issuerCert, spec.NotBefore, spec.NotAfter, serial);
        try
        {
            return issued.CopyWithPrivateKey(subjectRsa);
        }
        finally
        {
            issued.Dispose();
        }
    }
}
