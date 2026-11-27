/* RAGEFORGE - TTPF Decoder */

using System.Numerics;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;

namespace RageForge.Core.Ttpf;

public readonly struct DecodedTexture
{
    public required byte[] Rgba { get; init; }
    public required uint Width { get; init; }
    public required uint Height { get; init; }
    public required uint Mip { get; init; }
    public ulong Key { get; init; }
    public MipChain? Blocks { get; init; }
}

public static class TtpfDecoder
{
    private static readonly BcDecoder Decoder = new();

    public static DecodedTexture Decode(TtpfStore store, TtpfTexture texture, uint maxDim)
    {
        var (data, mip) = Read(store, texture, maxDim);
        return DecodeSlice(texture, data, mip, 0);
    }

    public static DecodedTexture Decode(TtpfStore store, TtpfTexture texture) => Decode(store, texture, 0);

    public static DecodedTexture Decode(TtpfStore store, TtpfTexture texture, uint maxDim, bool withBlocks)
    {
        var (data, mip) = Read(store, texture, maxDim);
        if (!withBlocks || Blocks(texture, data, mip) is not { } chain)
            return DecodeSlice(texture, data, mip, 0);
        return new DecodedTexture
        {
            Rgba = ToRgba(chain.Format, chain.Levels[0], chain.Width, chain.Height),
            Width = chain.Width,
            Height = chain.Height,
            Mip = mip,
            Blocks = chain,
        };
    }

    public static MipChain? ReadBlocks(TtpfStore store, TtpfTexture texture, uint maxDim)
    {
        var (data, mip) = Read(store, texture, maxDim);
        return Blocks(texture, data, mip);
    }

    public static MipChain? Blocks(TtpfTexture texture, ReadOnlySpan<byte> data, uint mip)
    {
        var fmt = texture.Format;
        if (fmt.BlockDim() != 4 || fmt is TtpfFormat.Bc6U or TtpfFormat.Bc6S || texture.Layout is not { } layout)
            return null;
        if (!BitOperations.IsPow2(texture.Width) || !BitOperations.IsPow2(texture.Height) || mip >= layout.Mips.Length)
            return null;
        uint w = Math.Max(texture.Width >> (int)mip, 1), h = Math.Max(texture.Height >> (int)mip, 1);
        if (w % 4 != 0 || h % 4 != 0)
            return null;

        long bpe = fmt.BytesPerElem();
        var levels = new List<byte[]>();
        for (int m = (int)mip; m < layout.Mips.Length; m++)
        {
            long need = DivCeil(Math.Max(texture.Width >> m, 1), 4) * DivCeil(Math.Max(texture.Height >> m, 1), 4) * bpe;
            var place = layout.Mips[m];
            if ((long)place.Width * place.Height * bpe != need)
                break;
            levels.Add(layout.UntileSlice(data, m, 0));
        }
        return levels.Count == 0 ? null : new MipChain { Format = fmt, Width = w, Height = h, Levels = [.. levels] };
    }

    private static (byte[] Data, uint Mip) Read(TtpfStore store, TtpfTexture texture, uint maxDim)
    {
        if (!texture.Decodable || texture.Layout is not { } layout)
            throw new NotSupportedException($"tile mode {texture.TileMode} / format {texture.FormatName} not supported.");

        uint want = 0;
        if (maxDim != 0)
            while (want + 1 < layout.Mips.Length && Math.Max(texture.Width, texture.Height) >> (int)want > maxDim)
                want++;
        if (want != 0)
        {
            ulong needed = !layout.SliceMajor && layout.Slices == 1 ? layout.MipSpan((int)want).End : texture.Size;
            var (part, partSkipped) = store.ReadTexture(texture, needed);
            uint floor = partSkipped != 0 ? store.BestMip(texture) ?? want : 0;
            return (part, Math.Max(want, floor));
        }

        var (data, skipped) = store.ReadTexture(texture);
        uint mip = 0;
        if (skipped != 0)
        {
            mip = store.BestMip(texture)
                ?? throw new NotSupportedException($"{skipped} bytes in BCPack chunks, no mip outside them (Xbox hardware codec).");
        }
        return (data, mip);
    }

    public static DecodedTexture DecodeSlice(TtpfTexture texture, ReadOnlySpan<byte> data, uint mip, uint slice)
    {
        var fmt = texture.Format;
        if (fmt == TtpfFormat.Unknown)
            throw new NotSupportedException($"unsupported format {texture.FormatName}.");

        var layout = texture.Layout
            ?? throw new NotSupportedException($"tile mode {texture.TileMode} / {texture.FormatName} not supported.");
        if (mip >= layout.Mips.Length)
            throw new ArgumentOutOfRangeException(nameof(mip));
        if (slice >= layout.Slices)
            throw new ArgumentOutOfRangeException(nameof(slice));

        uint mw = Math.Max(texture.Width >> (int)mip, 1);
        uint mh = Math.Max(texture.Height >> (int)mip, 1);
        var linear = layout.UntileSlice(data, (int)mip, slice);

        return new DecodedTexture
        {
            Rgba = ToRgba(fmt, linear, mw, mh),
            Width = mw,
            Height = mh,
            Mip = mip,
        };
    }

    public static byte[] ToRgba(TtpfFormat fmt, byte[] data, uint w, uint h)
    {
        int wu = (int)w, hu = (int)h;

        if (fmt is TtpfFormat.R16F or TtpfFormat.Rgba16F)
            return DecodeHalf(fmt, data, wu, hu);
        if (fmt.BlockDim() == 1)
            return DecodeLinear(fmt, data, wu, hu);

        int bpe = (int)fmt.BytesPerElem();
        int needBlocks = (int)(DivCeil(w, 4) * DivCeil(h, 4));
        if (data.Length < needBlocks * bpe)
        {
            var padded = new byte[needBlocks * bpe];
            data.AsSpan().CopyTo(padded);
            data = padded;
        }

        if (fmt is TtpfFormat.Bc6U or TtpfFormat.Bc6S)
            return DecodeHdr(data, wu, hu, fmt == TtpfFormat.Bc6S ? CompressionFormat.Bc6S : CompressionFormat.Bc6U);

        var cf = fmt switch
        {
            TtpfFormat.Bc1 => CompressionFormat.Bc1WithAlpha,
            TtpfFormat.Bc2 => CompressionFormat.Bc2,
            TtpfFormat.Bc3 => CompressionFormat.Bc3,
            TtpfFormat.Bc4 => CompressionFormat.Bc4,
            TtpfFormat.Bc5 => CompressionFormat.Bc5,
            TtpfFormat.Bc7 => CompressionFormat.Bc7,
            _ => CompressionFormat.Unknown,
        };

        var pixels = Decoder.DecodeRaw(data, wu, hu, cf);
        var rgba = new byte[wu * hu * 4];
        for (int i = 0; i < pixels.Length && i < wu * hu; i++)
        {
            var p = pixels[i];
            rgba[i * 4] = p.r;
            rgba[i * 4 + 1] = p.g;
            rgba[i * 4 + 2] = p.b;
            rgba[i * 4 + 3] = p.a;
        }
        return rgba;
    }

    private static byte[] DecodeHdr(byte[] data, int w, int h, CompressionFormat cf)
    {
        var px = Decoder.DecodeRawHdr(data, w, h, cf);
        int n = Math.Min(px.Length, w * h);
        float scale = Exposure(n, i => MathF.Max(px[i].r, MathF.Max(px[i].g, px[i].b)));
        var rgba = new byte[(long)w * h * 4];
        for (int i = 0; i < n; i++)
        {
            long o = (long)i * 4;
            rgba[o] = ToSrgb8(px[i].r * scale);
            rgba[o + 1] = ToSrgb8(px[i].g * scale);
            rgba[o + 2] = ToSrgb8(px[i].b * scale);
            rgba[o + 3] = 255;
        }
        return rgba;
    }

    private static byte[] DecodeHalf(TtpfFormat fmt, byte[] data, int w, int h)
    {
        long bpe = fmt.BytesPerElem();
        bool single = fmt == TtpfFormat.R16F;
        int n = (int)Math.Min((long)w * h, data.LongLength / bpe);

        float Channel(int i, int c) => Half16(data, i * bpe + c * 2);
        float Peak(int i) => single ? Channel(i, 0) : MathF.Max(Channel(i, 0), MathF.Max(Channel(i, 1), Channel(i, 2)));

        float scale = Exposure(n, Peak);
        var rgba = new byte[(long)w * h * 4];
        for (int i = 0; i < n; i++)
        {
            long o = (long)i * 4;
            if (single)
            {
                rgba[o] = rgba[o + 1] = rgba[o + 2] = ToSrgb8(Channel(i, 0) * scale);
            }
            else
            {
                rgba[o] = ToSrgb8(Channel(i, 0) * scale);
                rgba[o + 1] = ToSrgb8(Channel(i, 1) * scale);
                rgba[o + 2] = ToSrgb8(Channel(i, 2) * scale);
            }
            rgba[o + 3] = 255;
        }
        return rgba;
    }

    private static float Half16(byte[] b, long o) =>
        (float)BitConverter.UInt16BitsToHalf((ushort)(b[o] | (b[o + 1] << 8)));

    private static float Exposure(int n, Func<int, float> peakAt)
    {
        int step = Math.Max(1, n / 65536);
        var samples = new List<float>(n / step + 1);
        for (int i = 0; i < n; i += step)
        {
            float m = peakAt(i);
            if (float.IsFinite(m)) samples.Add(m);
        }
        if (samples.Count == 0) return 1f;
        samples.Sort();
        float peak = samples[(int)((samples.Count - 1) * 0.995f)];
        return peak > 1f ? 1f / peak : 1f;
    }

    private static byte ToSrgb8(float v)
    {
        if (!float.IsFinite(v) || v <= 0f) return 0;
        if (v >= 1f) return 255;
        float s = v <= 0.0031308f ? v * 12.92f : 1.055f * MathF.Pow(v, 1f / 2.4f) - 0.055f;
        return (byte)(s * 255f + 0.5f);
    }

    private static byte[] DecodeLinear(TtpfFormat fmt, byte[] data, int wu, int hu)
    {
        int bpe = (int)fmt.BytesPerElem();
        var outBuf = new byte[wu * hu * 4];
        Array.Fill(outBuf, (byte)255);
        for (int i = 0; i < wu * hu; i++)
        {
            int si = i * bpe;
            var o = outBuf.AsSpan(i * 4, 4);
            switch (fmt)
            {
                case TtpfFormat.Rgba8:
                    if (si + 4 <= data.Length) data.AsSpan(si, 4).CopyTo(o);
                    break;
                case TtpfFormat.Bgra8:
                    if (si + 4 <= data.Length)
                    {
                        o[0] = data[si + 2];
                        o[1] = data[si + 1];
                        o[2] = data[si];
                        o[3] = data[si + 3];
                    }
                    break;
                case TtpfFormat.R8:
                    if (si < data.Length) { o[0] = o[1] = o[2] = data[si]; }
                    break;
                case TtpfFormat.Rg8:
                    if (si + 2 <= data.Length) { o[0] = data[si]; o[1] = data[si + 1]; o[2] = 0; }
                    break;
                case TtpfFormat.Rgba16:
                    if (si + 8 <= data.Length)
                    {
                        o[0] = data[si + 1];
                        o[1] = data[si + 3];
                        o[2] = data[si + 5];
                        o[3] = data[si + 7];
                    }
                    break;
            }
        }
        return outBuf;
    }

    private static uint DivCeil(uint a, uint b) => (a + b - 1) / b;
}
