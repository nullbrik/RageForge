/* RAGEFORGE - Carved Resource Classification */

using System.Buffers.Binary;

namespace RageForge.Core.Carve;

public enum ResourceKind
{
    Resource,
    RageObj,
    Xml,
    Rbf,
    Pso,
    Gfx,
    Dds,
    Png,
    Text,
    Bin,
    Audio,
    Video,
    Shader,
}

public static class Classify
{
    public static bool StartsNewFile(ResourceKind k) => k is
        ResourceKind.Resource or ResourceKind.RageObj or ResourceKind.Pso or
        ResourceKind.Rbf or ResourceKind.Gfx or ResourceKind.Dds or ResourceKind.Png or ResourceKind.Shader;

    private static ReadOnlySpan<byte> Bom => [0xEF, 0xBB, 0xBF];
    private static ReadOnlySpan<byte> PngMagic => [0x89, 0x50, 0x4E, 0x47];

    public static (ResourceKind Kind, string Sub, string Ext) Of(ReadOnlySpan<byte> h)
    {
        var (kind, tag) = KindOf(h);
        var (sub, ext) = Describe(kind, tag);
        return (kind, sub, ext);
    }

    public static (string Sub, string Ext) Describe(ResourceKind kind, uint tag) => kind switch
    {
        ResourceKind.Xml => ("", "xml"),
        ResourceKind.Rbf => ("", "rbf"),
        ResourceKind.Shader => ("ps09", "psocache"),
        ResourceKind.Pso => ("", "pso"),
        ResourceKind.Gfx => ("", "gfx"),
        ResourceKind.Dds => ("", "dds"),
        ResourceKind.Png => ("", "png"),
        ResourceKind.Resource => (tag.ToString("x"), $"r{tag:x}"),
        ResourceKind.RageObj => ("", "rsc"),
        ResourceKind.Text => ("", "txt"),
        _ => (tag.ToString("x8"), "bin"),
    };

    public static (ResourceKind Kind, uint Tag) KindOf(ReadOnlySpan<byte> h)
    {
        var body = h.StartsWith(Bom) ? h[3..] : h;
        if (body.StartsWith("<?xml"u8) || (body.Length > 1 && body[0] == (byte)'<' && IsAlpha(body[1])))
            return (ResourceKind.Xml, 0);
        if (h.StartsWith("RBF0"u8)) return (ResourceKind.Rbf, 0);
        if (h.StartsWith("PS\0\t"u8)) return (ResourceKind.Shader, 0);
        if (h.StartsWith("PSIN"u8)) return (ResourceKind.Pso, 0);
        if (h.StartsWith("GFX"u8) || h.StartsWith("CFX"u8)) return (ResourceKind.Gfx, 0);
        if (h.StartsWith("DDS "u8)) return (ResourceKind.Dds, 0);
        if (h.StartsWith(PngMagic)) return (ResourceKind.Png, 0);

        if (h.Length >= 0x18 && h.Slice(0x10, 4).SequenceEqual("0DRP"u8))
            return (ResourceKind.Resource, U32(h, 0x14));

        uint p = U32(h, 8);
        if (h.Length >= 16 && (p & 0xF000_0000) == 0x5000_0000 && U32(h, 12) == 0)
            return (ResourceKind.RageObj, 0);

        int cap = Math.Min(128, h.Length);
        int printable = 0;
        for (int i = 0; i < cap; i++)
        {
            byte c = h[i];
            if (c is (byte)'\n' or (byte)'\r' or (byte)'\t' || (c >= 0x20 && c < 0x7f))
                printable++;
        }
        if (h.Length >= 32 && printable * 100 / cap > 95)
            return (ResourceKind.Text, 0);

        return (ResourceKind.Bin, U32(h, 0));
    }

    private static bool IsAlpha(byte c) => (c >= (byte)'A' && c <= (byte)'Z') || (c >= (byte)'a' && c <= (byte)'z');

    private static uint U32(ReadOnlySpan<byte> b, int o) =>
        b.Length < o + 4 ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(b[o..]);
}
