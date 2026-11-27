/* RAGEFORGE - PS09 Pipeline Cache */

using System.Buffers.Binary;
using System.Text;

namespace RageForge.Core.Shaders;

public enum ShaderStage
{
    Compute,
    Pixel,
    Vertex,
    Mesh,
    RayTracing,
    Unknown,
}

public readonly record struct ShaderBinding(string Kind, uint Offset, uint Slot, uint Dwords);

public sealed class ShaderEntry
{
    public required int Index { get; init; }
    public required int CodeOffset { get; init; }
    public required int CodeSize { get; init; }
    public required ShaderStage Stage { get; init; }
    public required int InstructionCount { get; init; }
    public required ulong PsoHash { get; init; }
    public (int X, int Y, int Z)? Threads { get; init; }
    public required List<ShaderBinding> Bindings { get; init; }

    public string StageTag => Stage switch
    {
        ShaderStage.Compute => "CS",
        ShaderStage.Pixel => "PS",
        ShaderStage.Vertex => "VS",
        ShaderStage.Mesh => "NGG",
        ShaderStage.RayTracing => "RT",
        _ => "??",
    };

    public string Display => $"#{Index:D5} {StageTag} {CodeSize,7:N0} B  {InstructionCount,5} insn";
}

public sealed class Ps09Cache
{
    private readonly byte[] _raw;

    public string Id { get; }
    public string Driver { get; }
    public int GraphicsPsos { get; }
    public int ComputePsos { get; }
    public int CodeBase { get; }
    public IReadOnlyList<ShaderEntry> Shaders { get; }

    private Ps09Cache(byte[] raw, string id, string driver, int gfx, int cs, int codeBase, List<ShaderEntry> shaders)
    {
        _raw = raw;
        Id = id;
        Driver = driver;
        GraphicsPsos = gfx;
        ComputePsos = cs;
        CodeBase = codeBase;
        Shaders = shaders;
    }

    public static bool HasMagic(ReadOnlySpan<byte> b) => b.Length >= 4 && b[..4].SequenceEqual("PS\0\t"u8);

    public ReadOnlySpan<byte> Code(ShaderEntry e)
    {
        int start = CodeBase + e.CodeOffset;
        if (start < 0 || start >= _raw.Length) return [];
        return _raw.AsSpan(start, Math.Min(e.CodeSize, _raw.Length - start));
    }

    private static uint U32(byte[] b, int o) => o + 4 <= b.Length ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o)) : 0;
    private static ulong U64(byte[] b, int o) => o + 8 <= b.Length ? BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(o)) : 0;

    private static string CStr(ReadOnlySpan<byte> b)
    {
        int end = b.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? b : b[..end]).Trim();
    }

    public static Ps09Cache? Parse(byte[] raw)
    {
        if (raw.Length < 0x80 || !HasMagic(raw))
            return null;

        int c0 = (int)U32(raw, 0x50), c1 = (int)U32(raw, 0x54), c2 = (int)U32(raw, 0x58), c3 = (int)U32(raw, 0x5c);
        int metaSize = (int)U32(raw, 0x60);
        int offC0 = 0x80;
        int offC1 = offC0 + c0 * 0x70;
        int offC2 = offC1 + c1 * 0x18;
        int offC3 = offC2 + c2 * 8;
        int offMeta = offC3 + c3 * 8;
        if (c0 < 0 || c1 < 0 || c2 < 0 || c3 < 0 || (long)offC0 + (long)c0 * 0x70 + (long)c1 * 0x18 + ((long)c2 + c3) * 8 > raw.Length)
            return null;
        int codeBase = LocateCodeBase(raw, offMeta + metaSize);

        var meta = new Dictionary<int, (ShaderStage? Stage, (int, int, int)? Threads, int? Bind, ulong Hash)>();
        for (int i = 0; i < c1; i++)
        {
            int p = offC1 + i * 0x18;
            if (p + 0x18 > raw.Length) break;
            int shader = (int)U32(raw, p + 16);
            int mRsrc = (int)U32(raw, p + 8);
            (int, int, int)? threads = null;
            if ((uint)mRsrc < (uint)c2)
            {
                int mo = (int)U32(raw, offC2 + mRsrc * 8);
                uint xy = U32(raw, offMeta + mo + 12), z = U32(raw, offMeta + mo + 16);
                int tx = (int)(xy & 0xFFFF), ty = (int)(xy >> 16), tz = (int)(z & 0xFFFF);
                if (tx is > 0 and <= 1024 && ty is > 0 and <= 1024 && tz is > 0 and <= 1024)
                    threads = (tx, ty, tz);
            }
            int csBind = (int)U32(raw, p + 20);
            meta.TryAdd(shader, (ShaderStage.Compute, threads, (uint)csBind < (uint)c2 ? csBind : null, U64(raw, p)));
        }
        for (int i = 0; i < c0; i++)
        {
            int p = offC0 + i * 0x70;
            if (p + 0x70 > raw.Length) break;
            for (int slot = 0; slot < 7; slot++)
            {
                uint shader = U32(raw, p + 0x38 + slot * 8);
                int bind = (int)U32(raw, p + 0x3c + slot * 8);
                if (shader != uint.MaxValue && shader < c3)
                    meta.TryAdd((int)shader, (null, null, (uint)bind < (uint)c2 ? bind : null, U64(raw, p)));
            }
        }

        var shaders = new List<ShaderEntry>(c3);
        for (int i = 0; i < c3; i++)
        {
            int so = (int)U32(raw, offC3 + i * 8);
            int ss = (int)Math.Min(U32(raw, offC3 + i * 8 + 4), int.MaxValue);
            int abs = codeBase + so;
            var code = so >= 0 && abs >= 0 && abs < raw.Length ? raw.AsSpan(abs, Math.Min(ss, raw.Length - abs)) : [];
            var (auto, insns) = Rdna2Disassembler.Classify(code);
            meta.TryGetValue(i, out var m);

            var bindings = new List<ShaderBinding>();
            if (m.Bind is { } mb && (uint)mb < (uint)c2)
            {
                int mo = (int)U32(raw, offC2 + mb * 8);
                int ms = (int)U32(raw, offC2 + mb * 8 + 4);
                int start = offMeta + mo;
                for (int k = start; k + 32 <= Math.Min(raw.Length, start + ms); )
                {
                    if (U32(raw, k) == 0x41)
                    {
                        string kind = U32(raw, k + 4) switch
                        {
                            0x0300 => "SRV", 0x0302 => "CBV", 0x0303 => "UAV", 0x0401 => "TABLE", 0x0002 => "SAMPLER", _ => "DESC",
                        };
                        bindings.Add(new ShaderBinding(kind, U32(raw, k + 8), U32(raw, k + 12), U32(raw, k + 16)));
                        k += 32;
                    }
                    else k += 4;
                }
            }

            var stage = auto is ShaderStage.RayTracing or ShaderStage.Mesh ? auto : m.Stage ?? auto;
            shaders.Add(new ShaderEntry
            {
                Index = i,
                CodeOffset = so,
                CodeSize = ss,
                Stage = stage,
                InstructionCount = insns,
                PsoHash = m.Hash,
                Threads = m.Threads,
                Bindings = bindings,
            });
        }

        return new Ps09Cache(raw, CStr(raw.AsSpan(4, 0x20)), CStr(raw.AsSpan(0x24, 0x20)), c0, c1, codeBase, shaders);
    }

    private static int LocateCodeBase(byte[] raw, int expected)
    {
        foreach (int delta in (int[])[0, -0x10000, 0x10000, -0x20000, 0x20000])
        {
            int cand = expected + delta;
            if (cand >= 0 && cand + 0x28 <= raw.Length
                && (U32(raw, cand + 0x18) == 0xD580_0000 || U32(raw, cand + 0x24) == 0xBFA0_0003))
                return cand;
        }
        return Math.Clamp(expected, 0, raw.Length);
    }
}
