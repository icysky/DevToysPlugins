using System.Numerics;
using System.Security.Cryptography;
using System.Collections.Generic;

namespace GM.Core.SM2;

public sealed class Sm2SignerManaged : ISM2Signer
{
    private static readonly BigInteger p = BigIntFromHex("FFFFFFFEFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00000000FFFFFFFFFFFFFFFF");
    private static readonly BigInteger a = BigIntFromHex("FFFFFFFEFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00000000FFFFFFFFFFFFFFFC");
    private static readonly BigInteger b = BigIntFromHex("28E9FA9E9D9F5E344D5A9E4BCF6509A7F39789F515AB8F92DDBCBD414D940E93");
    private static readonly BigInteger n = BigIntFromHex("FFFFFFFEFFFFFFFFFFFFFFFFFFFFFFFF7203DF6B21C6052B53BBF40939D54123");
    private static readonly BigInteger gx = BigIntFromHex("32C4AE2C1F1981195F9904466A39C9948FE30BBFF2660BE1715A4589334C74C7");
    private static readonly BigInteger gy = BigIntFromHex("BC3736A2F4F6779C59BDCEE36B692153D0A9877CC62A474002DF32E52139F0A0");

    private static readonly byte[] DefaultUserId = System.Text.Encoding.ASCII.GetBytes("1234567812345678");

    private readonly Point G = new Point(gx, gy, false);

    public byte[] Sign(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> userId = default)
    {
        if (privateKey.Length != 32) throw new ArgumentException("私钥应为32字节", nameof(privateKey));
        var d = ToBigInt(privateKey, isModN: true);
        if (d <= 0 || d >= n) throw new ArgumentException("私钥不在有效范围", nameof(privateKey));

        var P = ScalarMult(G, d);
        var ZA = ComputeZA(userId, P);
        var e = new BigInteger(SM3.SM3.CreateDefault().ComputeHash(Concat(ZA, message)), isUnsigned: true, isBigEndian: true);

        using var rng = RandomNumberGenerator.Create();
        while (true)
        {
            var k = RandomK(rng);
            var kG = ScalarMult(G, k);
            var r = ModN(e + kG.X);
            if (r.IsZero || r + k == n) continue;
            var rd = ModN(r * d);
            var onePlusDInv = ModInverseN(BigInteger.One + d);
            var s = ModN(onePlusDInv * (k - rd));
            if (s.IsZero) continue;
            return EncodeDerSignature(r, s);
        }
    }

    public bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> userId = default)
    {
        if (publicKey.Length != 65 || publicKey[0] != 0x04) throw new ArgumentException("公钥需为未压缩格式 65字节", nameof(publicKey));
        var Px = ToBigInt(publicKey.Slice(1, 32), isModN: false);
        var Py = ToBigInt(publicKey.Slice(33, 32), isModN: false);
        var PA = new Point(Px, Py, false);
        if (!IsOnCurve(PA)) return false;

        if (!TryDecodeDerSignature(signature, out var r, out var s)) return false;
        if (r <= 0 || r >= n || s <= 0 || s >= n) return false;

        var ZA = ComputeZA(userId, PA);
        var e = new BigInteger(SM3.SM3.CreateDefault().ComputeHash(Concat(ZA, message)), isUnsigned: true, isBigEndian: true);
        var t = ModN(r + s);
        if (t.IsZero) return false;
        var sG = ScalarMult(G, s);
        var tPA = ScalarMult(PA, t);
        var P = PointAdd(sG, tPA);
        if (P.IsInfinity) return false;
        var R = ModN(e + P.X);
        return R == r;
    }

    private static BigInteger RandomK(RandomNumberGenerator rng)
    {
        Span<byte> buf = stackalloc byte[32];
        while (true)
        {
            rng.GetBytes(buf);
            var k = new BigInteger(buf, isUnsigned: true, isBigEndian: true);
            k = ModN(k);
            if (k > 0 && k < n) return k;
        }
    }

    private static byte[] ComputeENTLABytes(int idLen)
    {
        ushort entla = (ushort)(idLen * 8);
        return new byte[] { (byte)(entla >> 8), (byte)(entla & 0xFF) };
    }

    private static byte[] ComputeZA(ReadOnlySpan<byte> userId, Point P)
    {
        var uid = userId.IsEmpty ? DefaultUserId : userId.ToArray();
        var sm3 = SM3.SM3.CreateDefault();
        var entla = ComputeENTLABytes(uid.Length);
        var data = new List<byte>(2 + uid.Length + 32 * 6);
        data.AddRange(entla);
        data.AddRange(uid);
        data.AddRange(ToFixed32Bytes(a));
        data.AddRange(ToFixed32Bytes(b));
        data.AddRange(ToFixed32Bytes(gx));
        data.AddRange(ToFixed32Bytes(gy));
        data.AddRange(ToFixed32Bytes(P.X));
        data.AddRange(ToFixed32Bytes(P.Y));
        return sm3.ComputeHash(CollectionsMarshalFree(data));
    }

    private static ReadOnlySpan<byte> CollectionsMarshalFree(List<byte> data)
    {
        // 避免依赖 CollectionsMarshal，直接复制到新数组
        return data.ToArray();
    }
    private static byte[] Concat(byte[] a, ReadOnlySpan<byte> b)
    {
        var res = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, res, 0, a.Length);
        b.CopyTo(res.AsSpan(a.Length));
        return res;
    }

    private readonly struct Point
    {
        public readonly BigInteger X;
        public readonly BigInteger Y;
        public readonly bool IsInfinity;
        public Point(BigInteger x, BigInteger y, bool inf) { X = x; Y = y; IsInfinity = inf; }
    }

    private static bool IsOnCurve(in Point P)
    {
        if (P.IsInfinity) return true;
        var left = ModP(P.Y * P.Y);
        var right = ModP(P.X * P.X * P.X + a * P.X + b);
        return left == right;
    }

    private static Point PointDouble(in Point P)
    {
        if (P.IsInfinity) return P;
        if (P.Y.IsZero) return new Point(0, 0, true);
        var s = ModP((3 * P.X * P.X + a) * ModInverseP(2 * P.Y));
        var xr = ModP(s * s - 2 * P.X);
        var yr = ModP(s * (P.X - xr) - P.Y);
        return new Point(xr, yr, false);
    }

    private static Point PointAdd(in Point P, in Point Q)
    {
        if (P.IsInfinity) return Q;
        if (Q.IsInfinity) return P;
        if (P.X == Q.X)
        {
            if (ModP(P.Y + Q.Y).IsZero) return new Point(0, 0, true);
            return PointDouble(P);
        }
        var s = ModP((Q.Y - P.Y) * ModInverseP(Q.X - P.X));
        var xr = ModP(s * s - P.X - Q.X);
        var yr = ModP(s * (P.X - xr) - P.Y);
        return new Point(xr, yr, false);
    }

    private static Point ScalarMult(in Point P, BigInteger k)
    {
        Point R = new Point(0, 0, true);
        Point add = P;
        var bits = GetBitLength(k);
        for (int i = 0; i < bits; i++)
        {
            if (!k.IsZero && ((k >> i) & 1) == 1)
            {
                R = PointAdd(R, add);
            }
            add = PointDouble(add);
        }
        return R;
    }

    private static int GetBitLength(BigInteger v)
    {
        if (v.IsZero) return 0;
        var bytes = v.ToByteArray(isUnsigned: true, isBigEndian: true);
        int msb = bytes[0];
        int bits = (bytes.Length - 1) * 8;
        while (msb != 0) { bits++; msb >>= 1; }
        return bits;
    }

    private static BigInteger ModP(BigInteger x)
    {
        var r = x % p; if (r < 0) r += p; return r;
    }
    private static BigInteger ModN(BigInteger x)
    {
        var r = x % n; if (r < 0) r += n; return r;
    }
    private static BigInteger ModInverseP(BigInteger x)
    {
        return ModInverse(x, p);
    }
    private static BigInteger ModInverseN(BigInteger x)
    {
        return ModInverse(x, n);
    }
    private static BigInteger ModInverse(BigInteger x, BigInteger m)
    {
        // 扩展欧几里得算法
        BigInteger r = m, newR = ModPositive(x, m);
        BigInteger t = 0, newT = 1;
        while (!newR.IsZero)
        {
            var q = r / newR;
            (r, newR) = (newR, r - q * newR);
            (t, newT) = (newT, t - q * newT);
        }
        if (r != 1) return 0; // 不可逆
        if (t < 0) t += m;
        return t;
    }
    private static BigInteger ModPositive(BigInteger x, BigInteger m)
    {
        var r = x % m; return r < 0 ? r + m : r;
    }

    private static BigInteger BigIntFromHex(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
    }
    private static BigInteger ToBigInt(ReadOnlySpan<byte> be32, bool isModN)
    {
        var bi = new BigInteger(be32, isUnsigned: true, isBigEndian: true);
        return isModN ? ModN(bi) : ModP(bi);
    }
    private static byte[] ToFixed32Bytes(BigInteger v)
    {
        var b = v.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (b.Length == 32) return b;
        var buf = new byte[32];
        if (b.Length > 32)
        {
            Buffer.BlockCopy(b, b.Length - 32, buf, 0, 32);
        }
        else
        {
            Buffer.BlockCopy(b, 0, buf, 32 - b.Length, b.Length);
        }
        return buf;
    }

    private static byte[] EncodeDerInteger(BigInteger v)
    {
        var m = v.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (m.Length == 0) m = new byte[] { 0x00 };
        // 若最高位为1，需要前置 0x00 以保持正数
        if ((m[0] & 0x80) != 0)
        {
            var tmp = new byte[m.Length + 1];
            tmp[0] = 0x00; Buffer.BlockCopy(m, 0, tmp, 1, m.Length); m = tmp;
        }
        var res = new byte[2 + m.Length];
        res[0] = 0x02; res[1] = (byte)m.Length; Buffer.BlockCopy(m, 0, res, 2, m.Length);
        return res;
    }
    private static byte[] EncodeDerSignature(BigInteger r, BigInteger s)
    {
        var R = EncodeDerInteger(r);
        var S = EncodeDerInteger(s);
        int len = R.Length + S.Length;
        var header = new List<byte>(4 + len) { 0x30, (byte)len };
        header.AddRange(R); header.AddRange(S);
        return header.ToArray();
    }
    private static bool TryDecodeDerSignature(ReadOnlySpan<byte> der, out BigInteger r, out BigInteger s)
    {
        r = s = 0;
        if (der.Length < 8) return false;
        int idx = 0;
        if (der[idx++] != 0x30) return false;
        int seqLen = der[idx++];
        if (seqLen != der.Length - 2) return false;
        if (der[idx++] != 0x02) return false;
        int rLen = der[idx++];
        if (idx + rLen > der.Length) return false;
        var rBytes = der.Slice(idx, rLen).ToArray(); idx += rLen;
        if (der[idx++] != 0x02) return false;
        int sLen = der[idx++];
        if (idx + sLen != der.Length) return false;
        var sBytes = der.Slice(idx, sLen).ToArray();
        r = new BigInteger(rBytes, isUnsigned: true, isBigEndian: true);
        s = new BigInteger(sBytes, isUnsigned: true, isBigEndian: true);
        return true;
    }

    // 工具：从私钥导出未压缩公钥（0x04||X||Y）
    public static byte[] DerivePublicKey(ReadOnlySpan<byte> privateKey)
    {
        if (privateKey.Length != 32) throw new ArgumentException("私钥应为32字节", nameof(privateKey));
        var d = ToBigInt(privateKey, isModN: true);
        if (d <= 0 || d >= n) throw new ArgumentException("私钥不在有效范围", nameof(privateKey));
        var Gp = new Point(gx, gy, false);
        var P = ScalarMult(Gp, d);
        var pub = new byte[65];
        pub[0] = 0x04;
        var x = ToFixed32Bytes(P.X);
        var y = ToFixed32Bytes(P.Y);
        Buffer.BlockCopy(x, 0, pub, 1, 32);
        Buffer.BlockCopy(y, 0, pub, 33, 32);
        return pub;
    }

    public static byte[] GeneratePrivateKey()
    {
        using var rng = RandomNumberGenerator.Create();
        var dBytes = new byte[32];
        while (true)
        {
            rng.GetBytes(dBytes);
            var d = new BigInteger(dBytes, isUnsigned: true, isBigEndian: true);
            d = ModN(d);
            if (d > 0 && d < n)
            {
                return ToFixed32Bytes(d);
            }
        }
    }

    public static bool TryDerToRaw(ReadOnlySpan<byte> der, out byte[] raw64)
    {
        raw64 = Array.Empty<byte>();
        if (!TryDecodeDerSignature(der, out var r, out var s)) return false;
        var rb = ToFixed32Bytes(r);
        var sb = ToFixed32Bytes(s);
        raw64 = new byte[64];
        Buffer.BlockCopy(rb, 0, raw64, 0, 32);
        Buffer.BlockCopy(sb, 0, raw64, 32, 32);
        return true;
    }

    public static byte[] RawToDer(ReadOnlySpan<byte> raw64)
    {
        if (raw64.Length != 64) throw new ArgumentException("raw 签名需为 64 字节(r||s)", nameof(raw64));
        var r = new BigInteger(raw64.Slice(0, 32), isUnsigned: true, isBigEndian: true);
        var s = new BigInteger(raw64.Slice(32, 32), isUnsigned: true, isBigEndian: true);
        return EncodeDerSignature(r, s);
    }
}

public sealed class Sm2EncryptorManaged : ISM2Encryptor
{
    private static readonly System.Numerics.BigInteger p = BigIntFromHex("FFFFFFFEFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00000000FFFFFFFFFFFFFFFF");
    private static readonly System.Numerics.BigInteger a = BigIntFromHex("FFFFFFFEFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00000000FFFFFFFFFFFFFFFC");
    private static readonly System.Numerics.BigInteger b = BigIntFromHex("28E9FA9E9D9F5E344D5A9E4BCF6509A7F39789F515AB8F92DDBCBD414D940E93");
    private static readonly System.Numerics.BigInteger n = BigIntFromHex("FFFFFFFEFFFFFFFFFFFFFFFFFFFFFFFF7203DF6B21C6052B53BBF40939D54123");
    private static readonly System.Numerics.BigInteger gx = BigIntFromHex("32C4AE2C1F1981195F9904466A39C9948FE30BBFF2660BE1715A4589334C74C7");
    private static readonly System.Numerics.BigInteger gy = BigIntFromHex("BC3736A2F4F6779C59BDCEE36B692153D0A9877CC62A474002DF32E52139F0A0");

    private readonly Point G = new Point(gx, gy, false);

    public byte[] Encrypt(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> plaintext)
    {
        if (publicKey.Length != 65 || publicKey[0] != 0x04) throw new ArgumentException("公钥需为未压缩格式 65字节", nameof(publicKey));
        var Px = ToBigInt(publicKey.Slice(1, 32), false);
        var Py = ToBigInt(publicKey.Slice(33, 32), false);
        var PA = new Point(Px, Py, false);
        if (!IsOnCurve(PA)) throw new ArgumentException("公钥不在曲线上", nameof(publicKey));

        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        while (true)
        {
            var k = RandomK(rng);
            var C1 = ScalarMult(G, k);
            var kP = ScalarMult(PA, k);
            var x2 = ToFixed32Bytes(kP.X);
            var y2 = ToFixed32Bytes(kP.Y);
            var t = KdfConcatSm3(x2, y2, plaintext.Length);
            if (AllZero(t)) continue;
            var c2 = Xor(plaintext, t);
            var sm3 = GM.Core.SM3.SM3.CreateDefault();
            var c3 = sm3.ComputeHash(Concat3(x2, plaintext, y2));
            var c1Bytes = EncodeUncompressed(C1);
            var result = new byte[c1Bytes.Length + c3.Length + c2.Length];
            Buffer.BlockCopy(c1Bytes, 0, result, 0, c1Bytes.Length);
            Buffer.BlockCopy(c3, 0, result, c1Bytes.Length, c3.Length);
            Buffer.BlockCopy(c2, 0, result, c1Bytes.Length + c3.Length, c2.Length);
            return result; // C1C3C2
        }
    }

    public byte[] Decrypt(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> ciphertext)
    {
        if (privateKey.Length != 32) throw new ArgumentException("私钥应为32字节", nameof(privateKey));
        var d = ToBigInt(privateKey, true);
        if (d <= 0 || d >= n) throw new ArgumentException("私钥不在有效范围", nameof(privateKey));
        if (ciphertext.Length < 65 + 32) throw new ArgumentException("密文长度不正确", nameof(ciphertext));
        // 解析 C1 (未压缩点)
        if (ciphertext[0] != 0x04) throw new ArgumentException("不支持的C1格式（需要未压缩点）", nameof(ciphertext));
        var C1x = ToBigInt(ciphertext.Slice(1, 32), false);
        var C1y = ToBigInt(ciphertext.Slice(33, 32), false);
        var C1 = new Point(C1x, C1y, false);
        if (!IsOnCurve(C1)) throw new ArgumentException("密文C1不在曲线上", nameof(ciphertext));
        var c3 = ciphertext.Slice(65, 32).ToArray();
        var c2 = ciphertext.Slice(97).ToArray();

        var dC1 = ScalarMult(C1, d);
        var x2 = ToFixed32Bytes(dC1.X);
        var y2 = ToFixed32Bytes(dC1.Y);
        var t = KdfConcatSm3(x2, y2, c2.Length);
        if (AllZero(t)) throw new System.Security.Cryptography.CryptographicException("KDF 输出为0，拒绝解密");
        var m = Xor(c2, t);
        var sm3 = GM.Core.SM3.SM3.CreateDefault();
        var u = sm3.ComputeHash(Concat3(x2, m, y2));
        if (!u.AsSpan().SequenceEqual(c3)) throw new System.Security.Cryptography.CryptographicException("C3 校验失败");
        return m;
    }

    private static byte[] EncodeUncompressed(in Point P)
    {
        var buf = new byte[65];
        buf[0] = 0x04;
        var xb = ToFixed32Bytes(P.X);
        var yb = ToFixed32Bytes(P.Y);
        Buffer.BlockCopy(xb, 0, buf, 1, 32);
        Buffer.BlockCopy(yb, 0, buf, 33, 32);
        return buf;
    }

    private static bool AllZero(ReadOnlySpan<byte> s)
    {
        foreach (var b0 in s) if (b0 != 0) return false; return true;
    }
    private static byte[] Xor(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var res = new byte[a.Length];
        for (int i = 0; i < a.Length; i++) res[i] = (byte)(a[i] ^ b[i]);
        return res;
    }
    private static byte[] Concat3(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, ReadOnlySpan<byte> c)
    {
        var res = new byte[a.Length + b.Length + c.Length];
        a.CopyTo(res);
        b.CopyTo(res.AsSpan(a.Length));
        c.CopyTo(res.AsSpan(a.Length + b.Length));
        return res;
    }
    private static byte[] KdfConcatSm3(ReadOnlySpan<byte> x2, ReadOnlySpan<byte> y2, int outLen)
    {
        var Z = new byte[x2.Length + y2.Length];
        x2.CopyTo(Z);
        y2.CopyTo(Z.AsSpan(x2.Length));
        var sm3 = GM.Core.SM3.SM3.CreateDefault();
        var res = new byte[outLen];
        uint ct = 1;
        int filled = 0;
        while (filled < outLen)
        {
            var buf = new byte[Z.Length + 4];
            Buffer.BlockCopy(Z, 0, buf, 0, Z.Length);
            buf[Z.Length + 0] = (byte)((ct >> 24) & 0xFF);
            buf[Z.Length + 1] = (byte)((ct >> 16) & 0xFF);
            buf[Z.Length + 2] = (byte)((ct >> 8) & 0xFF);
            buf[Z.Length + 3] = (byte)(ct & 0xFF);
            var block = sm3.ComputeHash(buf);
            int toCopy = Math.Min(block.Length, outLen - filled);
            Buffer.BlockCopy(block, 0, res, filled, toCopy);
            filled += toCopy;
            ct++;
        }
        return res;
    }

    private readonly struct Point
    {
        public readonly System.Numerics.BigInteger X;
        public readonly System.Numerics.BigInteger Y;
        public readonly bool IsInfinity;
        public Point(System.Numerics.BigInteger x, System.Numerics.BigInteger y, bool inf) { X = x; Y = y; IsInfinity = inf; }
    }
    private static bool IsOnCurve(in Point P)
    {
        if (P.IsInfinity) return true;
        var left = ModP(P.Y * P.Y);
        var right = ModP(P.X * P.X * P.X + a * P.X + b);
        return left == right;
    }
    private static Point PointDouble(in Point P)
    {
        if (P.IsInfinity) return P;
        if (P.Y.IsZero) return new Point(0, 0, true);
        var s = ModP((3 * P.X * P.X + a) * ModInverseP(2 * P.Y));
        var xr = ModP(s * s - 2 * P.X);
        var yr = ModP(s * (P.X - xr) - P.Y);
        return new Point(xr, yr, false);
    }
    private static Point PointAdd(in Point P, in Point Q)
    {
        if (P.IsInfinity) return Q;
        if (Q.IsInfinity) return P;
        if (P.X == Q.X)
        {
            if (ModP(P.Y + Q.Y).IsZero) return new Point(0, 0, true);
            return PointDouble(P);
        }
        var s = ModP((Q.Y - P.Y) * ModInverseP(Q.X - P.X));
        var xr = ModP(s * s - P.X - Q.X);
        var yr = ModP(s * (P.X - xr) - P.Y);
        return new Point(xr, yr, false);
    }
    private static Point ScalarMult(in Point P, System.Numerics.BigInteger k)
    {
        Point R = new Point(0, 0, true);
        Point add = P;
        var bits = GetBitLength(k);
        for (int i = 0; i < bits; i++)
        {
            if (!k.IsZero && ((k >> i) & 1) == 1)
            {
                R = PointAdd(R, add);
            }
            add = PointDouble(add);
        }
        return R;
    }
    private static int GetBitLength(System.Numerics.BigInteger v)
    {
        if (v.IsZero) return 0;
        var bytes = v.ToByteArray(isUnsigned: true, isBigEndian: true);
        int msb = bytes[0];
        int bits = (bytes.Length - 1) * 8;
        while (msb != 0) { bits++; msb >>= 1; }
        return bits;
    }
    private static System.Numerics.BigInteger ModP(System.Numerics.BigInteger x)
    {
        var r = x % p; if (r < 0) r += p; return r;
    }
    private static System.Numerics.BigInteger ModN(System.Numerics.BigInteger x)
    {
        var r = x % n; if (r < 0) r += n; return r;
    }
    private static System.Numerics.BigInteger ModInverseP(System.Numerics.BigInteger x)
    {
        return ModInverse(x, p);
    }
    private static System.Numerics.BigInteger ModInverse(System.Numerics.BigInteger x, System.Numerics.BigInteger m)
    {
        System.Numerics.BigInteger r = m, newR = ModPositive(x, m);
        System.Numerics.BigInteger t = 0, newT = 1;
        while (!newR.IsZero)
        {
            var q = r / newR;
            (r, newR) = (newR, r - q * newR);
            (t, newT) = (newT, t - q * newT);
        }
        if (r != 1) return 0;
        if (t < 0) t += m;
        return t;
    }
    private static System.Numerics.BigInteger ModPositive(System.Numerics.BigInteger x, System.Numerics.BigInteger m)
    {
        var r = x % m; return r < 0 ? r + m : r;
    }
    private static System.Numerics.BigInteger BigIntFromHex(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        return new System.Numerics.BigInteger(bytes, isUnsigned: true, isBigEndian: true);
    }
    private static System.Numerics.BigInteger ToBigInt(ReadOnlySpan<byte> be32, bool isModN)
    {
        var bi = new System.Numerics.BigInteger(be32, isUnsigned: true, isBigEndian: true);
        return isModN ? ModN(bi) : ModP(bi);
    }
    private static byte[] ToFixed32Bytes(System.Numerics.BigInteger v)
    {
        var b = v.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (b.Length == 32) return b;
        var buf = new byte[32];
        if (b.Length > 32) Buffer.BlockCopy(b, b.Length - 32, buf, 0, 32);
        else Buffer.BlockCopy(b, 0, buf, 32 - b.Length, b.Length);
        return buf;
    }
    private static System.Numerics.BigInteger RandomK(System.Security.Cryptography.RandomNumberGenerator rng)
    {
        Span<byte> buf = stackalloc byte[32];
        while (true)
        {
            rng.GetBytes(buf);
            var k = new System.Numerics.BigInteger(buf, isUnsigned: true, isBigEndian: true);
            k = ModN(k);
            if (k > 0 && k < n) return k;
        }
    }
}
