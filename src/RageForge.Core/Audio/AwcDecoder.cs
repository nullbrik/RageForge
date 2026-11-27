/* RAGEFORGE - AWC Decoder */

using System.Buffers.Binary;
using Concentus;

namespace RageForge.Core.Audio;

public sealed record DecodedAudio(int Channels, int SampleRate, short[] Samples)
{
    public double Seconds => Samples.Length / (double)(Channels * SampleRate);
}

public static class AwcDecoder
{
    private const int SampleRate = 48_000;
    private const int MaxFrame = 5760;

    public static List<DecodedAudio> Decode(byte[] data)
    {
        var result = new List<DecodedAudio>();
        if (AwcFile.Parse(data, data.Length) is not { } awc)
            return result;

        foreach (var chunks in awc.Streams)
        {
            AwcChunk? body = null, format = null, info = null;
            foreach (var c in chunks)
            {
                if (c.Tag == AwcFile.TagData && c.Size > 0) body ??= c;
                else if (c.Tag == AwcFile.TagStreamFormat) format ??= c;
                else if (c.Tag == AwcFile.TagFormat && c.Size >= 10) info ??= c;
            }
            if (body is not { } b)
                continue;

            if (awc.Multichannel && format is { } f)
                result.AddRange(DecodeMultichannel(data, f, b));
            else if (DecodePackets(Packets(data.AsMemory(b.Offset, b.Size)), 0) is { } audio)
                result.Add(info is { } i ? audio with { SampleRate = RateOr(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i.Offset + 8))) } : audio);
        }
        return result;
    }

    private static List<ReadOnlyMemory<byte>> Packets(ReadOnlyMemory<byte> frames)
    {
        var list = new List<ReadOnlyMemory<byte>>();
        for (int start = 0; start < frames.Length; start += 2048)
        {
            var blk = frames.Slice(start, Math.Min(2048, frames.Length - start));
            int p = 0;
            while (p + 2 <= blk.Length)
            {
                int len = BinaryPrimitives.ReadUInt16LittleEndian(blk.Span[p..]);
                if (len == 0 || p + 2 + len > blk.Length) break;
                list.Add(blk.Slice(p + 2, len));
                p += 2 + len;
            }
        }
        return list;
    }

    private static DecodedAudio? DecodePackets(List<ReadOnlyMemory<byte>> packets, int skipPerSegment)
    {
        if (packets.Count == 0)
            return null;
        int ch = (packets[0].Span[0] & 0x04) != 0 ? 2 : 1;
        var decoder = OpusCodecFactory.CreateDecoder(SampleRate, ch);
        var pcm = new short[MaxFrame * ch];
        var samples = new List<short>(packets.Count * 960 * ch);
        int skip = skipPerSegment * ch;
        foreach (var pkt in packets)
        {
            int n;
            try { n = decoder.Decode(pkt.Span, pcm, MaxFrame); }
            catch { continue; }
            int got = n * ch;
            int drop = Math.Min(skip, got);
            skip -= drop;
            for (int i = drop; i < got; i++) samples.Add(pcm[i]);
        }
        return samples.Count == 0 ? null : new DecodedAudio(ch, SampleRate, [.. samples]);
    }

    private static IEnumerable<DecodedAudio> DecodeMultichannel(byte[] data, AwcChunk format, AwcChunk body)
    {
        var f = data.AsSpan(format.Offset, format.Size);
        if (f.Length < 12) yield break;
        int blockCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(f);
        int blockSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(f[4..]);
        int nch = (int)BinaryPrimitives.ReadUInt32LittleEndian(f[8..]);
        if (nch is <= 0 or > 64 || blockSize < 0x800 || f.Length < 12 + nch * 16) yield break;

        var declared = new int[nch];
        var rates = new int[nch];
        for (int c = 0; c < nch; c++)
        {
            declared[c] = (int)BinaryPrimitives.ReadUInt32LittleEndian(f[(12 + c * 16 + 4)..]);
            rates[c] = RateOr(BinaryPrimitives.ReadUInt16LittleEndian(f[(12 + c * 16 + 10)..]));
        }

        var segments = new List<(int Skip, List<ReadOnlyMemory<byte>> Packets)>[nch];
        for (int c = 0; c < nch; c++) segments[c] = [];

        var bodyMem = data.AsMemory(body.Offset, body.Size);
        for (int blkIdx = 0; blkIdx < blockCount; blkIdx++)
        {
            int start = blkIdx * blockSize;
            if (start + nch * 24 > bodyMem.Length) break;
            var blk = bodyMem.Slice(start, Math.Min(blockSize, bodyMem.Length - start));
            var frames = new int[nch];
            int totalFrames = 0;
            for (int c = 0; c < nch; c++)
            {
                frames[c] = (int)BinaryPrimitives.ReadUInt32LittleEndian(blk.Span[(c * 24 + 4)..]);
                totalFrames += frames[c];
            }
            int p = (nch * 24 + totalFrames * 4 + 0x7FF) & ~0x7FF;
            for (int c = 0; c < nch; c++)
            {
                int end = Math.Min(p + frames[c] * 2048, blk.Length);
                var pk = p < end ? Packets(blk[p..end]) : [];
                int skipSamples = (int)BinaryPrimitives.ReadUInt32LittleEndian(blk.Span[(c * 24 + 8)..]);
                segments[c].Add((skipSamples, pk));
                p += frames[c] * 2048;
            }
        }

        for (int c = 0; c < nch; c++)
        {
            var first = segments[c].SelectMany(s => s.Packets).FirstOrDefault();
            if (first.IsEmpty) continue;
            int ch = (first.Span[0] & 0x04) != 0 ? 2 : 1;
            var decoder = OpusCodecFactory.CreateDecoder(SampleRate, ch);
            var pcm = new short[MaxFrame * ch];
            var samples = new List<short>();
            foreach (var (skip, packets) in segments[c])
            {
                int left = skip * ch;
                foreach (var pkt in packets)
                {
                    int n;
                    try { n = decoder.Decode(pkt.Span, pcm, MaxFrame); }
                    catch { continue; }
                    int got = n * ch;
                    int drop = Math.Min(left, got);
                    left -= drop;
                    for (int i = drop; i < got; i++) samples.Add(pcm[i]);
                }
            }
            int limit = declared[c] * ch;
            if (limit > 0 && samples.Count > limit)
                samples.RemoveRange(limit, samples.Count - limit);
            if (samples.Count > 0)
                yield return new DecodedAudio(ch, rates[c], [.. samples]);
        }
    }

    private static int RateOr(int declared) => declared is >= 8000 and <= 96000 ? declared : SampleRate;
}
