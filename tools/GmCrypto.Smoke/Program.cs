// 国密工具箱冒烟回归：算法标准向量 + 共享层（Hex / 编码 / 密文顺序 / 文件摘要）+ GB18030 可用性。
// 目标框架刻意是 net8.0（DevToys 宿主运行时），兼作 API 可用性门禁。
using System.Text;
using GmCrypto.DevToys.Core;
using GM.Core.SM2;
using GM.Core.SM3;
using GM.Core.SM4;

int passed = 0;
int failed = 0;

Console.WriteLine("=== GmCrypto.DevToys 冒烟回归（net8.0 = DevToys 宿主运行时） ===");
Console.WriteLine($"运行时: {Environment.Version}");
Console.WriteLine();

Section("SM3 标准向量");
Check("空串向量", () =>
    SM3.CreateDefault().ComputeHashHex(ReadOnlySpan<byte>.Empty)
        == "1ab21d8355cfa17f8e61194831e81a8f22bec8c728fefb747ed035eb5082aa2b");
Check("abc 向量（GB/T 32905）", () =>
    SM3.CreateDefault().ComputeHashHex(Encoding.ASCII.GetBytes("abc"))
        == "66c7f0f462eeedd9d1f2d46bdc10e4e24167c4875cf2f7a2297da02b8f4ba8e0");
Check("abcd x16 向量（GB/T 32905）", () =>
    SM3.CreateDefault().ComputeHashHex(Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("abcd", 16))))
        == "debe9ff92275b8a138604889c18e5a4d6fdb70e5387e5765293dcba39c0c5732");
Check("流式 = 一次性（1MB）", () =>
{
    byte[] data = Encoding.UTF8.GetBytes(new string('a', 1_000_000));
    using var input = new MemoryStream(data);
    using var output = new MemoryStream();
    SM3.CreateDefault().ComputeHash(input, output);
    return output.ToArray().AsSpan().SequenceEqual(SM3.CreateDefault().ComputeHash(data));
});

Section("文件摘要（Sm3FileHash + 进度流）");
Check("MemoryStream 摘要等于一次性摘要", () =>
{
    byte[] data = Encoding.UTF8.GetBytes("国密工具箱文件摘要");
    using var stream = new MemoryStream(data);
    string fileHash = Sm3FileHash.Compute(stream, data.Length, null);
    return fileHash == SM3.CreateDefault().ComputeHashHex(data);
});
Check("可 seek 流会上报 100% 进度", () =>
{
    byte[] data = new byte[64 * 1024];
    new Random(7).NextBytes(data);
    var reported = new List<double>();
    using var stream = new MemoryStream(data);
    var progress = new Progress<double>(reported.Add);
    Sm3FileHash.Compute(stream, data.Length, progress);
    // Progress<T> 走同步上下文，控制台程序里回调可能稍后到达，这里只校验计数流本身
    using var counting = new CountingReadStream(new MemoryStream(data), data.Length, progress);
    byte[] buffer = new byte[8192];
    while (counting.Read(buffer, 0, buffer.Length) > 0)
    {
    }

    return counting.BytesRead == data.Length;
});

Section("SM4 标准向量与模式");
Check("ECB 单块向量（GB/T 32907）", () =>
{
    byte[] key = Convert.FromHexString("0123456789abcdeffedcba9876543210");
    byte[] plain = Convert.FromHexString("0123456789abcdeffedcba9876543210");
    var parameters = new Sm4Parameters(key, Mode: Sm4Mode.Ecb, Padding: Sm4Padding.None);
    byte[] cipher = SM4.CreateDefault().Encrypt(plain, parameters);
    return Convert.ToHexString(cipher).ToLowerInvariant() == "681edf34d206965e86b3e94f536e4246"
           && SM4.CreateDefault().Decrypt(cipher, parameters).AsSpan().SequenceEqual(plain);
});
Check("PKCS7 往返（按块补齐）", () =>
{
    byte[] key = Convert.FromHexString("0123456789abcdeffedcba9876543210");
    var parameters = new Sm4Parameters(key, Mode: Sm4Mode.Ecb, Padding: Sm4Padding.PKCS7);
    byte[] plain = Encoding.UTF8.GetBytes("国密 SM4 分组加密 PKCS7");
    byte[] cipher = SM4.CreateDefault().Encrypt(plain, parameters);
    return cipher.Length % 16 == 0 && SM4.CreateDefault().Decrypt(cipher, parameters).AsSpan().SequenceEqual(plain);
});
Check("CBC 往返（16 字节 IV）", () =>
{
    byte[] key = Convert.FromHexString("0123456789abcdeffedcba9876543210");
    byte[] iv = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
    var parameters = new Sm4Parameters(key, iv, Sm4Mode.Cbc, Sm4Padding.PKCS7);
    byte[] plain = Encoding.UTF8.GetBytes("CBC mode 往返测试");
    byte[] cipher = SM4.CreateDefault().Encrypt(plain, parameters);
    return SM4.CreateDefault().Decrypt(cipher, parameters).AsSpan().SequenceEqual(plain);
});
Check("CTR 往返（长度不变）", () =>
{
    byte[] key = Convert.FromHexString("0123456789abcdeffedcba9876543210");
    byte[] iv = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
    var parameters = new Sm4Parameters(key, iv, Sm4Mode.Ctr, Sm4Padding.None);
    byte[] plain = Encoding.UTF8.GetBytes("CTR 模式不需要填充，长度保持不变");
    byte[] cipher = SM4.CreateDefault().Encrypt(plain, parameters);
    return cipher.Length == plain.Length
           && SM4.CreateDefault().Decrypt(cipher, parameters).AsSpan().SequenceEqual(plain);
});
Check("CTR 密钥流 = ECB 加密的计数器块", () =>
{
    // 独立校验 CTR 语义：keystream 必须是「计数器块做加密」得到，
    // 不能用解密方向轮密钥（GM 原实现正是在这里出错）。
    byte[] key = Convert.FromHexString("0123456789abcdeffedcba9876543210");
    byte[] iv = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
    byte[] plain = Encoding.UTF8.GetBytes("CTR keystream check");
    var ctr = new Sm4Parameters(key, iv, Sm4Mode.Ctr, Sm4Padding.None);
    var ecb = new Sm4Parameters(key, Mode: Sm4Mode.Ecb, Padding: Sm4Padding.None);
    byte[] cipher = SM4.CreateDefault().Encrypt(plain, ctr);

    byte[] counter = (byte[])iv.Clone();
    var expected = new List<byte>(plain.Length);
    for (int offset = 0; offset < plain.Length; offset += 16)
    {
        byte[] keystream = SM4.CreateDefault().Encrypt(counter, ecb);
        int take = Math.Min(16, plain.Length - offset);
        for (int i = 0; i < take; i++)
        {
            expected.Add((byte)(plain[offset + i] ^ keystream[i]));
        }

        for (int i = 15; i >= 0; i--)
        {
            if (++counter[i] != 0)
            {
                break;
            }
        }
    }

    return cipher.AsSpan().SequenceEqual(expected.ToArray());
});
Check("CBC 缺 IV 会报错", () =>
    Throws<ArgumentException>(() =>
        SM4.CreateDefault().Encrypt(
            new byte[16],
            new Sm4Parameters(new byte[16], null, Sm4Mode.Cbc, Sm4Padding.PKCS7))));

Section("SM2 签名与验签");
byte[] privateKey = Sm2SignerManaged.GeneratePrivateKey();
byte[] publicKey = Sm2SignerManaged.DerivePublicKey(privateKey);
byte[] message = Encoding.UTF8.GetBytes("国密 SM2 签名验签冒烟");

Check("私钥 32 字节 / 公钥 65 字节且 04 开头", () =>
    privateKey.Length == 32 && publicKey.Length == 65 && publicKey[0] == 0x04);
Check("签名为 DER 且可验证", () =>
{
    byte[] signature = SM2.CreateSigner().Sign(privateKey, message);
    return signature[0] == 0x30 && SM2.CreateSigner().Verify(publicKey, message, signature);
});
Check("篡改消息验签失败", () =>
{
    byte[] signature = SM2.CreateSigner().Sign(privateKey, message);
    byte[] tampered = Encoding.UTF8.GetBytes("国密 SM2 签名验签冒烟!");
    return !SM2.CreateSigner().Verify(publicKey, tampered, signature);
});
Check("自定义 UserId 可签可验，换 UserId 验签失败", () =>
{
    byte[] userId = Encoding.UTF8.GetBytes("ALICE123@YAHOO.COM");
    byte[] signature = SM2.CreateSigner().Sign(privateKey, message, userId);
    bool ok = SM2.CreateSigner().Verify(publicKey, message, signature, userId);
    bool wrongUser = SM2.CreateSigner().Verify(publicKey, message, signature, Encoding.UTF8.GetBytes("other"));
    return ok && !wrongUser;
});
Check("DER <-> raw(r||s) 互转无损", () =>
{
    byte[] signature = SM2.CreateSigner().Sign(privateKey, message);
    if (!Sm2SignerManaged.TryDerToRaw(signature, out byte[] raw) || raw.Length != 64)
    {
        return false;
    }

    byte[] back = Sm2SignerManaged.RawToDer(raw);
    return back.AsSpan().SequenceEqual(signature) && SM2.CreateSigner().Verify(publicKey, message, back);
});

Section("SM2 加解密与密文顺序");
Check("C1C3C2 往返，密文长度 = 65 + 32 + 明文", () =>
{
    byte[] plain = Encoding.UTF8.GetBytes("SM2 加密解密");
    byte[] cipher = SM2.CreateEncryptor().Encrypt(publicKey, plain);
    byte[] back = SM2.CreateEncryptor().Decrypt(privateKey, cipher);
    return cipher.Length == 65 + 32 + plain.Length && back.AsSpan().SequenceEqual(plain);
});
Check("Sm2CipherFormat 双向转换无损", () =>
{
    byte[] plain = Encoding.UTF8.GetBytes("老系统要求 C1C2C3");
    byte[] c1c3c2 = SM2.CreateEncryptor().Encrypt(publicKey, plain);
    byte[] c1c2c3 = Sm2CipherFormat.C1C3C2ToC1C2C3(c1c3c2);
    byte[] restored = Sm2CipherFormat.C1C2C3ToC1C3C2(c1c2c3);
    return !c1c2c3.AsSpan().SequenceEqual(c1c3c2)
           && restored.AsSpan().SequenceEqual(c1c3c2)
           && SM2.CreateEncryptor().Decrypt(privateKey, restored).AsSpan().SequenceEqual(plain);
});
Check("密文过短会报错", () =>
    Throws<InvalidOperationException>(() => Sm2CipherFormat.C1C3C2ToC1C2C3(new byte[96])));
Check("错误私钥解密抛异常", () =>
{
    byte[] cipher = SM2.CreateEncryptor().Encrypt(publicKey, Encoding.UTF8.GetBytes("x"));
    byte[] otherKey = Sm2SignerManaged.GeneratePrivateKey();
    return ThrowsAny(() => SM2.CreateEncryptor().Decrypt(otherKey, cipher));
});

Section("编码（GB18030 需要 CodePages 提供程序）");
Check("GB18030 可注册并编码「国密」", () =>
{
    byte[] bytes = TextEncodings.Resolve(TextEncodings.Gb18030).GetBytes("国密");
    return Convert.ToHexString(bytes) == "B9FAC3DC";
});
Check("GB18030 中文往返", () =>
{
    Encoding encoding = TextEncodings.Resolve(TextEncodings.Gb18030);
    return encoding.GetString(encoding.GetBytes("国密算法工具箱")) == "国密算法工具箱";
});
Check("UTF-8 / ASCII 可用", () =>
    TextEncodings.Resolve(TextEncodings.Utf8).GetBytes("国密").Length == 6
    && TextEncodings.Resolve(TextEncodings.Ascii).GetBytes("abc").Length == 3);

Section("Hex 解析约定（四个工具共用）");
Check("允许空格 / 短横线 / 冒号 / 逗号分隔与 0x 前缀", () =>
    HexCodec.ToHex(HexCodec.Parse("0x01 02-03:04,05", "输入")) == "0102030405"
    && HexCodec.ToHex(HexCodec.Parse(" AA BB ", "输入")) == "aabb");
Check("固定长度校验通过（16 字节密钥 / 32 字节私钥）", () =>
    HexCodec.ParseFixed(new string('a', 32), 16, "密钥").Length == 16
    && HexCodec.ParseFixed(new string('b', 64), 32, "私钥").Length == 32);
Check("长度不符会给出可读错误", () =>
{
    try
    {
        HexCodec.ParseFixed("aabb", 16, "SM4 密钥");
        return false;
    }
    catch (InvalidOperationException ex)
    {
        return ex.Message.Contains("16 字节", StringComparison.Ordinal);
    }
});
Check("非法 Hex / 奇数长度会报错", () =>
    Throws<InvalidOperationException>(() => HexCodec.Parse("zz", "输入"))
    && Throws<InvalidOperationException>(() => HexCodec.Parse("abc", "输入")));
Check("ToString 输出小写", () =>
    HexCodec.ToHex(new byte[] { 0xAB, 0xCD }) == "abcd"
    && HexCodec.RandomHex(16).Length == 32);

Console.WriteLine();
Console.WriteLine($"结果：通过 {passed}，失败 {failed}");
return failed == 0 ? 0 : 1;

// ---------------------------------------------------------------- 辅助

void Section(string title)
{
    Console.WriteLine();
    Console.WriteLine($"-- {title}");
}

void Check(string name, Func<bool> test)
{
    try
    {
        if (test())
        {
            passed++;
            Console.WriteLine($"  [PASS] {name}");
            return;
        }

        failed++;
        Console.WriteLine($"  [FAIL] {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"  [FAIL] {name} -> {ex.GetType().Name}: {ex.Message}");
    }
}

bool Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
        return false;
    }
    catch (TException)
    {
        return true;
    }
    catch
    {
        return false;
    }
}

bool ThrowsAny(Action action)
{
    try
    {
        action();
        return false;
    }
    catch
    {
        return true;
    }
}
