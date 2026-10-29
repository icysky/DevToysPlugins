using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.X509;
using BcX509Certificate = Org.BouncyCastle.X509.X509Certificate;

namespace TestCertGenerator.CertGen;

/// <summary>
/// 证书文件查看与 Windows 证书对象加载。
/// 查看统一走 BouncyCastle（RSA 与 SM2 都能解析，且能显示 Windows 不认识的国密算法）。
/// </summary>
public static class CertificateInspector
{
    private static readonly Dictionary<string, string> EkuNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1.3.6.1.5.5.7.3.1"] = "Server Authentication",
        ["1.3.6.1.5.5.7.3.2"] = "Client Authentication",
        ["1.3.6.1.5.5.7.3.3"] = "Code Signing",
        ["1.3.6.1.5.5.7.3.4"] = "Email Protection",
        ["1.3.6.1.5.5.7.3.8"] = "Time Stamping",
        ["1.3.6.1.5.5.7.3.9"] = "OCSP Signing",
        ["1.3.6.1.4.1.311.10.3.12"] = "Document Signing",
        ["2.5.29.37.0"] = "Any Purpose",
    };

    private static readonly Dictionary<string, string> EkuChineseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1.3.6.1.5.5.7.3.1"] = "服务器身份验证",
        ["1.3.6.1.5.5.7.3.2"] = "客户端身份验证",
        ["1.3.6.1.5.5.7.3.3"] = "代码签名",
        ["1.3.6.1.5.5.7.3.4"] = "邮件保护",
        ["1.3.6.1.5.5.7.3.8"] = "时间戳",
        ["1.3.6.1.5.5.7.3.9"] = "OCSP 签名",
        ["1.3.6.1.4.1.311.10.3.12"] = "文档签名",
        ["2.5.29.37.0"] = "任意用途",
    };

    private static readonly Dictionary<string, string> SignatureAlgorithmNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1.2.840.113549.1.1.5"] = "SHA1withRSA",
        ["1.2.840.113549.1.1.11"] = "SHA256withRSA",
        ["1.2.840.113549.1.1.12"] = "SHA384withRSA",
        ["1.2.840.113549.1.1.13"] = "SHA512withRSA",
        ["1.2.156.10197.1.501"] = "SM3withSM2",
    };

    private static readonly (string English, string Chinese)[] KeyUsageNames =
    [
        ("Digital Signature", "数字签名"),
        ("Non Repudiation", "不可否认"),
        ("Key Encipherment", "密钥加密"),
        ("Data Encipherment", "数据加密"),
        ("Key Agreement", "密钥协商"),
        ("Key Cert Sign", "证书签名"),
        ("CRL Sign", "CRL 签名"),
        ("Encipher Only", "仅加密"),
        ("Decipher Only", "仅解密"),
    ];

    /// <summary>生成证书文件的可读描述（主题、颁发者、算法、用途扩展等）。</summary>
    public static string Describe(string filePath, string? password)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"文件: {filePath}");

        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        switch (extension)
        {
            case ".pfx":
            case ".p12":
                DescribePkcs12(builder, filePath, password ?? string.Empty);
                break;
            case ".pem":
                DescribePem(builder, filePath);
                break;
            default:
                DescribeDer(builder, filePath);
                break;
        }

        return builder.ToString();
    }

    /// <summary>加载 Windows 可用的证书对象（SM2 证书不支持，需调用方处理异常）。</summary>
    public static X509Certificate2 LoadWindowsCertificate(string filePath, string? password)
    {
#if NET9_0_OR_GREATER
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".pfx" or ".p12" => X509CertificateLoader.LoadPkcs12FromFile(
                filePath,
                password ?? string.Empty,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable),
            ".pem" => X509Certificate2.CreateFromPem(File.ReadAllText(filePath)),
            _ => X509CertificateLoader.LoadCertificateFromFile(filePath)
        };
#else
#pragma warning disable SYSLIB0057 // net8.0 没有 X509CertificateLoader
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".pfx" or ".p12" => new X509Certificate2(
                filePath,
                password ?? string.Empty,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable),
            ".pem" => X509Certificate2.CreateFromPem(File.ReadAllText(filePath)),
            _ => new X509Certificate2(filePath)
        };
#pragma warning restore SYSLIB0057
#endif
    }

    private static void DescribePkcs12(StringBuilder builder, string filePath, string password)
    {
        builder.AppendLine("格式: PKCS#12 (PFX/P12)");

        var store = new Pkcs12StoreBuilder().Build();
        using (var input = File.OpenRead(filePath))
        {
            store.Load(input, password.ToCharArray());
        }

        int index = 0;
        foreach (string alias in store.Aliases)
        {
            bool hasKey = store.IsKeyEntry(alias);
            builder.AppendLine(hasKey ? $"别名: {alias}（含私钥）" : $"别名: {alias}（仅证书）");

            if (hasKey)
            {
                builder.AppendLine($"私钥算法: {DescribePrivateKey(store.GetKey(alias).Key)}");
            }

            X509CertificateEntry[]? chain = store.GetCertificateChain(alias);
            if (chain is null)
            {
                continue;
            }

            foreach (X509CertificateEntry entry in chain)
            {
                AppendCertificate(builder, entry.Certificate, index++, chain.Length > 1);
            }
        }

        if (index == 0)
        {
            builder.AppendLine("(未找到证书)");
        }
    }

    private static void DescribePem(StringBuilder builder, string filePath)
    {
        builder.AppendLine("格式: PEM");

        using var reader = new StreamReader(filePath);
        var pemReader = new PemReader(reader);
        int index = 0;

        object? item;
        while ((item = pemReader.ReadObject()) is not null)
        {
            switch (item)
            {
                case BcX509Certificate cert:
                    AppendCertificate(builder, cert, index++, false);
                    break;
                case AsymmetricCipherKeyPair keyPair:
                    builder.AppendLine($"私钥: {DescribePrivateKey(keyPair.Private)}");
                    break;
                case PrivateKeyInfo privateKeyInfo:
                    builder.AppendLine($"私钥: PKCS#8 {privateKeyInfo.PrivateKeyAlgorithm.Algorithm}");
                    break;
                case AsymmetricKeyParameter keyParameter:
                    builder.AppendLine($"私钥: {DescribePrivateKey(keyParameter)}");
                    break;
            }
        }

        if (index == 0)
        {
            builder.AppendLine("(未找到证书)");
        }
    }

    private static void DescribeDer(StringBuilder builder, string filePath)
    {
        builder.AppendLine("格式: DER / 二进制证书");

        using var input = File.OpenRead(filePath);
        var certificates = new X509CertificateParser().ReadCertificates(input);
        if (certificates.Count == 0)
        {
            builder.AppendLine("(未找到证书)");
            return;
        }

        int index = 0;
        foreach (BcX509Certificate cert in certificates)
        {
            AppendCertificate(builder, cert, index++, certificates.Count > 1);
        }
    }

    private static void AppendCertificate(StringBuilder builder, BcX509Certificate cert, int index, bool chain)
    {
        builder.AppendLine();
        builder.AppendLine(chain ? $"[证书 {index}]" : "[证书]");
        builder.AppendLine($"主题: {FormatName(cert.SubjectDN)}");
        builder.AppendLine($"颁发者: {FormatName(cert.IssuerDN)}");
        builder.AppendLine($"有效期: {cert.NotBefore:yyyy-MM-dd HH:mm:ss} ~ {cert.NotAfter:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"序列号: {cert.SerialNumber.ToString(16).ToUpperInvariant()}");
        builder.AppendLine($"指纹 (SHA-1): {ComputeThumbprint(cert)}");
        string signatureName = SignatureAlgorithmNames.TryGetValue(cert.SigAlgOid, out string? name)
            ? name
            : cert.SigAlgName;
        builder.AppendLine($"签名算法: {signatureName} ({cert.SigAlgOid})");
        builder.AppendLine($"公钥算法: {DescribePublicKey(cert.GetPublicKey())}");

        int constraints = cert.GetBasicConstraints();
        builder.AppendLine(constraints >= 0
            ? $"是否 CA: 是{(constraints == int.MaxValue ? string.Empty : $"（路径长度 {constraints}）")}"
            : "是否 CA: 否");

        builder.AppendLine($"KU: {DescribeKeyUsage(cert)}");
        builder.AppendLine($"EKU: {DescribeExtendedKeyUsage(cert)}");

        string? san = DescribeSubjectAlternativeNames(cert);
        if (!string.IsNullOrEmpty(san))
        {
            builder.AppendLine($"SAN: {san}");
        }
    }

    private static string DescribePrivateKey(AsymmetricKeyParameter key)
    {
        return key switch
        {
            ECPrivateKeyParameters ec => $"EC / SM2（{DescribeCurve(ec.Parameters)}）",
            RsaPrivateCrtKeyParameters rsa => $"RSA {rsa.Modulus.BitLength} 位",
            RsaKeyParameters rsa => $"RSA {rsa.Modulus.BitLength} 位",
            _ => key.GetType().Name
        };
    }

    private static string DescribePublicKey(AsymmetricKeyParameter key)
    {
        return key switch
        {
            ECPublicKeyParameters ec => $"EC / SM2（{DescribeCurve(ec.Parameters)}）",
            RsaKeyParameters rsa => $"RSA（{rsa.Modulus.BitLength} 位）",
            _ => key.GetType().Name
        };
    }

    private static string DescribeCurve(ECDomainParameters domain)
    {
        return domain is ECNamedDomainParameters named
            ? DescribeCurve(named.Name)
            : "显式曲线参数";
    }

    private static string DescribeCurve(Org.BouncyCastle.Asn1.DerObjectIdentifier curveOid)
    {
        return curveOid.Id == "1.2.156.10197.1.301"
            ? $"sm2p256v1 ({curveOid.Id})"
            : curveOid.Id;
    }

    private static string DescribeKeyUsage(BcX509Certificate cert)
    {
        bool[]? usage = cert.GetKeyUsage();
        if (usage is null)
        {
            return "(无)";
        }

        var parts = new List<string>();
        for (int i = 0; i < usage.Length && i < KeyUsageNames.Length; i++)
        {
            if (usage[i])
            {
                parts.Add($"{KeyUsageNames[i].English}（{KeyUsageNames[i].Chinese}）");
            }
        }

        return parts.Count == 0 ? "(none)" : string.Join(", ", parts);
    }

    private static string DescribeExtendedKeyUsage(BcX509Certificate cert)
    {
        var usages = cert.GetExtendedKeyUsage();
        if (usages is null || usages.Count == 0)
        {
            return "(无)";
        }

        return string.Join(", ", usages.Select(oid =>
        {
            if (!EkuNames.TryGetValue(oid.Id, out string? name))
            {
                return oid.Id;
            }

            return EkuChineseNames.TryGetValue(oid.Id, out string? chinese)
                ? $"{oid.Id} ({name})（{chinese}）"
                : $"{oid.Id} ({name})";
        }));
    }

    private static string? DescribeSubjectAlternativeNames(BcX509Certificate cert)
    {
        var entries = cert.GetSubjectAlternativeNames();
        if (entries is null || entries.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (IList<object> entry in entries)
        {
            int tag = Convert.ToInt32(entry[0], CultureInfo.InvariantCulture);
            string value = entry.Count > 1 ? FormatGeneralNameValue(tag, entry[1]) : string.Empty;
            string label = tag switch
            {
                GeneralName.Rfc822Name => "email",
                GeneralName.DnsName => "DNS",
                GeneralName.UniformResourceIdentifier => "URI",
                GeneralName.IPAddress => "IP",
                GeneralName.DirectoryName => "DirName",
                GeneralName.RegisteredID => "OID",
                _ => $"#{tag}"
            };

            parts.Add($"{label}:{value}");
        }

        return string.Join(", ", parts);
    }

    private static string FormatGeneralNameValue(int tag, object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            byte[] bytes when tag == GeneralName.IPAddress => new IPAddress(bytes).ToString(),
            byte[] bytes => Convert.ToHexString(bytes),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string FormatName(X509Name name)
    {
        var oids = name.GetOidList();
        var values = name.GetValueList();
        var parts = new List<string>();

        // DER 中 RDN 由通用到具体，展示时反转为 CN 在前的习惯写法。
        for (int i = oids.Count - 1; i >= 0; i--)
        {
            string label = oids[i].Id switch
            {
                "2.5.4.3" => "CN",
                "2.5.4.10" => "O",
                "2.5.4.11" => "OU",
                "2.5.4.6" => "C",
                "2.5.4.8" => "ST",
                "2.5.4.7" => "L",
                "1.2.840.113549.1.9.1" => "E",
                _ => oids[i].Id
            };

            string value = i < values.Count ? values[i] : string.Empty;
            parts.Add($"{label}={EscapeDnValue(value)}");
        }

        return string.Join(", ", parts);
    }

    private static string EscapeDnValue(string value)
    {
        return value.Replace("\\", "\\\\").Replace(",", "\\,");
    }

    private static string ComputeThumbprint(BcX509Certificate cert)
    {
        byte[] hash = SHA1.HashData(cert.GetEncoded());
        return Convert.ToHexString(hash);
    }
}
