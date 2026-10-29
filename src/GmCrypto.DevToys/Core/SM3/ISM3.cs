namespace GM.Core.SM3;

public interface ISM3
{
    byte[] ComputeHash(ReadOnlySpan<byte> data);
    string ComputeHashHex(ReadOnlySpan<byte> data);
    void ComputeHash(Stream input, Stream output);
}

public static class SM3
{
    public static ISM3 CreateDefault() => new SM3Managed();
}
