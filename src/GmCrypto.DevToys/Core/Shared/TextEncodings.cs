using System.Text;

namespace GmCrypto.DevToys.Core;

/// <summary>
/// 文本编码选择（UTF-8 / GB18030 / ASCII）。
/// GB18030 需要注册 CodePagesEncodingProvider —— 它在 .NET 共享框架里
/// （System.Text.Encoding.CodePages.dll），net8.0 目标下可直接引用，无需 NuGet 包。
/// </summary>
public static class TextEncodings
{
    public const string Utf8 = "utf8";

    public const string Gb18030 = "gb18030";

    public const string Ascii = "ascii";

    private static bool _codePagesRegistered;

    public static Encoding Resolve(string id)
    {
        return id switch
        {
            Gb18030 => GetGb18030(),
            Ascii => Encoding.ASCII,
            _ => Encoding.UTF8
        };
    }

    private static Encoding GetGb18030()
    {
        EnsureCodePagesProvider();
        try
        {
            return Encoding.GetEncoding("GB18030");
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("当前运行环境不支持 GB18030 编码，请改用 UTF-8。");
        }
    }

    private static void EnsureCodePagesProvider()
    {
        if (_codePagesRegistered)
        {
            return;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _codePagesRegistered = true;
    }
}
