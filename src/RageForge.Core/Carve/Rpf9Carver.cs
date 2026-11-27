/* RAGEFORGE - RPF9 Carver */

using ICSharpCode.SharpZipLib.Zip.Compression;
using RageForge.Core.Audio;
using RageForge.Core.Rpf9;
using RageForge.Core.Video;

namespace RageForge.Core.Carve;

public readonly record struct RawStream(ulong Offset, ulong Csize, ulong Usize, ResourceKind Kind, uint Tag);

public static class Rpf9Carver
{
    private const int HeadLen = 256;
    private const ulong Chunk = 65536;
    private const int Window = 32 << 20;
    private const ulong Segment = 256UL << 20;
    private const int Lookahead = 4 << 20;
    private const int Refill = 1 << 20;
    private const int FirstRead = 64 << 10;

    [ThreadStatic] private static Inflater? t_inflater;
    [ThreadStatic] private static byte[]? t_out;
    [ThreadStatic] private static byte[]? t_read;
    [ThreadStatic] private static byte[]? t_head;
    [ThreadStatic] private static byte[]? t_window;

    public static int MaxParallelism { get; set; } = Environment.ProcessorCount;

    public static bool ZlibHeaderOk(byte a, byte b) =>
        a == 0x78 && b is 0x01 or 0x5e or 0x9c or 0xda && ((a << 8) | b) % 31 == 0;

    public static ulong? DataStart(string path)
    {
        try
        {
            Span<byte> head = stackalloc byte[Rpf9Header.Size];
            using var fs = File.OpenRead(path);
            fs.ReadExactly(head);
            var header = Rpf9Header.Read(head);
            ulong start = ((ulong)Rpf9Header.Size + header.TocEnd + 15) & ~15UL;
            return start <= (ulong)fs.Length ? start : null;
        }
        catch
        {
            return null;
        }
    }

    public static List<CarvedEntry> ScanArchive(string path, Action<ulong, ulong>? progress = null, ulong? maxBytes = null, ulong startBytes = 0, CancellationToken ct = default)
    {
        var archiveId = Path.GetFileNameWithoutExtension(path);
        ulong size = (ulong)new FileInfo(path).Length;
        if (DataStart(path) is not { } dataStart)
            return [];
        ulong start = dataStart + startBytes;
        ulong end = maxBytes is { } m ? Math.Min(size, start + m) : size;

        var (streams, blobs) = ScanParallel(path, start, end, progress, ct);
        var entries = GroupStreams(archiveId, streams);
        foreach (var b in blobs)
            entries.Add(new CarvedEntry
            {
                Id = $"{archiveId}@{b.Offset:x}",
                Offset = b.Offset,
                Csize = b.Size,
                Usize = b.Size,
                RawSize = b.Size,
                Streams = [],
                Kind = b.Kind,
                Sub = b.Sub,
                Ext = b.Kind == ResourceKind.Audio ? "awc" : "bk2",
            });
        entries.Sort((x, y) => x.Offset.CompareTo(y.Offset));
        return entries;
    }

    public static (List<RawStream> Streams, List<RawBlob> Blobs) ScanParallel(string path, ulong start, ulong end, Action<ulong, ulong>? progress, CancellationToken ct = default)
    {
        if (end <= start)
            return ([], []);

        int nseg = (int)((end - start + Segment - 1) / Segment);
        var streams = new List<RawStream>[nseg];
        var blobs = new List<RawBlob>[nseg];
        long done = 0;
        ulong total = end - start;
        ulong fileSize = (ulong)new FileInfo(path).Length;

        var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, MaxParallelism) };
        Parallel.For(0, nseg, options, i =>
        {
            ulong s = start + (ulong)i * Segment;
            ulong e = Math.Min(s + Segment, end);
            (streams[i], blobs[i]) = ScanRange(path, s, e, fileSize, step =>
            {
                long d = Interlocked.Add(ref done, (long)step);
                progress?.Invoke(Math.Min((ulong)d, total), total);
            }, ct);
        });

        progress?.Invoke(total, total);
        return (streams.SelectMany(x => x).ToList(), blobs.SelectMany(x => x).ToList());
    }

    private static (List<RawStream>, List<RawBlob>) ScanRange(string path, ulong start, ulong end, ulong fileSize, Action<ulong> advanced, CancellationToken ct)
    {
        var streams = new List<RawStream>();
        var blobs = new List<RawBlob>();
        using var window = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        using var inflateFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1);

        ulong pos = (start + 15) & ~15UL;
        var win = t_window ??= new byte[Window];
        ulong winOff = ulong.MaxValue;
        int winLen = 0;
        ulong reported = start;
        ulong limit = Math.Min(fileSize, end + Lookahead);

        while (pos < end)
        {
            ulong winEnd = winOff + (ulong)winLen;
            if (winOff == ulong.MaxValue || pos < winOff || pos + 64 > winEnd || (pos + Refill > winEnd && winEnd < limit))
            {
                ct.ThrowIfCancellationRequested();
                window.Position = (long)pos;
                int want = (int)Math.Min((ulong)Window, limit - pos);
                int n = 0;
                while (n < want)
                {
                    int r = window.Read(win, n, want - n);
                    if (r == 0) break;
                    n += r;
                }
                if (n < 2) break;
                winOff = pos;
                winLen = n;
            }

            int i = (int)(pos - winOff);
            ulong next = pos + 16;

            if (ZlibHeaderOk(win[i], win[i + 1]))
            {
                if (InflateAt(inflateFile, pos, win, i, winLen - i, null) is { Usize: > 0 } rs)
                {
                    streams.Add(rs);
                    next = (pos + rs.Csize + 15) & ~15UL;
                }
            }
            else if (win[i] == (byte)'A' && AwcFile.HasMagic(win.AsSpan(i, winLen - i))
                && AwcFile.Parse(win.AsSpan(i, winLen - i)) is { } awc
                && (ulong)awc.Extent <= fileSize - pos)
            {
                blobs.Add(new RawBlob(pos, (ulong)awc.Extent, ResourceKind.Audio, $"{awc.Streams.Count}st"));
                next = (pos + (ulong)awc.Extent + 15) & ~15UL;
            }
            else if (win[i] == (byte)'K' && Bk2Info.Parse(win.AsSpan(i, winLen - i), fileSize - pos) is { } bk)
            {
                blobs.Add(new RawBlob(pos, bk.TotalSize, ResourceKind.Video, $"{bk.Width}x{bk.Height}"));
                next = (pos + bk.TotalSize + 15) & ~15UL;
            }

            pos = next;
            if (pos - reported >= (8 << 20))
            {
                advanced(Math.Min(pos, end) - reported);
                reported = Math.Min(pos, end);
            }
        }

        if (end > reported)
            advanced(end - reported);
        return (streams, blobs);
    }

    public static RawStream? InflateAt(FileStream file, ulong off, byte[] pre, int preOff, int preLen, Stream? sink, ulong csizeHint = 0)
    {
        var inf = t_inflater ??= new Inflater(false);
        var outBuf = t_out ??= new byte[256 * 1024];
        var head = t_head ??= new byte[HeadLen];
        inf.Reset();
        int headLen = 0;

        try
        {
            if (preLen > 0)
            {
                inf.SetInput(pre, preOff, preLen);
                if (!Pump(inf, outBuf, head, ref headLen, sink))
                    return null;
                if (inf.IsFinished)
                    return Result(off, inf, head, headLen);
            }

            var buf = t_read ??= new byte[4 << 20];
            file.Position = (long)(off + (ulong)preLen);
            int chunk = csizeHint > (ulong)preLen
                ? (int)Math.Min((ulong)buf.Length, csizeHint - (ulong)preLen + 64)
                : FirstRead;
            while (true)
            {
                int n = file.Read(buf, 0, chunk);
                chunk = Math.Min(buf.Length, chunk * 2);
                if (n == 0) return null;
                inf.SetInput(buf, 0, n);
                if (!Pump(inf, outBuf, head, ref headLen, sink))
                    return null;
                if (inf.IsFinished)
                    return Result(off, inf, head, headLen);
            }
        }
        catch
        {
            return null;
        }
    }

    private static RawStream Result(ulong off, Inflater inf, byte[] head, int headLen)
    {
        var (kind, tag) = Classify.KindOf(head.AsSpan(0, headLen));
        return new(off, (ulong)inf.TotalIn, (ulong)inf.TotalOut, kind, tag);
    }

    private static bool Pump(Inflater inf, byte[] outBuf, byte[] head, ref int headLen, Stream? sink)
    {
        while (!inf.IsNeedingInput && !inf.IsFinished)
        {
            int produced = inf.Inflate(outBuf, 0, outBuf.Length);
            if (produced == 0)
                return inf.IsFinished || inf.IsNeedingInput;
            if (headLen < HeadLen)
            {
                int take = Math.Min(HeadLen - headLen, produced);
                Buffer.BlockCopy(outBuf, 0, head, headLen, take);
                headLen += take;
            }
            sink?.Write(outBuf, 0, produced);
        }
        return true;
    }

    public static List<CarvedEntry> GroupStreams(string archiveId, List<RawStream> streams)
    {
        streams.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        var outList = new List<CarvedEntry>();
        ulong nextOff = ulong.MaxValue;
        ulong lastU = 0;
        ulong lastEnd = 0;

        foreach (var s in streams)
        {
            if (s.Offset < lastEnd)
                continue;
            lastEnd = s.Offset + s.Csize;

            ulong alignedEnd = (s.Offset + s.Csize + 15) & ~15UL;
            var kind = s.Kind;
            bool contiguous = s.Offset == nextOff;
            bool misalignedObj = kind == ResourceKind.RageObj && s.Offset % 4096 != 0x10;
            bool cont = contiguous && outList.Count > 0
                && (lastU == Chunk || !Classify.StartsNewFile(kind) || misalignedObj);

            if (cont)
            {
                var e = outList[^1];
                e.Csize += s.Csize;
                e.Usize += s.Usize;
                e.Streams.Add(new ZlibStream(s.Offset, s.Csize, s.Usize));
            }
            else
            {
                var (sub, ext) = Classify.Describe(kind, s.Tag);
                outList.Add(new CarvedEntry
                {
                    Id = $"{archiveId}@{s.Offset:x}",
                    Offset = s.Offset,
                    Csize = s.Csize,
                    Usize = s.Usize,
                    Streams = [new ZlibStream(s.Offset, s.Csize, s.Usize)],
                    Kind = kind,
                    Sub = sub,
                    Ext = ext,
                });
            }

            nextOff = alignedEnd;
            lastU = s.Usize;
        }

        return outList;
    }
}
