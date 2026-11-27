/* RAGEFORGE - TTPF Store */

using System.Buffers.Binary;
using System.IO.Compression;

namespace RageForge.Core.Ttpf;

public sealed class TtpfStore
{
    private readonly string _path;
    private readonly (ulong FileOffset, ulong VirtOffset, byte Flags, ulong Pad)[] _chunks;

    public string Path => _path;
    public ulong VirtSize { get; }
    public IReadOnlyList<TtpfTexture> Textures { get; }

    private TtpfStore(string path, (ulong, ulong, byte, ulong)[] chunks, ulong virtSize, TtpfTexture[] textures)
    {
        _path = path;
        _chunks = chunks;
        VirtSize = virtSize;
        Textures = textures;
    }

    public static TtpfStore Open(string path)
    {
        using var fs = File.OpenRead(path);

        Span<byte> head = stackalloc byte[24];
        ReadExact(fs, head);
        if (!head[..4].SequenceEqual("RDS5"u8))
            throw new InvalidDataException("Not an RDS5 (TTPF) store.");

        int n = (int)BinaryPrimitives.ReadUInt32LittleEndian(head[4..]);
        ulong virtSize = BinaryPrimitives.ReadUInt32LittleEndian(head[8..]);

        var table = new byte[8 * n];
        ReadExact(fs, table);
        var chunks = new List<(ulong, ulong, byte, ulong)>(n);
        for (int i = 0; i < n; i++)
        {
            uint a = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(8 * i));
            uint b = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(8 * i + 4));
            if (a == 0 && b == 0)
                break;
            ulong foff = (ulong)(a & 0xFF_FFFF) << 8;
            ulong voff = 0x1000 + (ulong)(b & 0xF_FFFF) * 0x4000 + (ulong)(a >> 30) * 0x1000;
            chunks.Add((foff, voff, (byte)(a >> 24), b >> 20));
        }

        long eoff = (0x10 + 8 * n + 0xFF) & ~0xFF;
        fs.Position = eoff;
        Span<byte> etpf = stackalloc byte[16];
        ReadExact(fs, etpf);
        if (!etpf[..4].SequenceEqual("ETPF"u8))
            throw new InvalidDataException($"No ETPF index at 0x{eoff:x}.");

        int nt = (int)BinaryPrimitives.ReadUInt32LittleEndian(etpf[8..]);
        var body = new byte[24 * nt];
        ReadExact(fs, body);

        var textures = new TtpfTexture[nt];
        for (int i = 0; i < nt; i++)
        {
            uint d0 = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(16 * i));
            uint d1 = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(16 * i + 4));
            ulong loc = BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(16 * i + 8));
            textures[i] = new TtpfTexture
            {
                Hash = BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(16 * nt + 8 * i)),
                Width = (d0 & 0x3FFF) + 1,
                Height = ((d0 >> 14) & 0x3FFF) + 1,
                DxgiFormat = (d1 >> 3) & 0x7F,
                Virt = (loc & 0x1F_FFFF) * 0x1000 + 0x1000,
                D0 = d0,
                D1 = d1,
                Loc = loc,
            };
        }

        var sortedVirt = chunks.Select(c => c.Item2).ToArray();
        foreach (var t in textures)
        {
            if (t.TileMode is >= 9 and <= 12)
            {
                int n2 = (int)(t.TileMode - 8);
                int idx = Array.BinarySearch(sortedVirt, t.Virt);
                if (idx >= 0 && idx + n2 - 1 < chunks.Count)
                    t.CubeBase = chunks[idx + n2 - 1].Item2 - t.Virt;
            }
            t.Size = t.Layout?.Total ?? 0;
        }

        var packed = textures.OrderBy(t => t.Virt).ToArray();
        for (int i = 0; i + 1 < packed.Length; i++)
            if (packed[i + 1].Virt > packed[i].Virt)
                packed[i].FitMipsTo(packed[i + 1].Virt - packed[i].Virt);

        return new TtpfStore(path, chunks.ToArray(), virtSize, textures);
    }

    public (byte[] Data, ulong Skipped) ReadVirtual(ulong virt, ulong len)
    {
        var outBuf = new byte[len];
        ulong skipped = 0;
        ulong end = virt + len;

        using var fs = File.OpenRead(_path);
        ulong fileLen = (ulong)fs.Length;
        for (int i = 0; i < _chunks.Length; i++)
        {
            var (foff, v0, fl, pad) = _chunks[i];
            var (fnext, v1) = i + 1 < _chunks.Length
                ? (_chunks[i + 1].FileOffset, _chunks[i + 1].VirtOffset)
                : (fileLen, VirtSize > v0 ? VirtSize : ulong.MaxValue);
            if (foff >= fileLen || fnext <= foff)
                continue;
            if (v1 <= virt || v0 >= end)
                continue;

            if (Undecodable(fl))
            {
                skipped += Math.Min(end, v1) - Math.Max(virt, v0);
                continue;
            }

            fs.Position = (long)foff;
            var comp = new byte[(fnext - foff > pad ? fnext - foff - pad : 0)];
            ReadExact(fs, comp);

            byte[] raw;
            if (IsPacked(fl))
            {
                if (v1 == ulong.MaxValue || BcPack.DecodeBc1(comp, (int)((v1 - v0) / 8)) is not { } blocks)
                {
                    skipped += Math.Min(end, v1) - Math.Max(virt, v0);
                    continue;
                }
                raw = blocks;
            }
            else if ((fl & 0x3F) == 0)
            {
                raw = comp;
            }
            else
            {
                using var ms = new MemoryStream(comp);
                using var zs = new ZLibStream(ms, CompressionMode.Decompress);
                using var dst = new MemoryStream((int)Math.Min(v1 - v0, 64UL << 20));
                zs.CopyTo(dst);
                raw = dst.ToArray();
            }

            ulong s = Math.Max(virt, v0);
            ulong e = Math.Min(Math.Min(end, v1), v0 + (ulong)raw.Length);
            if (e > s)
                raw.AsSpan((int)(s - v0), (int)(e - s)).CopyTo(outBuf.AsSpan((int)(s - virt)));
        }

        return (outBuf, skipped);
    }

    private static bool IsPacked(byte flags) => (flags & 0x1C) != 0;

    private static bool Undecodable(byte flags) => IsPacked(flags) && (flags & 0x3F) != BcPack.CodecBc1;

    public ulong BcpackBytes(ulong virt, ulong len)
    {
        ulong end = virt + len;
        ulong n = 0;
        for (int i = 0; i < _chunks.Length - 1; i++)
        {
            var (_, v0, fl, _) = _chunks[i];
            var v1 = _chunks[i + 1].Item2;
            if (v1 > virt && v0 < end && Undecodable(fl))
                n += Math.Min(end, v1) - Math.Max(virt, v0);
        }
        return n;
    }

    public uint? BestMip(TtpfTexture t)
    {
        var l = t.Layout;
        if (l is null)
            return null;
        for (int m = 0; m < l.Mips.Length; m++)
        {
            var (a, b) = l.MipSpan(m);
            if (BcpackBytes(t.Virt + a, b - a) == 0)
                return (uint)m;
        }
        return null;
    }

    public const ulong MaxTextureBytes = 1UL << 30;

    public (byte[] Data, ulong Skipped) ReadTexture(TtpfTexture t) => ReadTexture(t, t.Size);

    public (byte[] Data, ulong Skipped) ReadTexture(TtpfTexture t, ulong length)
    {
        ulong len = Math.Min(length, t.Size);
        if (len > MaxTextureBytes)
            throw new InvalidDataException($"texture {t.HashHex} claims {len:N0} bytes, more than the {MaxTextureBytes >> 20} MB limit.");
        return ReadVirtual(t.Virt, len);
    }

    private static void ReadExact(Stream s, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = s.Read(buffer[total..]);
            if (read == 0)
                throw new EndOfStreamException();
            total += read;
        }
    }
}
