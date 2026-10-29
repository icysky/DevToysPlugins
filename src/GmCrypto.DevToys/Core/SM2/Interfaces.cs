namespace GM.Core.SM2;

public interface ISM2Signer
{
    byte[] Sign(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> userId = default);
    bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> userId = default);
}

public interface ISM2Encryptor
{
    byte[] Encrypt(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> plaintext);
    byte[] Decrypt(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> ciphertext);
}

public static class SM2
{
    public static ISM2Signer CreateSigner() => new Sm2SignerManaged();
    public static ISM2Encryptor CreateEncryptor() => new Sm2EncryptorManaged();
}
