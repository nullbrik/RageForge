/* RAGEFORGE - RAGE Resource */

using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

namespace RageForge.Core.Carve;

public sealed class RageResource
{
    public byte[] Sys { get; }
    public byte[] Gfx { get; }

    private RageResource(byte[] sys, byte[] gfx)
    {
        Sys = sys;
        Gfx = gfx;
    }

    public static RageResource FromPages(byte[] sys, byte[] gfx) => new(sys, gfx);

    public static RageResource Load(string archive, CarvedEntry e, bool withGfx = true)
    {
        using var f = OpenShared(archive);
        return Load(f, e, withGfx);
    }

    public static RageResource Load(FileStream f, CarvedEntry e, bool withGfx = true)
    {
        var (sys, n) = ReadSys(f, e);
        if (!withGfx || n >= e.Streams.Count)
            return new RageResource(sys, []);

        return new RageResource(sys, InflateRange(f, e.Streams, n, e.Streams.Count));
    }

    public static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);

    public static byte[] ReadRaw(string archive, CarvedEntry e)
    {
        using var f = OpenShared(archive);
        var buf = new byte[e.RawSize];
        f.Position = (long)e.Offset;
        f.ReadExactly(buf);
        return buf;
    }

    public static byte[] ReadAll(string archive, CarvedEntry e)
    {
        if (e.IsRaw)
            return ReadRaw(archive, e);
        using var f = OpenShared(archive);
        return InflateRange(f, e.Streams, 0, e.Streams.Count);
    }

    public static byte[] ReadStream(FileStream f, ZlibStream st) => InflateRange(f, [st], 0, 1);

    private static byte[] InflateRange(FileStream f, IReadOnlyList<ZlibStream> streams, int from, int to)
    {
        ulong total = 0;
        for (int k = from; k < to; k++)
            total += streams[k].Usize;
        if (total > (ulong)Array.MaxLength)
            throw new InvalidDataException($"resource too large ({total:N0} bytes)");
        var buf = new byte[total];
        int off = 0;
        for (int k = from; k < to; k++)
        {
            int len = (int)streams[k].Usize;
            InflateInto(f, streams[k], buf, off, len);
            off += len;
        }
        return buf;
    }

    private static void InflateInto(FileStream f, ZlibStream st, byte[] dest, int off, int len)
    {
        if (st.Csize is > 0 and <= MaxPooledInput && TryInflateNative(f, st, dest.AsSpan(off, len)))
            return;
        using var sink = new MemoryStream(dest, off, len, writable: true);
        if (Rpf9Carver.InflateAt(f, st.Offset, [], 0, 0, sink, st.Csize) is null)
            throw new InvalidDataException($"inflate failed at 0x{st.Offset:x}");
    }

    private const ulong MaxPooledInput = 256UL << 20;

    private static bool TryInflateNative(FileStream f, ZlibStream st, Span<byte> dest)
    {
        int csize = (int)st.Csize;
        var input = ArrayPool<byte>.Shared.Rent(csize);
        try
        {
            f.Position = (long)st.Offset;
            f.ReadExactly(input, 0, csize);
            using var source = new MemoryStream(input, 0, csize, writable: false);
            using var zlib = new ZLibStream(source, CompressionMode.Decompress);
            zlib.ReadExactly(dest);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
        {
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(input);
        }
    }

    private static (byte[] Sys, int N) ReadSys(FileStream f, CarvedEntry e)
    {
        var parts = new List<byte[]> { ReadStream(f, e.Streams[0]) };
        long length = parts[0].Length;
        int n = 1;

        ulong pm = parts[0].Length >= 16 ? BinaryPrimitives.ReadUInt64LittleEndian(parts[0].AsSpan(8, 8)) : 0;
        if (pm >> 28 == 5)
        {
            ulong need = (pm & 0x0FFF_FFFF) + 8;
            while ((ulong)length < need && n < e.Streams.Count)
            {
                var part = ReadStream(f, e.Streams[n]);
                parts.Add(part);
                length += part.Length;
                n++;
            }
        }

        while (n < e.Streams.Count)
        {
            ulong next = e.Streams[n].Usize;
            if (PointersInto(parts, (ulong)length, next) < Math.Max(2, (int)(next / 65536)))
                break;
            var part = ReadStream(f, e.Streams[n]);
            parts.Add(part);
            length += part.Length;
            n++;
        }

        if (parts.Count == 1)
            return (parts[0], n);
        var sys = new byte[length];
        int at = 0;
        foreach (var part in parts)
        {
            part.CopyTo(sys, at);
            at += part.Length;
        }
        return (sys, n);
    }

    private static int PointersInto(List<byte[]> parts, ulong start, ulong size)
    {
        int hits = 0;
        long global = 0;
        foreach (var part in parts)
        {
            int first = (int)((8 - global % 8) % 8);
            for (int c = first; c + 8 <= part.Length; c += 8)
            {
                ulong p = BinaryPrimitives.ReadUInt64LittleEndian(part.AsSpan(c, 8));
                ulong o = p & 0x0FFF_FFFF;
                if (p >> 28 == 5 && (p & 3) == 0 && o >= start && o < start + size)
                    hits++;
            }
            global += part.Length;
        }
        return hits;
    }

    private bool Fits(int o, int n) => o >= 0 && (long)o + n <= Sys.Length;

    public ushort U16(int o) => Fits(o, 2) ? BinaryPrimitives.ReadUInt16LittleEndian(Sys.AsSpan(o)) : (ushort)0;
    public uint U32(int o) => Fits(o, 4) ? BinaryPrimitives.ReadUInt32LittleEndian(Sys.AsSpan(o)) : 0;
    public ulong U64(int o) => Fits(o, 8) ? BinaryPrimitives.ReadUInt64LittleEndian(Sys.AsSpan(o)) : 0;
    public float F32(int o) => BitConverter.UInt32BitsToSingle(U32(o));

    public int? SysOff(ulong ptr)
    {
        if (ptr >> 28 == 5)
        {
            int o = (int)(ptr & 0x0FFF_FFFF);
            if (o < Sys.Length) return o;
        }
        return null;
    }

    public int? GfxOff(ulong ptr)
    {
        if (ptr >> 28 == 6)
        {
            int o = (int)(ptr & 0x0FFF_FFFF);
            if (o <= Gfx.Length) return o;
        }
        return null;
    }

    public static (string Name, string Tag)? FindResourceName(ReadOnlySpan<byte> sys)
    {
        int i = 0;
        while (true)
        {
            int p = IndexOf(sys[i..], ".#"u8);
            if (p < 0) break;
            int at = i + p;
            if (at + 4 <= sys.Length)
            {
                var tag = sys.Slice(at + 2, 2);
                if (IsLower(tag[0]) && IsLower(tag[1]))
                {
                    int s = at;
                    while (s > 0 && IsNameChar(sys[s - 1])) s--;
                    if (at - s >= 2)
                        return (
                            System.Text.Encoding.ASCII.GetString(sys[s..at]),
                            System.Text.Encoding.ASCII.GetString(tag));
                }
            }
            i = at + 2;
        }
        return null;
    }

    private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle) => haystack.IndexOf(needle);
    private static bool IsLower(byte c) => c >= (byte)'a' && c <= (byte)'z';
    private static bool IsNameChar(byte c) =>
        IsLower(c) || (c >= (byte)'0' && c <= (byte)'9') || c == (byte)'_' || c == (byte)'-';
}
