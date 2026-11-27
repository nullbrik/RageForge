/* RAGEFORGE - Gfx10 Texture Layout */

using System.Numerics;

namespace RageForge.Core.Ttpf;

public enum Swizzle
{
    S4K,
    D64K,
}

public readonly struct MipPlace
{
    public uint Width { get; init; }
    public uint Height { get; init; }
    public ulong Offset { get; init; }
    public bool IsTail { get; init; }
    public uint TailX { get; init; }
    public uint TailY { get; init; }
}

public sealed class Gfx10Layout
{
    private readonly (bool IsX, int Bit)[] _eq;
    private readonly uint _bpe;

    public uint BlockWidth { get; }
    public uint BlockHeight { get; }
    public ulong BlockBytes { get; }
    public MipPlace[] Mips { get; }
    public uint Slices { get; private set; }
    public bool SliceMajor { get; private set; }
    public ulong SliceBytes { get; }
    public ulong Base { get; private set; }
    public ulong Total { get; private set; }

    private Gfx10Layout((bool, int)[] eq, uint bpe, uint bw, uint bh, ulong bs, MipPlace[] mips, ulong sliceBytes)
    {
        _eq = eq;
        _bpe = bpe;
        BlockWidth = bw;
        BlockHeight = bh;
        BlockBytes = bs;
        Mips = mips;
        Slices = 1;
        SliceBytes = sliceBytes;
        Total = sliceBytes;
    }

    private static (bool, int)[]? Equation(Swizzle sw, uint bpe)
    {
        const bool X = true, Y = false;
        return (sw, bpe) switch
        {
            (Swizzle.S4K, 16) => [(Y, 0), (Y, 1), (X, 0), (X, 1), (Y, 2), (X, 2), (Y, 3), (X, 3)],
            (Swizzle.S4K, 8) => [(X, 0), (Y, 0), (Y, 1), (X, 1), (X, 2), (Y, 2), (X, 3), (Y, 3), (X, 4)],
            (Swizzle.S4K, 4) => [(X, 0), (X, 1), (Y, 0), (Y, 1), (Y, 2), (X, 2), (Y, 3), (X, 3), (Y, 4), (X, 4)],
            (Swizzle.S4K, 2) => [(X, 0), (X, 1), (X, 2), (Y, 0), (Y, 1), (Y, 2), (X, 3), (Y, 3), (X, 4), (Y, 4), (X, 5)],
            (Swizzle.S4K, 1) => [(X, 0), (X, 1), (X, 2), (X, 3), (Y, 0), (Y, 1), (Y, 2), (Y, 3), (Y, 4), (X, 4), (Y, 5), (X, 5)],
            (Swizzle.D64K, 16) => [(X, 0), (Y, 0), (X, 1), (Y, 1), (Y, 2), (X, 2), (Y, 3), (X, 3), (Y, 4), (X, 4), (Y, 5), (X, 5)],
            (Swizzle.D64K, 8) => [(X, 0), (Y, 0), (X, 1), (X, 2), (Y, 1), (Y, 2), (X, 3), (Y, 3), (X, 4), (Y, 4), (X, 5), (Y, 5), (X, 6)],
            (Swizzle.D64K, 4) => [(X, 0), (X, 1), (Y, 0), (Y, 1), (X, 2), (Y, 2), (Y, 3), (X, 3), (Y, 4), (X, 4), (Y, 5), (X, 5), (Y, 6), (X, 6)],
            (Swizzle.D64K, 2) => [(X, 0), (X, 1), (X, 2), (Y, 0), (Y, 1), (Y, 2), (X, 3), (Y, 3), (X, 4), (Y, 4), (X, 5), (Y, 5), (X, 6), (Y, 6), (X, 7)],
            (Swizzle.D64K, 1) => [(Y, 0), (Y, 1), (Y, 2), (X, 1), (X, 0), (X, 2), (Y, 3), (X, 3), (X, 4), (Y, 4), (X, 5), (Y, 5), (X, 6), (Y, 6), (X, 7), (Y, 7)],
            _ => null,
        };
    }

    private static uint DivCeil(uint a, uint b) => (a + b - 1) / b;

    public static Gfx10Layout? Create(Swizzle sw, uint w, uint h, uint mips, uint bpe)
    {
        var eq = Equation(sw, bpe);
        if (eq is null)
            return null;

        uint nx = (uint)eq.Count(e => e.Item1);
        uint bw = 1u << (int)nx;
        uint bh = 1u << (eq.Length - (int)nx);
        ulong bs = (ulong)bw * bh * bpe;
        int log2 = BitOperations.Log2(bs);
        uint maxTail = log2 <= 11 ? (uint)(1 + (1 << (log2 - 9))) : (uint)(log2 - 4);
        uint tw = bw / 2, th = bh;
        uint nm = Math.Max(mips, 1);

        var dims = new (uint W, uint H)[nm];
        for (uint i = 0; i < nm; i++)
            dims[i] = (Math.Max(w >> (int)i, 1), Math.Max(h >> (int)i, 1));

        int first = (int)nm;
        for (int i = 0; i < nm; i++)
        {
            if (dims[i].W <= tw && dims[i].H <= th && nm - (uint)i <= maxTail)
            {
                first = i;
                break;
            }
        }

        var place = new MipPlace[nm];
        for (int i = 0; i < nm; i++)
            place[i] = new MipPlace { Width = dims[i].W, Height = dims[i].H };

        ulong off = first < nm ? bs : 0;
        for (int i = first - 1; i >= 0; i--)
        {
            var (mw, mh) = dims[i];
            place[i] = place[i] with { Offset = off };
            off += (ulong)(DivCeil(mw, bw) * bw) * (DivCeil(mh, bh) * bh) * bpe;
        }

        (uint X, uint Y) b256 = bpe switch
        {
            16 => (4u, 4u),
            8 => (8u, 4u),
            4 => (8u, 8u),
            2 => (16u, 8u),
            _ => (16u, 16u),
        };
        for (int k = first; k < nm; k++)
        {
            uint m = maxTail - 1 - (uint)(k - first);
            uint mo = m > 6 ? (16u << (int)m) : (m << 8);
            uint mx = 0, my = 0;
            for (int b = 0; b < 6; b++)
            {
                mx |= ((mo >> (9 + 2 * b)) & 1) << b;
                my |= ((mo >> (8 + 2 * b)) & 1) << b;
            }
            place[k] = place[k] with { Offset = 0, IsTail = true, TailX = mx * b256.X, TailY = my * b256.Y };
        }

        return new Gfx10Layout(eq, bpe, bw, bh, bs, place, off);
    }

    public Gfx10Layout WithSlices(uint n)
    {
        n = Math.Max(n, 1);
        Total = SliceBytes * n;
        Slices = n;
        return this;
    }

    public Gfx10Layout? WithCubeCopies(uint slices, uint copies, ulong? baseOverride)
    {
        int first = Array.FindIndex(Mips, p => p.IsTail);
        bool tail = first >= 0;
        if (first < 0)
            first = Mips.Length;
        int units = first + (tail ? 1 : 0);
        int n = (int)copies;
        if (n == 0 || n > units)
            return null;

        ulong Chain(int j)
        {
            if (tail && j == 0)
                return BlockBytes;
            int m = first - (j + 1 - (tail ? 1 : 0));
            return Mips[m].Offset + LevelBytes(m);
        }

        ulong s = Math.Max(slices, 1);
        ulong Pad(ulong b) => b > (128ul << 10) ? ((b + (64ul << 10) - 1) / (64ul << 10)) * (64ul << 10) : b;

        ulong computedBase = 0;
        if (baseOverride is null)
            for (int c = 0; c < n - 1; c++)
                computedBase += Pad(s * Chain(units - n + c));

        Slices = Math.Max(slices, 1);
        SliceMajor = true;
        Base = baseOverride ?? computedBase;
        Total = Base + s * SliceBytes;
        return this;
    }

    private ulong LevelBytes(int m)
    {
        var p = Mips[m];
        if (p.IsTail)
            return BlockBytes;
        return (ulong)(DivCeil(p.Width, BlockWidth) * BlockWidth) * (DivCeil(p.Height, BlockHeight) * BlockHeight) * _bpe;
    }

    private ulong LevelStart(int m, uint s)
    {
        if (SliceMajor)
        {
            ulong off = Mips[m].IsTail ? 0 : Mips[m].Offset;
            return Base + (ulong)s * SliceBytes + off;
        }
        ulong b = Mips[m].IsTail ? 0 : Mips[m].Offset * Slices;
        return b + (ulong)s * LevelBytes(m);
    }

    private ulong InBlock(uint x, uint y)
    {
        ulong a = 0;
        for (int k = 0; k < _eq.Length; k++)
        {
            var (isx, bit) = _eq[k];
            uint v = isx ? x : y;
            a |= (ulong)((v >> bit) & 1) << k;
        }
        return a;
    }

    public (ulong Start, ulong End) MipSpan(int m)
    {
        ulong a = LevelStart(m, 0);
        return (a, a + LevelBytes(m));
    }

    public ulong Addr(int m, uint s, uint x, uint y)
    {
        var p = Mips[m];
        ulong baseAddr = LevelStart(m, s);
        if (p.IsTail)
            return baseAddr + InBlock(x + p.TailX, y + p.TailY) * _bpe;

        ulong pitch = DivCeil(p.Width, BlockWidth);
        ulong blk = (ulong)(y / BlockHeight) * pitch + x / BlockWidth;
        return baseAddr + blk * BlockBytes + InBlock(x % BlockWidth, y % BlockHeight) * _bpe;
    }

    public byte[] UntileSlice(ReadOnlySpan<byte> src, int m, uint slice)
    {
        var p = Mips[m];
        int b = (int)_bpe;
        var outBuf = new byte[(int)(p.Width * p.Height) * b];
        for (uint y = 0; y < p.Height; y++)
        {
            for (uint x = 0; x < p.Width; x++)
            {
                int s = (int)Addr(m, slice, x, y);
                if (s >= 0 && s + b <= src.Length)
                {
                    int d = (int)(y * p.Width + x) * b;
                    src.Slice(s, b).CopyTo(outBuf.AsSpan(d, b));
                }
            }
        }
        return outBuf;
    }
}
