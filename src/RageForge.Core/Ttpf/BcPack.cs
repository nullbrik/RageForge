/* RAGEFORGE - BCPack */

namespace RageForge.Core.Ttpf;

public static class BcPack
{
    public const int CodecBc1 = 0x04;

    private const uint Lower = 1u << 24;
    private const int FeedPerClock = 8;
    private const int Segments = 10;
    private const int Models = 16;

    private static readonly ushort[]?[] InverseTables = new ushort[9 * Models][];

    private static readonly byte[] Bc1Widths = [8, 8, 5, 6, 5, 5, 6, 5, 8, 8, 8, 8];

    private enum Phase : byte { Idle, Header0, Header1, Header2, Processing, Draining }

    private sealed class Lane
    {
        public int AlphabetBits;
        public Phase State = Phase.Idle;
        public byte Input;
        public bool InputFull;
        public ushort Symbol;
        public bool SymbolFull;
        public uint InputLeft;
        public uint OutputLeft;
        public uint StateP0;
        public uint StateP1;
        public bool ThreeByteHeader;
        public bool Flush;
        public int Model;
        public int Bits;
        public bool Done;
        public bool Started;
        public bool Stalled;
        public int Produced;
        public uint Final;
    }

    private static int Index(int bits, int model, int seg) => (bits * Models + model) * Segments + seg;

    private static (uint F, uint Cf) Forward(int bits, int model, ushort symbol)
    {
        int seg = symbol == 0 ? 0 : 32 - System.Numerics.BitOperations.LeadingZeroCount(symbol);
        uint start = seg == 0 ? 0u : 1u << (seg - 1);
        int k = Index(bits, model, seg);
        uint fa = BcPackModels.Freq[k];
        uint cfa = (BcPackModels.Base[k] + fa * ((symbol - start) & 0x1FF)) & 0xFFFF;
        return cfa == 0 ? ((fa - 1) & 0xFFFF, 1u) : (fa, cfa);
    }

    private static uint Advance(int bits, int model, uint x, ushort symbol)
    {
        var (f, cf) = Forward(bits, model, symbol);
        return unchecked(f * (x >> 16) + (x & 0xFFFF) - cf);
    }

    private static ushort InverseExact(int bits, int model, uint cf)
    {
        int seg = 0;
        for (int k = bits + 1; k >= 1; k--)
            if (cf >= BcPackModels.Base[Index(bits, model, k)])
            {
                seg = k;
                break;
            }
        int i = Index(bits, model, seg);
        uint baseX = seg == 0 ? 0u : 1u << (seg - 1);
        uint dist = (cf - BcPackModels.Base[i] + BcPackModels.Offset[i]) & 0xFFFF;
        uint steps = ((dist * BcPackModels.Reciprocal[i]) >> 15 >> BcPackModels.Shift[i]) & 0x1FF;
        return (ushort)((baseX + steps) & 0x1FF);
    }

    private static ushort[] InverseTable(int bits, int model)
    {
        int key = bits * Models + model;
        if (Volatile.Read(ref InverseTables[key]) is { } cached) return cached;
        var table = new ushort[65536];
        for (int cf = 0; cf < table.Length; cf++)
            table[cf] = InverseExact(bits, model, (uint)cf);
        Volatile.Write(ref InverseTables[key], table);
        return table;
    }

    public static ushort[][]? DecodeLanes(ReadOnlySpan<byte> data, ReadOnlySpan<byte> alphabetBits, int target)
    {
        int n = alphabetBits.Length;
        if (n is 0 or > 16 || target <= 0 || data.IsEmpty) return null;
        var lanes = new Lane[n];
        var output = new ushort[n][];
        var inverse = new ushort[n][];
        for (int i = 0; i < n; i++)
        {
            lanes[i] = new Lane { AlphabetBits = alphabetBits[i] };
            output[i] = new ushort[target];
            inverse[i] = InverseTable(0, 0);
        }

        uint slots = 0;
        int pos = 0;
        long maxClocks = 64L * ((long)n * target + data.Length) + 1000;
        (int, long, int) lastProgress = (-1, -1, -1);
        long lastChange = 0;
        long symbols = 0;

        for (long clock = 0; clock < maxClocks; clock++)
        {
            int served = 0;
            for (int i = 0; i < n; i++)
            {
                var ln = lanes[i];
                uint bit = 1u << i;
                bool blocked = ln.SymbolFull && (slots & bit) != 0;
                bool need = !ln.InputFull && (ln.State is Phase.Header0 or Phase.Header1 or Phase.Header2
                    || ln.State == Phase.Processing && ln.InputLeft > 0);
                bool grant = need && !blocked && served < FeedPerClock && pos < data.Length;
                ln.Stalled = need && !grant || blocked;
                if (ln.Stalled) continue;
                if (ln.State is Phase.Processing or Phase.Draining)
                    ln.StateP0 = ln.SymbolFull ? Advance(ln.Bits, ln.Model, ln.StateP1, ln.Symbol) : ln.StateP1;
                if (grant)
                {
                    ln.Input = data[pos++];
                    ln.InputFull = true;
                    served++;
                }
                if (ln.SymbolFull)
                {
                    slots |= bit;
                    output[i][ln.Produced++] = ln.Symbol;
                    symbols++;
                    ln.SymbolFull = false;
                }
            }

            uint active = 0;
            bool finished = true;
            int stateSum = 0;
            for (int i = 0; i < n; i++)
            {
                var ln = lanes[i];
                if (!ln.Stalled && !Step(ln, target, ref inverse[i])) return null;
                uint bit = 1u << i;
                if (!(ln.Done && !ln.SymbolFull && (slots & bit) == 0)) active |= bit;
                finished &= ln.Done && !ln.SymbolFull;
                stateSum += (int)ln.State;
            }
            if (slots != 0 && (slots & active) == active) slots = 0;

            if (finished)
            {
                foreach (var ln in lanes)
                    if (ln.Final != Lower && ln.Final != Lower >> 8 && ln.Final != Lower >> 16) return null;
                for (int k = pos; k < data.Length; k++)
                    if (data[k] != 0) return null;
                return output;
            }
            var progress = (pos, symbols, stateSum);
            if (progress != lastProgress)
            {
                lastProgress = progress;
                lastChange = clock;
            }
            else if (clock - lastChange > 64)
            {
                return null;
            }
        }
        return null;
    }

    private static bool Step(Lane ln, int target, ref ushort[] inverse)
    {
        switch (ln.State)
        {
            case Phase.Idle:
                if (!ln.Started)
                {
                    ln.Started = true;
                    ln.State = Phase.Header0;
                    ln.OutputLeft = (uint)target;
                    ln.StateP0 = 0;
                    ln.StateP1 = 0;
                }
                return true;
            case Phase.Header0:
                if (!ln.InputFull) return true;
                ln.InputFull = false;
                ln.InputLeft = 0;
                ln.Model = ln.Input & 15;
                ln.ThreeByteHeader = (ln.Input & 0x20) != 0;
                ln.Flush = (ln.Input & 0x10) != 0;
                int bits = (ln.AlphabetBits - (ln.Input >> 6)) & 15;
                ln.Bits = bits < 10 ? bits : 9;
                ln.State = Phase.Header1;
                return ln.Bits <= 8;
            case Phase.Header1:
                if (!ln.InputFull) return true;
                ln.InputFull = false;
                inverse = InverseTable(ln.Bits, ln.Model);
                if (ln.ThreeByteHeader)
                {
                    ln.InputLeft = ln.Input;
                    ln.State = Phase.Header2;
                }
                else
                {
                    ln.InputLeft = ln.Input + 1u;
                    ln.State = Phase.Processing;
                }
                return true;
            case Phase.Header2:
                if (!ln.InputFull) return true;
                ln.InputFull = false;
                ln.InputLeft = ((uint)ln.Input << 8 | (ln.InputLeft & 0xFF)) + 257;
                ln.State = Phase.Processing;
                return true;
        }

        bool processing = ln.State == Phase.Processing;
        bool feed = processing && ln.StateP0 < Lower && ln.InputLeft > 0 && ln.InputFull;
        uint x = feed ? (ln.StateP0 & 0xFF_FFFF) << 8 | ln.Input : ln.StateP0;
        bool emit = x > Lower && ln.OutputLeft > 0;
        if (emit)
        {
            ln.OutputLeft--;
            ln.Symbol = inverse[x & 0xFFFF];
        }
        ln.StateP1 = x;
        ln.SymbolFull = emit;
        if (processing)
        {
            if (feed)
            {
                ln.InputLeft--;
                ln.InputFull = false;
            }
            if (ln.InputLeft != 0) return true;
            if (ln.OutputLeft > 0)
            {
                if (x == Lower)
                {
                    ln.State = Phase.Header0;
                    if (ln.Flush) ln.StateP1 = 0;
                }
                else
                {
                    ln.State = Phase.Draining;
                }
            }
            else
            {
                Finish(ln, x, emit);
            }
            return true;
        }
        if (x > Lower) return true;
        if (ln.OutputLeft > 0)
        {
            ln.State = Phase.Header0;
            if (ln.Flush)
            {
                ln.StateP0 = 0;
                ln.StateP1 = 0;
            }
        }
        else
        {
            Finish(ln, x, emit);
        }
        return true;
    }

    private static void Finish(Lane ln, uint x, bool emitted)
    {
        ln.Done = true;
        ln.State = Phase.Idle;
        ln.Final = emitted ? Advance(ln.Bits, ln.Model, x, ln.Symbol) : x;
    }

    private static readonly uint[] QuadrantBits = BuildQuadrantBits();

    private static uint[] BuildQuadrantBits()
    {
        var bits = new uint[4 * 256];
        Span<byte> codes = stackalloc byte[4];
        for (int q = 0; q < 4; q++)
            for (int s = 0; s < 256; s++)
            {
                byte p = BcPackBc1Tables.Patterns[s];
                for (int k = 0; k < 4; k++) codes[k] = (byte)((p >> (2 * k)) & 3);
                bits[q * 256 + s] = QuadrantMask(q, codes);
            }
        return bits;
    }

    public static byte[]? DecodeBc1(ReadOnlySpan<byte> data, int blocks)
    {
        if (blocks <= 0 || DecodeBc1Lanes(data, blocks) is not { } lanes) return null;
        return AssembleBc1(lanes, BcPackBc1Tables.Window, QuadrantBits);
    }

    public static ushort[][]? DecodeBc1Lanes(ReadOnlySpan<byte> data, int blocks)
    {
        Span<byte> bits = stackalloc byte[Bc1Widths.Length];
        for (int i = 0; i < bits.Length; i++) bits[i] = (byte)(Bc1Widths[i] - 1);
        return DecodeLanes(data, bits, blocks);
    }

    public static int BackDistance(ushort v)
    {
        int b = v & 0xFF;
        return (b & 1) == 0 ? b >> 1 : 255 - (b >> 1);
    }

    public static int Signed(ushort v) => (v & 1) != 0 ? -((v + 1) >> 1) : v >> 1;

    public static byte[] AssembleBc1(ushort[][] lanes, ReadOnlySpan<byte> window, ReadOnlySpan<uint> quadrantBits)
    {
        int n = lanes[0].Length;
        var fields = new byte[n * 6];
        var blocks = new byte[n * 8];
        Span<byte> e = stackalloc byte[6];
        for (int i = 0; i < n; i++)
        {
            int d = BackDistance(lanes[0][i]);
            for (int k = 0; k < 6; k++)
            {
                int pred = d == 0 ? 0 : d <= i ? fields[(i - d) * 6 + k] : window[((i - d) & 0xFF) * 6 + k];
                int mask = (1 << Bc1Widths[2 + k]) - 1;
                e[k] = (byte)((pred + Signed(lanes[2 + k][i])) & mask);
                fields[i * 6 + k] = e[k];
            }
            ushort c0 = (ushort)(e[0] << 11 | e[1] << 5 | e[2]);
            ushort c1 = (ushort)(e[3] << 11 | e[4] << 5 | e[5]);
            uint word = 0;
            for (int q = 0; q < 4; q++)
                word |= quadrantBits[q * 256 + (lanes[8 + q][i] & 0xFF)];
            var block = blocks.AsSpan(i * 8, 8);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(block, c0);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(block[2..], c1);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(block[4..], word);
        }
        return blocks;
    }

    public static uint QuadrantMask(int quadrant, ReadOnlySpan<byte> codes)
    {
        int ox = (quadrant & 1) * 2, oy = (quadrant >> 1) * 2;
        uint word = 0;
        for (int k = 0; k < 4; k++)
        {
            int x = ox + (k & 1), y = oy + (k >> 1);
            word |= (uint)(codes[k] & 3) << (2 * (4 * y + x));
        }
        return word;
    }
}
