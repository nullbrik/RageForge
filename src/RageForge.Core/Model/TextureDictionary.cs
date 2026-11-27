/* RAGEFORGE - Texture Dictionary */

using RageForge.Core.Carve;
using RageForge.Core.Ttpf;

namespace RageForge.Core.Model;

public static class TextureDictionary
{
    public static (string Name, int Count)? Probe(ReadOnlySpan<byte> sys)
    {
        if (sys.Length < 0x40) return null;
        ulong hashPtr = U64(sys, 0x20), texPtr = U64(sys, 0x30);
        int hashCount = U16(sys, 0x28), count = U16(sys, 0x38), cap = U16(sys, 0x3a);
        if (SysOff(hashPtr, sys.Length) is null || SysOff(texPtr, sys.Length) is not { } arr) return null;
        if (count is < 1 or > 2048 || count != hashCount || count > cap || arr + 8 > sys.Length) return null;
        if (SysOff(U64(sys, arr), sys.Length) is not { } tp || tp + 0x44 > sys.Length) return null;
        if (U64(sys, tp + 0x18) >> 28 != 6) return null;
        uint w = U16(sys, tp + 0x38), h = U16(sys, tp + 0x3a);
        if (w is < 4 or > 8192 || h is < 4 or > 8192 || TtpfFormats.FromDxgi(sys[tp + 0x3f]) == TtpfFormat.Unknown) return null;

        string name = $"txd_{count}tex";
        if (SysOff(U64(sys, tp + 0x30), sys.Length) is { } no)
        {
            var s = sys[no..];
            if (s.Length >= 8 && s[..8].IndexOfAnyExcept((byte)0) < 0) s = s[8..];
            int end = s.IndexOf((byte)0);
            if (end is >= 2 and < 96 && s[..end].IndexOfAnyExceptInRange((byte)0x21, (byte)0x7e) < 0)
                name = System.Text.Encoding.ASCII.GetString(s[..end]);
        }
        return (name, count);
    }

    public static Dictionary<int, EmbeddedTexture> Read(RageResource r) => EmbeddedTextures.Read(r, 0);

    public static IReadOnlyList<(int TexOff, ulong Hash)> Hashes(RageResource r)
    {
        var list = new List<(int, ulong)>();
        if (r.SysOff(r.U64(0x20)) is not { } h || r.SysOff(r.U64(0x30)) is not { } arr) return list;
        int n = r.U16(0x38);
        for (int i = 0; i < n; i++)
            if (r.SysOff(r.U64(arr + 8 * i)) is { } tp)
                list.Add((tp, r.U64(h + 8 * i)));
        return list;
    }

    private static int? SysOff(ulong p, int len) =>
        p >> 28 == 5 && (int)(p & 0x0FFF_FFFF) < len ? (int)(p & 0x0FFF_FFFF) : null;

    private static ulong U64(ReadOnlySpan<byte> b, int o) => o + 8 <= b.Length ? System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(b[o..]) : 0;
    private static ushort U16(ReadOnlySpan<byte> b, int o) => o + 2 <= b.Length ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(b[o..]) : (ushort)0;
}
