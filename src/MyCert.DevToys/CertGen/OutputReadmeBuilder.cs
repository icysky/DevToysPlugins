using System;
using System.Text;

namespace TestCertGenerator.CertGen;

/// <summary>
/// 生成导出目录下的 ReadMe_&lt;基名&gt;.txt：记录生成参数、每个文件的用途和 PFX 密码。
/// WPF 版与 DevToys 插件共用，避免两套说明文字不一致。
/// </summary>
public static class OutputReadmeBuilder
{
    public static string Build(
        string subject,
        string baseName,
        string keyDescription,
        string signatureAlgorithm,
        string pfxPassword,
        bool exportedPfx,
        bool exportedCer,
        bool exportedPem,
        bool exportedIssuerCa,
        DateTime generatedAt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("MyCert 测试证书 - 生成结果与文件说明");
        builder.AppendLine("========================================");
        builder.AppendLine();
        builder.AppendLine($"生成时间: {generatedAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"证书主题: {subject}");
        builder.AppendLine($"证书基名: {baseName}");
        builder.AppendLine($"密钥算法: {keyDescription}");
        builder.AppendLine($"签名算法: {signatureAlgorithm}");
        builder.AppendLine();

        builder.AppendLine("生成的文件");
        builder.AppendLine("----------------------------------------");

        if (exportedPfx)
        {
            AppendFile(
                builder,
                baseName + ".pfx",
                "PKCS#12 容器，包含终端证书和私钥（受 PFX 密码保护）。用于导入签名工具、IIS、.NET 等。");
        }

        if (exportedCer)
        {
            AppendFile(
                builder,
                baseName + ".cer",
                "DER 编码的终端证书（只含公钥）。用于验证签名或交付给对方。");
        }

        if (exportedPem)
        {
            AppendFile(
                builder,
                baseName + ".pem",
                "PEM 编码的终端证书（只含公钥）。用于 OpenSSL、Java、国密工具等。");
            AppendFile(
                builder,
                baseName + ".key",
                "PEM 编码的 PKCS#8 私钥（无密码保护）。用于 OpenSSL、Java、国密工具，务必保密。");
        }

        if (exportedIssuerCa)
        {
            AppendFile(
                builder,
                baseName + "_issuer_ca.cer",
                "颁发者 CA 证书（只含公钥）。用于构建/验证证书链，导入受信任根时选这个文件。");
        }

        string readmeFileName = $"ReadMe_{baseName}.txt";
        AppendFile(
            builder,
            readmeFileName,
            "本说明文件，记录生成参数、各文件用途和 PFX 密码。");

        builder.AppendLine("PFX 密码");
        builder.AppendLine("----------------------------------------");
        builder.AppendLine(exportedPfx && !string.IsNullOrEmpty(pfxPassword)
            ? pfxPassword
            : "本次未导出 PFX，无密码。");
        builder.AppendLine();

        builder.AppendLine("安全提示");
        builder.AppendLine("----------------------------------------");
        builder.AppendLine("- .pfx 和 .key 包含私钥，泄露后他人可冒用该证书签名。");
        builder.AppendLine($"- 本文件（{readmeFileName}）包含 PFX 密码，请与证书一起妥善保管，不要上传到公共仓库或聊天工具。");
        builder.AppendLine("- 仅用于测试环境；正式电子签章请使用具备资质的 CA 签发的证书。");

        if (signatureAlgorithm.Contains("SM2", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("- SM2 证书无法导入 Windows 证书存储；请在 OFD 签名/签章工具中直接使用 PFX 或 PEM + KEY。");
        }

        return builder.ToString();
    }

    private static void AppendFile(StringBuilder builder, string fileName, string description)
    {
        builder.AppendLine($"- {fileName}");
        builder.AppendLine($"  {description}");
        builder.AppendLine();
    }
}
