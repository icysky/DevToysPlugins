using System.Buffers.Binary;
using System.Text;

namespace GM.Core.SM3;

internal sealed class SM3Managed : ISM3
{
    private const int BlockSize = 64; // 512-bit
    private const int DigestSize = 32; // 256-bit

    public byte[] ComputeHash(ReadOnlySpan<byte> data)
    {
        Span<byte> result = stackalloc byte[DigestSize];
        ComputeHashInternal(data, result);
        return result.ToArray();
    }

    public string ComputeHashHex(ReadOnlySpan<byte> data)
    {
        var bytes = ComputeHash(data);
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public void ComputeHash(Stream input, Stream output)
    {
        // 流式：分块读取，最终一次性做填充
        Span<uint> V = stackalloc uint[8];
        Initialize(V);

        byte[] buffer = new byte[8192];
        long totalBits = 0;
        int buffered = 0;
        byte[] block = new byte[BlockSize];

        while (true)
        {
            int read = input.Read(buffer, 0, buffer.Length);
            if (read <= 0) break;
            totalBits += (long)read * 8;
            int offset = 0;
            while (offset < read)
            {
                int toCopy = Math.Min(BlockSize - buffered, read - offset);
                Buffer.BlockCopy(buffer, offset, block, buffered, toCopy);
                buffered += toCopy; offset += toCopy;
                if (buffered == BlockSize)
                {
                    Compress(V, block);
                    buffered = 0;
                }
            }
        }

        // 填充
        Span<byte> finalBlock = stackalloc byte[BlockSize * 2];
        int fbLen = 0;
        finalBlock[..buffered].Clear();
        if (buffered > 0) new ReadOnlySpan<byte>(block, 0, buffered).CopyTo(finalBlock);
        fbLen = buffered;

        finalBlock[fbLen++] = 0x80;
        int padLen = ((BlockSize - 8) - fbLen) % BlockSize;
        if (padLen < 0) padLen += BlockSize;
        for (int i = 0; i < padLen; i++) finalBlock[fbLen++] = 0x00;
        BinaryPrimitives.WriteUInt64BigEndian(finalBlock.Slice(fbLen, 8), (ulong)totalBits);
        fbLen += 8;

        for (int i = 0; i < fbLen; i += BlockSize)
        {
            Compress(V, finalBlock.Slice(i, BlockSize));
        }

        Span<byte> digest = stackalloc byte[DigestSize];
        for (int i = 0; i < 8; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(digest.Slice(i * 4, 4), V[i]);
        }
        output.Write(digest);
    }

    private static void Initialize(Span<uint> V)
    {
        V[0] = 0x7380166f;
        V[1] = 0x4914b2b9;
        V[2] = 0x172442d7;
        V[3] = 0xda8a0600;
        V[4] = 0xa96f30bc;
        V[5] = 0x163138aa;
        V[6] = 0xe38dee4d;
        V[7] = 0xb0fb0e4e;
    }

    private static uint ROTL(uint x, int n) => (x << n) | (x >> (32 - n));
    private static uint FF(int j, uint x, uint y, uint z) => j < 16 ? (x ^ y ^ z) : ((x & y) | (x & z) | (y & z));
    private static uint GG(int j, uint x, uint y, uint z) => j < 16 ? (x ^ y ^ z) : ((x & y) | (~x & z));
    private static uint P0(uint x) => x ^ ROTL(x, 9) ^ ROTL(x, 17);
    private static uint P1(uint x) => x ^ ROTL(x, 15) ^ ROTL(x, 23);

    private static void Compress(Span<uint> V, ReadOnlySpan<byte> B)
    {
        Span<uint> W = stackalloc uint[68];
        Span<uint> W1 = stackalloc uint[64];

        for (int i = 0; i < 16; i++)
        {
            W[i] = BinaryPrimitives.ReadUInt32BigEndian(B.Slice(i * 4, 4));
        }
        for (int i = 16; i < 68; i++)
        {
            uint x = W[i - 16] ^ W[i - 9] ^ ROTL(W[i - 3], 15);
            W[i] = P1(x) ^ ROTL(W[i - 13], 7) ^ W[i - 6];
        }
        for (int i = 0; i < 64; i++)
        {
            W1[i] = W[i] ^ W[i + 4];
        }

        uint A = V[0], Bv = V[1], C = V[2], D = V[3], E = V[4], F = V[5], G = V[6], H = V[7];

        for (int j = 0; j < 64; j++)
        {
            uint Tj = j < 16 ? 0x79cc4519u : 0x7a879d8au;
            uint SS1 = ROTL((ROTL(A, 12) + E + ROTL(Tj, j)) & 0xffffffffu, 7);
            uint SS2 = SS1 ^ ROTL(A, 12);
            uint TT1 = (FF(j, A, Bv, C) + D + SS2 + W1[j]) & 0xffffffffu;
            uint TT2 = (GG(j, E, F, G) + H + SS1 + W[j]) & 0xffffffffu;
            D = C;
            C = ROTL(Bv, 9);
            Bv = A;
            A = TT1;
            H = G;
            G = ROTL(F, 19);
            F = E;
            E = P0(TT2);
        }

        V[0] ^= A; V[1] ^= Bv; V[2] ^= C; V[3] ^= D; V[4] ^= E; V[5] ^= F; V[6] ^= G; V[7] ^= H;
    }

    private static void ComputeHashInternal(ReadOnlySpan<byte> data, Span<byte> output)
    {
        Span<uint> V = stackalloc uint[8];
        Initialize(V);

        int fullBlocks = data.Length / BlockSize;
        for (int i = 0; i < fullBlocks; i++)
        {
            Compress(V, data.Slice(i * BlockSize, BlockSize));
        }

        int rem = data.Length - fullBlocks * BlockSize;
        Span<byte> final = stackalloc byte[BlockSize * 2];
        data.Slice(fullBlocks * BlockSize, rem).CopyTo(final);
        int fbLen = rem;
        final[fbLen++] = 0x80;
        int padLen = ((BlockSize - 8) - fbLen) % BlockSize;
        if (padLen < 0) padLen += BlockSize;
        for (int i = 0; i < padLen; i++) final[fbLen++] = 0x00;
        ulong bitLen = (ulong)data.Length * 8UL;
        BinaryPrimitives.WriteUInt64BigEndian(final.Slice(fbLen, 8), bitLen);
        fbLen += 8;

        for (int i = 0; i < fbLen; i += BlockSize)
            Compress(V, final.Slice(i, BlockSize));

        for (int i = 0; i < 8; i++)
            BinaryPrimitives.WriteUInt32BigEndian(output.Slice(i * 4, 4), V[i]);
    }
}
