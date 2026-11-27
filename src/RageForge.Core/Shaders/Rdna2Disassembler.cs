/* RAGEFORGE - RDNA2 Disassembler */

using System.Buffers.Binary;
using System.Text;

namespace RageForge.Core.Shaders;

public enum InsnEncoding
{
    Sop2, Sopk, Sop1, Sopc, Sopp, Smem, Vop2, Vop1, Vopc, Vop3, Vop3P, Vintrp, Ds, Flat, Mubuf, Mtbuf, Mimg, Exp, Unknown,
}

public sealed record Instruction(int Offset, uint[] Words, InsnEncoding Encoding, uint Opcode, string Mnemonic, string Operands)
{
    public override string ToString()
    {
        var hex = string.Join(' ', Words.Select(w => w.ToString("x8")));
        return $"{Offset,6:x4}:  {hex,-35} {Mnemonic,-24} {Operands}";
    }
}

public static class Rdna2Disassembler
{
    private static readonly string[] Xyzw = ["x", "xy", "xyz", "xyzw"];

    private static uint W(ReadOnlySpan<byte> c, int o) => o + 4 <= c.Length ? BinaryPrimitives.ReadUInt32LittleEndian(c[o..]) : 0;

    public static ReadOnlySpan<byte> SkipPrologue(ReadOnlySpan<byte> code) =>
        code.Length >= 0x28 && W(code, 0x18) == 0xD580_0000 && W(code, 0x1c) == 0x03FE_0100 ? code[0x24..] : code;

    public static InsnEncoding Identify(uint w0)
    {
        if ((w0 & 0x8000_0000) == 0)
            return (w0 >> 25) switch { 0x3F => InsnEncoding.Vop1, 0x3E => InsnEncoding.Vopc, _ => InsnEncoding.Vop2 };
        if ((w0 >> 30) == 0b10)
            return (w0 >> 23) switch
            {
                0x17D => InsnEncoding.Sop1,
                0x17E => InsnEncoding.Sopc,
                0x17F => InsnEncoding.Sopp,
                _ => (w0 >> 28) == 0xB ? InsnEncoding.Sopk : InsnEncoding.Sop2,
            };
        return (w0 >> 26) switch
        {
            0x32 => InsnEncoding.Vintrp,
            0x33 => InsnEncoding.Vop3P,
            0x35 => InsnEncoding.Vop3,
            0x36 => InsnEncoding.Ds,
            0x37 => InsnEncoding.Flat,
            0x38 => InsnEncoding.Mubuf,
            0x3A => InsnEncoding.Mtbuf,
            0x3C => InsnEncoding.Mimg,
            0x3D => InsnEncoding.Smem,
            0x3E => InsnEncoding.Exp,
            _ => InsnEncoding.Unknown,
        };
    }

    public static int Length(ReadOnlySpan<byte> code, int i)
    {
        uint w0 = W(code, i);
        int len = Identify(w0) switch
        {
            InsnEncoding.Sop2 => 4 + ((w0 & 0xFF) == 255 || ((w0 >> 8) & 0xFF) == 255 ? 4 : 0),
            InsnEncoding.Sopk => 4 + (((w0 >> 23) & 0x1F) == 21 ? 4 : 0),
            InsnEncoding.Sop1 => 4 + ((w0 & 0xFF) == 255 ? 4 : 0),
            InsnEncoding.Sopc => 4 + ((w0 & 0xFF) == 255 || ((w0 >> 8) & 0xFF) == 255 ? 4 : 0),
            InsnEncoding.Sopp or InsnEncoding.Vintrp => 4,
            InsnEncoding.Vop1 or InsnEncoding.Vopc => 4 + VopExtra(w0),
            InsnEncoding.Vop2 => 4 + Math.Max(VopExtra(w0), ((w0 >> 25) & 0x3F) is 32 or 33 or 44 or 45 or 55 or 56 ? 4 : 0),
            InsnEncoding.Vop3 or InsnEncoding.Vop3P => 8 + (HasLiteral3(W(code, i + 4)) ? 4 : 0),
            InsnEncoding.Mimg => 8 + (int)((w0 >> 1) & 3) * 4,
            InsnEncoding.Unknown => 4,
            _ => 8,
        };
        return i + len <= code.Length ? len : Math.Max(4, code.Length - i);
    }

    private static int VopExtra(uint w0) => (w0 & 0x1FF) is 255 or 250 or 233 or 249 ? 4 : 0;

    private static bool HasLiteral3(uint w1) =>
        (w1 & 0x1FF) == 255 || ((w1 >> 9) & 0x1FF) == 255 || ((w1 >> 18) & 0x1FF) == 255;

    public static List<Instruction> Decode(ReadOnlySpan<byte> code, int maxInstructions = int.MaxValue)
    {
        var list = new List<Instruction>();
        int i = 0;
        while (i + 4 <= code.Length && list.Count < maxInstructions)
        {
            int len = Length(code, i);
            var words = new uint[len / 4];
            for (int k = 0; k < words.Length; k++) words[k] = W(code, i + 4 * k);
            var insn = Format(i, words);
            list.Add(insn);
            i += len;
            if (insn.Mnemonic is "s_endpgm" or "s_code_end") break;
        }
        return list;
    }

    public static (ShaderStage Stage, int Instructions) Classify(ReadOnlySpan<byte> code)
    {
        var body = SkipPrologue(code);
        bool mrt = false, pos = false, prim = false, rt = false, interp = false;
        int count = 0;
        for (int i = 0; i + 4 <= body.Length; )
        {
            uint w0 = W(body, i);
            count++;
            if (w0 == 0xBF81_0000 || w0 == 0xBF9F_0000) break;
            switch (Identify(w0))
            {
                case InsnEncoding.Exp:
                    uint tgt = (w0 >> 4) & 0x3F;
                    if (tgt <= 8) mrt = true;
                    else if (tgt is >= 12 and <= 15) pos = true;
                    else if (tgt == 20) prim = true;
                    break;
                case InsnEncoding.Mimg:
                    if (((w0 >> 18) & 0x7F) is 0x66 or 0x67) rt = true;
                    break;
                case InsnEncoding.Vintrp:
                    interp = true;
                    break;
            }
            i += Length(body, i);
        }
        var stage = rt ? ShaderStage.RayTracing : prim ? ShaderStage.Mesh
            : mrt || interp ? ShaderStage.Pixel : pos ? ShaderStage.Vertex : ShaderStage.Compute;
        return (stage, count);
    }


    private static string Src(uint s, uint literal)
    {
        if (s >= 256) return $"v{s - 256}";
        if (s <= 105) return $"s{s}";
        if (s is >= 129 and <= 192) return (s - 128).ToString();
        if (s is >= 193 and <= 208) return (-(int)(s - 192)).ToString();
        return s switch
        {
            106 => "vcc_lo", 107 => "vcc_hi", 124 => "m0", 125 => "null", 126 => "exec_lo", 127 => "exec_hi",
            128 => "0", 235 => "src_shared_base", 236 => "src_shared_limit", 237 => "src_private_base",
            238 => "src_private_limit", 239 => "src_pops_exiting_wave_id", 240 => "0.5", 241 => "-0.5",
            242 => "1.0", 243 => "-1.0", 244 => "2.0", 245 => "-2.0", 246 => "4.0", 247 => "-4.0",
            248 => "0.15915494", 233 => "dpp8", 249 => "sdwa", 250 => "dpp", 251 => "vccz", 252 => "execz",
            253 => "scc", 254 => "lds_direct", 255 => $"0x{literal:x}",
            _ => $"src{s}",
        };
    }

    private static string SReg(uint s, int count) =>
        count <= 1 || s > 105 ? Src(s, 0) : $"s[{s}:{s + count - 1}]";

    private static string VReg(uint v, int count) => count <= 1 ? $"v{v}" : $"v[{v}:{v + count - 1}]";

    private static string VSrc0(uint w0, uint w1, uint lit) => (w0 & 0x1FF) switch
    {
        249 => ((w1 >> 23) & 1) != 0 ? $"s{w1 & 0xFF}" : $"v{w1 & 0xFF}",
        250 => $"v{w1 & 0xFF}",
        233 => $"v{w1 & 0xFF}",
        var s => Src(s, lit),
    };

    private static int Width(string mnemonic) =>
        mnemonic.EndsWith("_b64", StringComparison.Ordinal) || mnemonic.EndsWith("_u64", StringComparison.Ordinal) || mnemonic.EndsWith("_i64", StringComparison.Ordinal) || mnemonic.EndsWith("_f64", StringComparison.Ordinal) ? 2 : 1;


    private static Instruction Format(int offset, uint[] w)
    {
        uint w0 = w[0];
        uint w1 = w.Length > 1 ? w[1] : 0;
        uint lit = w.Length > 0 ? w[^1] : 0;
        var enc = Identify(w0);
        uint op = 0;
        string name, ops;

        switch (enc)
        {
            case InsnEncoding.Sop2:
            {
                op = (w0 >> 23) & 0x7F;
                name = Lookup(Tables.Sop2, op, "s_sop2");
                int wd = Width(name);
                string S(uint s) => s == 255 ? $"0x{lit:x}" : SReg(s, wd);
                ops = $"{SReg((w0 >> 16) & 0x7F, wd)}, {S(w0 & 0xFF)}, {S((w0 >> 8) & 0xFF)}";
                break;
            }
            case InsnEncoding.Sopk:
                op = (w0 >> 23) & 0x1F;
                name = Lookup(Tables.Sopk, op, "s_sopk");
                ops = op == 21
                    ? $"hwreg(0x{w0 & 0xFFFF:x}), 0x{lit:x}"
                    : $"{Src((w0 >> 16) & 0x7F, 0)}, 0x{w0 & 0xFFFF:x}";
                break;
            case InsnEncoding.Sop1:
            {
                op = (w0 >> 8) & 0xFF;
                name = Lookup(Tables.Sop1, op, "s_sop1");
                int wd = Width(name) == 2 || name.EndsWith("saveexec_b64", StringComparison.Ordinal) || name.Contains("pc_b64") ? 2 : 1;
                ops = $"{SReg((w0 >> 16) & 0x7F, wd)}, {((w0 & 0xFF) == 255 ? $"0x{lit:x}" : SReg(w0 & 0xFF, wd))}";
                break;
            }
            case InsnEncoding.Sopc:
            {
                op = (w0 >> 16) & 0x7F;
                name = Lookup(Tables.Sopc, op, "s_sopc");
                int wd = Width(name);
                ops = $"{((w0 & 0xFF) == 255 ? $"0x{lit:x}" : SReg(w0 & 0xFF, wd))}, {(((w0 >> 8) & 0xFF) == 255 ? $"0x{lit:x}" : SReg((w0 >> 8) & 0xFF, wd))}";
                break;
            }
            case InsnEncoding.Sopp:
                op = (w0 >> 16) & 0x7F;
                name = Lookup(Tables.Sopp, op, "s_sopp");
                ops = Sopp(op, (short)(w0 & 0xFFFF), offset);
                break;
            case InsnEncoding.Smem:
            {
                op = (w0 >> 18) & 0xFF;
                name = Lookup(Tables.Smem, op, "s_smem");
                int n = name.EndsWith("x16", StringComparison.Ordinal) ? 16 : name.EndsWith("x8", StringComparison.Ordinal) ? 8 : name.EndsWith("x4", StringComparison.Ordinal) ? 4 : name.EndsWith("x2", StringComparison.Ordinal) ? 2 : 1;
                uint sbase = (w0 & 0x3F) * 2;
                int baseN = name.StartsWith("s_buffer", StringComparison.Ordinal) ? 4 : 2;
                int off = (int)(w1 << 11) >> 11;
                uint soff = (w1 >> 25) & 0x7F;
                ops = name is "s_memtime" or "s_memrealtime" or "s_dcache_inv" or "s_gl1_inv"
                    ? (name.StartsWith("s_mem", StringComparison.Ordinal) ? SReg((w0 >> 6) & 0x7F, 2) : string.Empty)
                    : $"{SReg((w0 >> 6) & 0x7F, n)}, {SReg(sbase, baseN)}, 0x{off:x}" + (soff != 125 ? $", {Src(soff, 0)}" : "");
                break;
            }
            case InsnEncoding.Vop2:
                op = (w0 >> 25) & 0x3F;
                name = Lookup(Tables.Vop2, op, "v_vop2");
                ops = $"v{(w0 >> 17) & 0xFF}, {VSrc0(w0, w1, lit)}, v{(w0 >> 9) & 0xFF}" + (op is 32 or 33 or 44 or 45 or 55 or 56 ? $", 0x{lit:x}" : "");
                if (op is 1 or 40 or 41 or 42) ops += ", vcc_lo";
                break;
            case InsnEncoding.Vop1:
                op = (w0 >> 9) & 0xFF;
                name = Lookup(Tables.Vop1, op, "v_vop1");
                ops = op == 0 ? string.Empty : $"v{(w0 >> 17) & 0xFF}, {VSrc0(w0, w1, lit)}";
                break;
            case InsnEncoding.Vopc:
                op = (w0 >> 17) & 0xFF;
                name = Tables.VopcName(op);
                ops = $"vcc_lo, {VSrc0(w0, w1, lit)}, v{(w0 >> 9) & 0xFF}";
                break;
            case InsnEncoding.Vop3:
                op = (w0 >> 16) & 0x3FF;
                (name, ops) = Vop3(op, w0, w1, lit);
                break;
            case InsnEncoding.Vop3P:
            {
                op = (w0 >> 16) & 0x7F;
                name = Lookup(Tables.Vop3P, op, "v_vop3p");
                int nsrc = name.Contains("fma") || name.Contains("mad") || name.Contains("dot") ? 3 : 2;
                ops = $"v{w0 & 0xFF}, " + string.Join(", ", Enumerable.Range(0, nsrc).Select(k => Src((w1 >> (9 * k)) & 0x1FF, lit)));
                break;
            }
            case InsnEncoding.Vintrp:
            {
                op = (w0 >> 16) & 3;
                name = op switch { 0 => "v_interp_p1_f32", 1 => "v_interp_p2_f32", _ => "v_interp_mov_f32" };
                char chan = "xyzw"[(int)((w0 >> 8) & 3)];
                uint attr = (w0 >> 10) & 0x3F;
                ops = op == 2
                    ? $"v{(w0 >> 18) & 0xFF}, {(w0 & 0xFF) switch { 0 => "p10", 1 => "p20", _ => "p0" }}, attr{attr}.{chan}"
                    : $"v{(w0 >> 18) & 0xFF}, v{w0 & 0xFF}, attr{attr}.{chan}";
                break;
            }
            case InsnEncoding.Ds:
            {
                op = (w0 >> 18) & 0xFF;
                name = Lookup(Tables.Ds, op, "ds_op");
                uint addr = w1 & 0xFF, d0 = (w1 >> 8) & 0xFF, d1 = (w1 >> 16) & 0xFF, vdst = w1 >> 24;
                bool read = name.Contains("read") || name.Contains("permute") || name.Contains("swizzle");
                ops = read ? $"v{vdst}, v{addr}" : $"v{addr}, v{d0}" + (name.Contains("2") ? $", v{d1}" : "");
                ops += $" offset0:{w0 & 0xFF} offset1:{(w0 >> 8) & 0xFF}";
                break;
            }
            case InsnEncoding.Flat:
            {
                op = (w0 >> 18) & 0x7F;
                uint seg = (w0 >> 14) & 3;
                string prefix = seg switch { 1 => "scratch_", 2 => "global_", _ => "flat_" };
                name = prefix + Lookup(Tables.Flat, op, "op");
                int off = (int)(w0 << 21) >> 21;
                uint addr = w1 & 0xFF, data = (w1 >> 8) & 0xFF, saddr = (w1 >> 16) & 0x7F, vdst = w1 >> 24;
                string sa = saddr == 0x7D || seg == 0 ? "off" : SReg(saddr, 2);
                ops = name.Contains("load") || name.Contains("atomic")
                    ? $"v{vdst}, {VReg(addr, saddr == 0x7D ? 2 : 1)}, {sa} offset:{off}"
                    : $"{VReg(addr, saddr == 0x7D ? 2 : 1)}, v{data}, {sa} offset:{off}";
                break;
            }
            case InsnEncoding.Mubuf:
            {
                op = (w0 >> 18) & 0x7F;
                name = Lookup(Tables.Mubuf, op, "buffer_op");
                ops = $"v{(w1 >> 8) & 0xFF}, v{w1 & 0xFF}, {SReg(((w1 >> 16) & 0x1F) * 4, 4)}, {Src(w1 >> 24, 0)} offset:{w0 & 0xFFF}"
                    + ((w0 & 0x1000) != 0 ? " offen" : "") + ((w0 & 0x2000) != 0 ? " idxen" : "") + ((w0 & 0x4000) != 0 ? " glc" : "");
                break;
            }
            case InsnEncoding.Mtbuf:
            {
                op = ((w0 >> 16) & 7) | (((w1 >> 21) & 1) << 3);
                name = op < 4 ? $"tbuffer_load_format_{Xyzw[op]}" : op < 8 ? $"tbuffer_store_format_{Xyzw[op - 4]}" : $"tbuffer_op{op}";
                ops = $"v{(w1 >> 8) & 0xFF}, v{w1 & 0xFF}, {SReg(((w1 >> 16) & 0x1F) * 4, 4)}, {Src(w1 >> 24, 0)} offset:{w0 & 0xFFF} format:{(w0 >> 19) & 0x7F}";
                break;
            }
            case InsnEncoding.Mimg:
            {
                op = (w0 >> 18) & 0x7F;
                name = Lookup(Tables.Mimg, op, "image_op");
                uint dmask = (w0 >> 8) & 0xF;
                int comps = Math.Max(1, System.Numerics.BitOperations.PopCount(dmask));
                if (op is 0x66 or 0x67) comps = 4;
                string dim = ((w0 >> 3) & 7) switch { 0 => "1D", 1 => "2D", 2 => "3D", 3 => "CUBE", 4 => "1D_ARRAY", 5 => "2D_ARRAY", 6 => "2D_MSAA", _ => "2D_MSAA_ARRAY" };
                ops = $"{VReg((w1 >> 8) & 0xFF, comps)}, v{w1 & 0xFF}, {SReg(((w1 >> 16) & 0x1F) * 4, 8)}"
                    + (name.Contains("sample") || name.Contains("gather") || name.Contains("lod") ? $", {SReg(((w1 >> 21) & 0x1F) * 4, 4)}" : "")
                    + $" dmask:0x{dmask:x} dim:{dim}";
                break;
            }
            case InsnEncoding.Exp:
            {
                name = "exp";
                uint tgt = (w0 >> 4) & 0x3F, en = w0 & 0xF;
                string target = tgt switch
                {
                    <= 7 => $"mrt{tgt}", 8 => "mrtz", 9 => "null", >= 12 and <= 15 => $"pos{tgt - 12}", 20 => "prim",
                    >= 32 and <= 63 => $"param{tgt - 32}", _ => $"target{tgt}",
                };
                var v = Enumerable.Range(0, 4).Select(k => (en >> k & 1) != 0 ? $"v{(w1 >> (8 * k)) & 0xFF}" : "off");
                ops = $"{target} {string.Join(", ", v)}" + ((w0 & 0x800) != 0 ? " done" : "") + ((w0 & 0x1000) != 0 ? " vm" : "") + ((w0 & 0x400) != 0 ? " compr" : "");
                break;
            }
            default:
                name = ".word";
                ops = $"0x{w0:x8}";
                break;
        }

        return new Instruction(offset, w, enc, op, name, ops);
    }

    private static string Sopp(uint op, short simm, int offset) => op switch
    {
        1 or 10 or 27 or 30 or 31 => string.Empty,
        2 or 4 or 5 or 6 or 7 or 8 or 9 => $"0x{offset + 4 + simm * 4:x}",
        12 => Waitcnt((ushort)simm),
        _ => $"0x{(ushort)simm:x}",
    };

    private static string Waitcnt(ushort s)
    {
        int vm = (s & 0xF) | ((s >> 14) & 3) << 4, exp = (s >> 4) & 7, lgkm = (s >> 8) & 0x3F;
        var parts = new List<string>();
        if (vm != 63) parts.Add($"vmcnt({vm})");
        if (exp != 7) parts.Add($"expcnt({exp})");
        if (lgkm != 63) parts.Add($"lgkmcnt({lgkm})");
        return parts.Count == 0 ? "0" : string.Join(' ', parts);
    }

    private static (string, string) Vop3(uint op, uint w0, uint w1, uint lit)
    {
        uint vdst = w0 & 0xFF, abs = (w0 >> 8) & 7, neg = (w1 >> 29) & 7;
        bool clamp = (w0 & 0x8000) != 0;
        string Mod(int k)
        {
            string s = Src((w1 >> (9 * k)) & 0x1FF, lit);
            if ((abs >> k & 1) != 0) s = $"|{s}|";
            if ((neg >> k & 1) != 0) s = "-" + s;
            return s;
        }

        string name;
        int nsrc;
        string dst;
        if (op < 0x100)
        {
            name = Tables.VopcName(op);
            nsrc = 2;
            dst = Src(vdst, 0);
        }
        else if (op < 0x140)
        {
            name = Lookup(Tables.Vop2, op - 0x100, "v_vop2");
            nsrc = name.Contains("fma") || name.Contains("cndmask") || name.Contains("_co_ci") ? 3 : 2;
            dst = $"v{vdst}";
        }
        else if (op is >= 0x180 and < 0x200)
        {
            name = Lookup(Tables.Vop1, op - 0x180, "v_vop1");
            nsrc = 1;
            dst = $"v{vdst}";
        }
        else
        {
            name = Lookup(Tables.Vop3, op, "v_vop3");
            nsrc = Tables.Vop3TwoSource.Contains(op) ? 2 : 3;
            dst = op is 0x30f or 0x310 or 0x319 or 0x16d or 0x16e or 0x176 or 0x177
                ? $"v{vdst}, {Src((w0 >> 8) & 0x7F, 0)}"
                : $"v{vdst}";
        }
        var srcs = string.Join(", ", Enumerable.Range(0, nsrc).Select(Mod));
        return (name, $"{dst}, {srcs}" + (clamp ? " clamp" : ""));
    }

    private static string Lookup(Dictionary<uint, string> table, uint op, string fallback) =>
        table.TryGetValue(op, out var n) ? n : $"{fallback}_{op}";


    public static string Listing(ShaderEntry e, ReadOnlySpan<byte> code)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"; shader #{e.Index}  stage {e.Stage}  size {e.CodeSize} bytes  pso 0x{e.PsoHash:x16}");
        if (e.Threads is { } t) sb.AppendLine($"; numthreads({t.X}, {t.Y}, {t.Z})");
        foreach (var b in e.Bindings) sb.AppendLine($"; {b.Kind,-8} slot {b.Slot}  offset 0x{b.Offset:x}  dwords {b.Dwords}");
        sb.AppendLine();
        var body = SkipPrologue(code);
        int skipped = code.Length - body.Length;
        foreach (var insn in Decode(body))
            sb.AppendLine((insn with { Offset = insn.Offset + skipped }).ToString());
        return sb.ToString();
    }

    public static string PseudoHlsl(ShaderEntry e, ReadOnlySpan<byte> code)
    {
        var sb = new StringBuilder();
        string entry = e.Stage switch { ShaderStage.Pixel => "PSMain", ShaderStage.Vertex => "VSMain", ShaderStage.Mesh => "MSMain", _ => "CSMain" };
        if (e.Threads is { } t) sb.AppendLine($"[numthreads({t.X}, {t.Y}, {t.Z})]");
        sb.AppendLine($"void {entry}()  // shader #{e.Index}, lifted from GFX10.3 ISA");
        sb.AppendLine("{");
        foreach (var insn in Decode(SkipPrologue(code)))
            sb.AppendLine("    " + Lift(insn));
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Lift(Instruction i)
    {
        var a = i.Operands.Split(", ", StringSplitOptions.TrimEntries);
        string A(int k) => k < a.Length ? a[k].Split(' ')[0] : "?";
        return i.Mnemonic switch
        {
            "s_endpgm" => "return;",
            "v_mov_b32" or "s_mov_b32" or "s_mov_b64" => $"{A(0)} = {A(1)};",
            "v_add_f32" or "v_add_nc_u32" or "s_add_u32" or "s_add_i32" or "v_add_f16" => $"{A(0)} = {A(1)} + {A(2)};",
            "v_sub_f32" or "v_sub_nc_u32" or "s_sub_u32" => $"{A(0)} = {A(1)} - {A(2)};",
            "v_subrev_f32" or "v_subrev_nc_u32" => $"{A(0)} = {A(2)} - {A(1)};",
            "v_mul_f32" or "v_mul_legacy_f32" or "v_mul_lo_u32" or "s_mul_i32" or "v_mul_u32_u24" => $"{A(0)} = {A(1)} * {A(2)};",
            "v_fma_f32" or "v_mad_u32_u24" or "v_mad_legacy_f32" => $"{A(0)} = {A(1)} * {A(2)} + {A(3)};",
            "v_fmac_f32" or "v_fmac_legacy_f32" => $"{A(0)} += {A(1)} * {A(2)};",
            "v_min_f32" or "v_min_i32" or "v_min_u32" => $"{A(0)} = min({A(1)}, {A(2)});",
            "v_max_f32" or "v_max_i32" or "v_max_u32" => $"{A(0)} = max({A(1)}, {A(2)});",
            "v_med3_f32" => $"{A(0)} = clamp({A(1)}, {A(2)}, {A(3)});",
            "v_rcp_f32" => $"{A(0)} = 1.0 / {A(1)};",
            "v_rsq_f32" => $"{A(0)} = rsqrt({A(1)});",
            "v_sqrt_f32" => $"{A(0)} = sqrt({A(1)});",
            "v_exp_f32" => $"{A(0)} = exp2({A(1)});",
            "v_log_f32" => $"{A(0)} = log2({A(1)});",
            "v_sin_f32" => $"{A(0)} = sin({A(1)} * 6.2831853);",
            "v_cos_f32" => $"{A(0)} = cos({A(1)} * 6.2831853);",
            "v_floor_f32" => $"{A(0)} = floor({A(1)});",
            "v_ceil_f32" => $"{A(0)} = ceil({A(1)});",
            "v_fract_f32" => $"{A(0)} = frac({A(1)});",
            "v_trunc_f32" => $"{A(0)} = trunc({A(1)});",
            "v_cvt_f32_u32" or "v_cvt_f32_i32" => $"{A(0)} = float({A(1)});",
            "v_cvt_u32_f32" => $"{A(0)} = uint({A(1)});",
            "v_cvt_i32_f32" => $"{A(0)} = int({A(1)});",
            "v_and_b32" or "s_and_b32" or "s_and_b64" => $"{A(0)} = {A(1)} & {A(2)};",
            "v_or_b32" or "s_or_b32" or "s_or_b64" => $"{A(0)} = {A(1)} | {A(2)};",
            "v_xor_b32" or "s_xor_b32" => $"{A(0)} = {A(1)} ^ {A(2)};",
            "v_lshlrev_b32" => $"{A(0)} = {A(2)} << {A(1)};",
            "v_lshrrev_b32" => $"{A(0)} = {A(2)} >> {A(1)};",
            "s_lshl_b32" => $"{A(0)} = {A(1)} << {A(2)};",
            "s_lshr_b32" => $"{A(0)} = {A(1)} >> {A(2)};",
            "v_cndmask_b32" => $"{A(0)} = vcc ? {A(2)} : {A(1)};",
            "v_cvt_pkrtz_f16_f32" => $"{A(0)} = pack_half2({A(1)}, {A(2)});",
            "exp" => $"OUT.{i.Operands.Split(' ')[0]} = ({string.Join(", ", i.Operands.Split(' ', 2)[1].Split(", ").Select(s => s.Split(' ')[0]))});",
            _ when i.Mnemonic.StartsWith("v_cmp_", StringComparison.Ordinal) => $"vcc = {A(1)} {CmpOp(i.Mnemonic)} {A(2)};",
            _ when i.Mnemonic.StartsWith("image_sample", StringComparison.Ordinal) => $"{A(0)} = Tex[{A(2)}].Sample(Samp[{A(3)}], {A(1)});",
            _ when i.Mnemonic.StartsWith("image_load", StringComparison.Ordinal) => $"{A(0)} = Tex[{A(2)}].Load({A(1)});",
            _ when i.Mnemonic.StartsWith("s_buffer_load", StringComparison.Ordinal) || i.Mnemonic.StartsWith("s_load", StringComparison.Ordinal) => $"{A(0)} = cbuffer[{A(1)}][{A(2)}];",
            _ when i.Mnemonic.StartsWith("v_interp", StringComparison.Ordinal) => $"{A(0)} = interpolate({string.Join(", ", a.Skip(1))});",
            _ => $"// {i.Mnemonic} {i.Operands}",
        };
    }

    private static string CmpOp(string m) => m.Split('_')[2] switch
    {
        "lt" => "<", "le" => "<=", "gt" => ">", "ge" => ">=", "eq" => "==", "ne" or "lg" or "neq" => "!=", _ => "?",
    };
}
