using System.Buffers.Binary;

namespace GM.Core.SM4;

internal sealed class SM4Managed : ISM4Cipher
{
    private const int BlockSize = 16;

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext, in Sm4Parameters parameters)
        => Process(plaintext, parameters, encrypt: true);

    public byte[] Decrypt(ReadOnlySpan<byte> ciphertext, in Sm4Parameters parameters)
        => Process(ciphertext, parameters, encrypt: false);

    private static byte[] Process(ReadOnlySpan<byte> input, in Sm4Parameters p, bool encrypt)
    {
        if (p.Key is null || p.Key.Length != 16) throw new ArgumentException("SM4 key must be 16 bytes.");

        // 修复（相对 GM 原实现）：CTR 模式下无论加密还是解密，
        // 密钥流都必须用「加密方向」的轮密钥（计数器块做加密）。
        // 原实现按 encrypt 取轮密钥，导致 CTR 解密得到乱码。
        bool useDecryptKeys = !encrypt && p.Mode != Sm4Mode.Ctr;
        var rk = ExpandKey(p.Key, !useDecryptKeys);

        if (p.Mode == Sm4Mode.Ecb)
        {
            var data = encrypt ? ApplyPadding(input, p.Padding) : input.ToArray();
            if (data.Length % BlockSize != 0) throw new ArgumentException("Invalid input length for SM4 ECB.");
            for (int i = 0; i < data.Length; i += BlockSize)
                CryptBlockInPlace(data.AsSpan(i, BlockSize), rk);
            if (!encrypt) data = RemovePadding(data, p.Padding);
            return data;
        }
        else if (p.Mode == Sm4Mode.Cbc)
        {
            if (p.IV is null || p.IV.Length != 16) throw new ArgumentException("CBC requires 16-byte IV.");
            var iv = p.IV.ToArray();
            if (encrypt)
            {
                var data = ApplyPadding(input, p.Padding);
                for (int i = 0; i < data.Length; i += BlockSize)
                {
                    XorBlock(data.AsSpan(i, BlockSize), iv);
                    CryptBlockInPlace(data.AsSpan(i, BlockSize), rk);
                    Buffer.BlockCopy(data, i, iv, 0, BlockSize);
                }
                return data;
            }
            else
            {
                if (input.Length % BlockSize != 0) throw new ArgumentException("Invalid ciphertext length for CBC.");
                var outBuf = input.ToArray();
                var prev = iv;
                for (int i = 0; i < outBuf.Length; i += BlockSize)
                {
                    var block = outBuf.AsSpan(i, BlockSize);
                    var encCopy = block.ToArray();
                    CryptBlockInPlace(block, rk);
                    XorBlock(block, prev);
                    prev = encCopy;
                }
                return RemovePadding(outBuf, p.Padding);
            }
        }
        else // CTR
        {
            if (p.IV is null || p.IV.Length != 16) throw new ArgumentException("CTR requires 16-byte nonce/counter.");
            var counter = p.IV.ToArray();
            var outBuf2 = new byte[input.Length];
            int offset = 0;
            Span<byte> keystream = stackalloc byte[BlockSize];
            while (offset < input.Length)
            {
                var ctrBlock = counter.ToArray();
                CryptBlockInPlace(ctrBlock, rk);
                int n = Math.Min(BlockSize, input.Length - offset);
                for (int i = 0; i < n; i++)
                    outBuf2[offset + i] = (byte)(input[offset + i] ^ ctrBlock[i]);
                offset += n;
                IncrementCounter(counter);
            }
            return outBuf2;
        }
    }

    private static void IncrementCounter(byte[] counter)
    {
        for (int i = 15; i >= 0; i--)
        {
            if (++counter[i] != 0) break;
        }
    }

    private static void XorBlock(Span<byte> a, ReadOnlySpan<byte> b)
    {
        for (int i = 0; i < BlockSize; i++) a[i] ^= b[i];
    }

    private static byte[] ApplyPadding(ReadOnlySpan<byte> input, Sm4Padding padding)
    {
        switch (padding)
        {
            case Sm4Padding.None:
                if (input.Length % BlockSize != 0) throw new ArgumentException("Input length must be multiple of 16 when Padding=None.");
                return input.ToArray();
            case Sm4Padding.Zeros:
                int pad0 = (BlockSize - (input.Length % BlockSize)) % BlockSize;
                var r0 = new byte[input.Length + pad0];
                input.CopyTo(r0);
                return r0;
            case Sm4Padding.PKCS7:
            default:
                int pad = BlockSize - (input.Length % BlockSize);
                if (pad == 0) pad = BlockSize;
                var r = new byte[input.Length + pad];
                input.CopyTo(r);
                for (int i = input.Length; i < r.Length; i++) r[i] = (byte)pad;
                return r;
        }
    }

    private static byte[] RemovePadding(ReadOnlySpan<byte> input, Sm4Padding padding)
    {
        switch (padding)
        {
            case Sm4Padding.None:
                return input.ToArray();
            case Sm4Padding.Zeros:
                int len = input.Length;
                while (len > 0 && input[len - 1] == 0) len--;
                var rz = new byte[len];
                input[..len].CopyTo(rz);
                return rz;
            case Sm4Padding.PKCS7:
            default:
                if (input.Length == 0 || input.Length % BlockSize != 0) throw new ArgumentException("Invalid PKCS7 padded data.");
                int pad = input[^1];
                if (pad <= 0 || pad > BlockSize) throw new ArgumentException("Invalid PKCS7 padding.");
                for (int i = 1; i <= pad; i++) if (input[^i] != pad) throw new ArgumentException("Invalid PKCS7 padding.");
                var rr = new byte[input.Length - pad];
                input[..^pad].CopyTo(rr);
                return rr;
        }
    }

    // ---- SM4 Core ----
    private static readonly uint[] FK = { 0xa3b1bac6u, 0x56aa3350u, 0x677d9197u, 0xb27022dcu };
    private static readonly uint[] CK = new uint[32]
    {
        0x00070e15u,0x1c232a31u,0x383f464du,0x545b6269u,0x70777e85u,0x8c939aa1u,0xa8afb6bdu,0xc4cbd2d9u,
        0xe0e7eef5u,0xfc030a11u,0x181f262du,0x343b4249u,0x50575e65u,0x6c737a81u,0x888f969du,0xa4abb2b9u,
        0xc0c7ced5u,0xdce3eaf1u,0xf8ff060du,0x141b2229u,0x30373e45u,0x4c535a61u,0x686f767du,0x848b9299u,
        0xa0a7aeb5u,0xbcc3cad1u,0xd8dfe6edu,0xf4fb0209u,0x10171e25u,0x2c333a41u,0x484f565du,0x646b7279u
    };

    private static readonly byte[] Sbox = new byte[]
    {
        0xd6,0x90,0xe9,0xfe,0xcc,0xe1,0x3d,0xb7,0x16,0xb6,0x14,0xc2,0x28,0xfb,0x2c,0x05,
        0x2b,0x67,0x9a,0x76,0x2a,0xbe,0x04,0xc3,0xaa,0x44,0x13,0x26,0x49,0x86,0x06,0x99,
        0x9c,0x42,0x50,0xf4,0x91,0xef,0x98,0x7a,0x33,0x54,0x0b,0x43,0xed,0xcf,0xac,0x62,
        0xe4,0xb3,0x1c,0xa9,0xc9,0x08,0xe8,0x95,0x80,0xdf,0x94,0xfa,0x75,0x8f,0x3f,0xa6,
        0x47,0x07,0xa7,0xfc,0xf3,0x73,0x17,0xba,0x83,0x59,0x3c,0x19,0xe6,0x85,0x4f,0xa8,
        0x68,0x6b,0x81,0xb2,0x71,0x64,0xda,0x8b,0xf8,0xeb,0x0f,0x4b,0x70,0x56,0x9d,0x35,
        0x1e,0x24,0x0e,0x5e,0x63,0x58,0xd1,0xa2,0x25,0x22,0x7c,0x3b,0x01,0x21,0x78,0x87,
        0xd4,0x00,0x46,0x57,0x9f,0xd3,0x27,0x52,0x4c,0x36,0x02,0xe7,0xa0,0xc4,0xc8,0x9e,
        0xea,0xbf,0x8a,0xd2,0x40,0xc7,0x38,0xb5,0xa3,0xf7,0xf2,0xce,0xf9,0x61,0x15,0xa1,
        0xe0,0xae,0x5d,0xa4,0x9b,0x34,0x1a,0x55,0xad,0x93,0x32,0x30,0xf5,0x8c,0xb1,0xe3,
        0x1d,0xf6,0xe2,0x2e,0x82,0x66,0xca,0x60,0xc0,0x29,0x23,0xab,0x0d,0x53,0x4e,0x6f,
        0xd5,0xdb,0x37,0x45,0xde,0xfd,0x8e,0x2f,0x03,0xff,0x6a,0x72,0x6d,0x6c,0x5b,0x51,
        0x8d,0x1b,0xaf,0x92,0xbb,0xdd,0xbc,0x7f,0x11,0xd9,0x5c,0x41,0x1f,0x10,0x5a,0xd8,
        0x0a,0xc1,0x31,0x88,0xa5,0xcd,0x7b,0xbd,0x2d,0x74,0xd0,0x12,0xb8,0xe5,0xb4,0xb0,
        0x89,0x69,0x97,0x4a,0x0c,0x96,0x77,0x7e,0x65,0xb9,0xf1,0x09,0xc5,0x6e,0xc6,0x84,
        0x18,0xf0,0x7d,0xec,0x3a,0xdc,0x4d,0x20,0x79,0xee,0x5f,0x3e,0xd7,0xcb,0x39,0x48
    };

    private static uint Tau(uint a)
    {
        byte[] b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, a);
        for (int i = 0; i < 4; i++) b[i] = Sbox[b[i]];
        return BinaryPrimitives.ReadUInt32BigEndian(b);
    }

    private static uint L(uint b)
    {
        return b ^ Rol(b, 2) ^ Rol(b, 10) ^ Rol(b, 18) ^ Rol(b, 24);
    }

    private static uint Lp(uint b)
    {
        return b ^ Rol(b, 13) ^ Rol(b, 23);
    }

    private static uint Rol(uint x, int n) => (x << n) | (x >> (32 - n));

    private static void CryptBlockInPlace(Span<byte> block, uint[] rk)
    {
        uint X0 = BinaryPrimitives.ReadUInt32BigEndian(block.Slice(0, 4));
        uint X1 = BinaryPrimitives.ReadUInt32BigEndian(block.Slice(4, 4));
        uint X2 = BinaryPrimitives.ReadUInt32BigEndian(block.Slice(8, 4));
        uint X3 = BinaryPrimitives.ReadUInt32BigEndian(block.Slice(12, 4));

        for (int i = 0; i < 32; i++)
        {
            uint tmp = X1 ^ X2 ^ X3 ^ rk[i];
            tmp = L(Tau(tmp));
            uint Xn = X0 ^ tmp;
            X0 = X1; X1 = X2; X2 = X3; X3 = Xn;
        }
        // 输出为 (X3,X2,X1,X0)
        BinaryPrimitives.WriteUInt32BigEndian(block.Slice(0, 4), X3);
        BinaryPrimitives.WriteUInt32BigEndian(block.Slice(4, 4), X2);
        BinaryPrimitives.WriteUInt32BigEndian(block.Slice(8, 4), X1);
        BinaryPrimitives.WriteUInt32BigEndian(block.Slice(12, 4), X0);
    }

    private static uint[] ExpandKey(byte[] key, bool encrypt)
    {
        uint K0 = BinaryPrimitives.ReadUInt32BigEndian(key.AsSpan(0, 4)) ^ FK[0];
        uint K1 = BinaryPrimitives.ReadUInt32BigEndian(key.AsSpan(4, 4)) ^ FK[1];
        uint K2 = BinaryPrimitives.ReadUInt32BigEndian(key.AsSpan(8, 4)) ^ FK[2];
        uint K3 = BinaryPrimitives.ReadUInt32BigEndian(key.AsSpan(12, 4)) ^ FK[3];

        uint[] rk = new uint[32];
        for (int i = 0; i < 32; i++)
        {
            uint t = K1 ^ K2 ^ K3 ^ CK[i];
            t = Lp(Tau(t));
            uint Ki = K0 ^ t;
            rk[i] = Ki;
            K0 = K1; K1 = K2; K2 = K3; K3 = Ki;
        }
        if (!encrypt)
        {
            Array.Reverse(rk);
        }
        return rk;
    }
}
