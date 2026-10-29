namespace GM.Core.SM4;

public enum Sm4Mode { Ecb, Cbc, Ctr }
public enum Sm4Padding { None, PKCS7, Zeros }
public sealed record Sm4Parameters(
    byte[] Key,
    byte[]? IV = null,
    Sm4Mode Mode = Sm4Mode.Ecb,
    Sm4Padding Padding = Sm4Padding.PKCS7
);

public interface ISM4Cipher
{
    byte[] Encrypt(ReadOnlySpan<byte> plaintext, in Sm4Parameters parameters);
    byte[] Decrypt(ReadOnlySpan<byte> ciphertext, in Sm4Parameters parameters);
}

public static class SM4
{
    public static ISM4Cipher CreateDefault() => new SM4Managed();
}
