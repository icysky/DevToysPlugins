using GM.Core.SM3;

namespace GmCrypto.DevToys.Core;

/// <summary>文件 SM3：流式计算 + 可选进度上报（不要求流可 seek / 可知长度）。</summary>
public static class Sm3FileHash
{
    public static string Compute(Stream stream, long? totalLength, IProgress<double>? progress)
    {
        using var output = new MemoryStream(32);
        using var counting = new CountingReadStream(stream, totalLength, progress);
        SM3.CreateDefault().ComputeHash(counting, output);
        return HexCodec.ToHex(output.ToArray());
    }
}
