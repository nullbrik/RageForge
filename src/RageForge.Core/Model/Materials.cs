/* RAGEFORGE - Materials */

using RageForge.Core.Carve;
using RageForge.Core.Ttpf;

namespace RageForge.Core.Model;

public enum MaterialPass : byte
{
    Opaque,
    Alpha,
    Decal,
    Cutout,
}

public sealed record MeshMaterial(DecodedTexture? Texture, bool Cutout, ulong? StreamHash, string? EmbeddedName, List<ulong> Hashes, MaterialPass Pass)
{
    public static readonly MeshMaterial None = new(null, false, null, null, [], MaterialPass.Opaque);

    public DecodedTexture? Normal { get; init; }
    public ulong? NormalHash { get; init; }
    public TerrainMaterial? Terrain { get; init; }
    public uint Shader { get; init; }
    public bool Fallback { get; init; }
    public IReadOnlyList<(uint Param, ulong Hash)> Params { get; init; } = [];
    public ulong? TintKey { get; init; }
}

public sealed record TerrainMaterial(ulong Mask, ulong[] Layers);

public static class Materials
{
    private const uint DiffuseTex = 0xc8e8_c282;
    private const uint DiffuseTex2 = 0x0df4_7048;
    private const uint BodyTex = 0x3463_94ea;
    private const uint EmissiveTex = 0x8e0d_63ee;
    private const ulong StreamedRef = 0x980;
    private const int MinSize = 8;

    private static bool IsColorFormat(uint dxgi) => dxgi is not (65 or 80 or 81 or 83 or 84);

    private static bool IsNormalFormat(uint dxgi) => dxgi is 82 or 83 or 84;

    private static readonly HashSet<uint> NormalSlots = [0x1e77_bf4c, 0x84c6_9350, 0x58b6_e707, 0x4d56_d7fe, 0x2bb2_ede8];

    private static bool IsSrgbFormat(uint dxgi) => dxgi is 29 or 72 or 75 or 78 or 91 or 93 or 99;

    private const int SrgbRank = 4;

    private static (int ArrayOff, int Count) FindShaderArray(RageResource r)
    {
        bool IsShader(ulong p) =>
            r.SysOff(p) is { } o && o + 8 <= r.Sys.Length && r.Sys.AsSpan(o + 4, 4).SequenceEqual("atem"u8);

        int bestOff = 0, bestCount = 0;
        int off = 0;
        while (off + 8 <= r.Sys.Length)
        {
            int n = 0;
            while (off + 8 * (n + 1) <= r.Sys.Length && IsShader(r.U64(off + 8 * n)))
                n++;
            if (n > bestCount) { bestCount = n; bestOff = off; }
            off += 8 * Math.Max(n, 1);
        }
        return (bestOff, bestCount);
    }

    private static int? FindEmbeddedDictionary(RageResource r, int shaderArray, int count)
    {
        for (int sg = 0; sg + 0x20 <= r.Sys.Length; sg += 8)
        {
            if (r.SysOff(r.U64(sg + 0x10)) != shaderArray || r.U16(sg + 0x18) != count) continue;
            return r.SysOff(r.U64(sg + 8));
        }
        return null;
    }

    public static List<MeshMaterial> Resolve(RageResource r, List<Geometry> geoms, TextureCatalog? catalog, int? shaderGroup = null)
    {
        var result = Enumerable.Repeat(MeshMaterial.None, geoms.Count).ToList();
        var (arrayOff, count) = shaderGroup is { } sg && sg + 0x20 <= r.Sys.Length && r.SysOff(r.U64(sg + 0x10)) is { } array
            ? (array, (int)r.U16(sg + 0x18))
            : FindShaderArray(r);
        if (count == 0) return result;

        var embedded = FindEmbeddedDictionary(r, arrayOff, count) is { } txd
            ? EmbeddedTextures.Read(r, txd)
            : [];
        uint maxDim = catalog?.MaxDimension ?? 0;

        var shaders = new MeshMaterial[count];
        for (int k = 0; k < count; k++)
            shaders[k] = r.SysOff(r.U64(arrayOff + 8 * k)) is { } s ? ResolveShader(r, s, embedded, catalog, maxDim) : MeshMaterial.None;

        var shaderOf = GeometryShaderIndex(r, geoms, count);
        for (int gi = 0; gi < geoms.Count; gi++)
            if (shaderOf[gi] is { } si)
                result[gi] = shaders[si];
        return result;
    }

    private static MeshMaterial ResolveShader(RageResource r, int s, Dictionary<int, EmbeddedTexture> embedded, TextureCatalog? catalog, uint maxDim)
    {
        var pass = (s + 0x3c < r.Sys.Length ? r.Sys[s + 0x3c] : 0) switch
        {
            2 => MaterialPass.Alpha,
            4 => MaterialPass.Decal,
            8 => MaterialPass.Cutout,
            _ => MaterialPass.Opaque,
        };
        if (r.SysOff(r.U64(s + 0x20)) is not { } pars || r.SysOff(r.U64(s + 0x10)) is not { } slots || pars + 8 > r.Sys.Length)
            return MeshMaterial.None with { Pass = pass };

        var hashes = new List<ulong>();
        var ranked = new (EmbeddedTexture? Emb, ulong? Hash)[SrgbRank + 1];
        EmbeddedTexture? normalEmb = null;
        ulong? normalHash = null;
        bool normalBySlot = false;
        ulong? tintKey = null;
        bool declaresColor = false;
        var streamed = new List<(uint Param, ulong Hash, uint Dxgi, uint Width)>();
        int n = pars + 1 < r.Sys.Length ? r.Sys[pars + 1] : 0;
        for (int k = 0; k < n; k++)
        {
            int ent = pars + 8 + 8 * k;
            if (ent + 8 > r.Sys.Length) break;
            int rank = r.U32(ent) switch
            {
                DiffuseTex => 0,
                BodyTex => 1,
                DiffuseTex2 => 2,
                EmissiveTex => 3,
                _ => -1,
            };
            declaresColor |= rank >= 0;
            uint code = r.U32(ent + 4);
            long slot = slots + (long)code * 2;
            if (code % 4 != 0 || slot + 8 > r.Sys.Length) continue;
            if (r.SysOff(r.U64((int)slot)) is not { } tp) continue;

            if (embedded.TryGetValue(tp, out var emb))
            {
                bool embSlot = NormalSlots.Contains(r.U32(ent)) && !IsSrgbFormat(emb.Dxgi);
                if (emb.Width > MinSize && emb.Height > MinSize
                    && (embSlot && !normalBySlot || normalEmb is null && normalHash is null && IsNormalFormat(emb.Dxgi)))
                {
                    normalEmb = emb;
                    normalHash = null;
                    normalBySlot |= embSlot;
                }
                if (rank < 0 && IsSrgbFormat(emb.Dxgi)) rank = SrgbRank;
                if (rank >= 0 && ranked[rank].Emb is null && ranked[rank].Hash is null && IsColorFormat(emb.Dxgi)
                    && emb.Width > MinSize && emb.Height > MinSize)
                    ranked[rank] = (emb, null);
                continue;
            }
            if (tp + 0x38 > r.Sys.Length) continue;
            if (r.SysOff(r.U64(tp + 0x30)) is not { } reference || reference + 0x14 > r.Sys.Length) continue;
            if (r.U64(tp + 0x10) != StreamedRef && r.U32(reference + 0x10) != 6) continue;
            ulong hash = r.U64(reference + 8);
            hashes.Add(hash);
            if (r.U32(ent) == TintPalette.Param) tintKey ??= hash;
            if (catalog?.Find(hash) is not { } info || info.Width <= MinSize || info.Height <= MinSize) continue;
            streamed.Add((r.U32(ent), hash, info.DxgiFormat, info.Width));
            bool hashSlot = NormalSlots.Contains(r.U32(ent)) && !IsSrgbFormat(info.DxgiFormat);
            if (hashSlot && !normalBySlot || normalEmb is null && normalHash is null && IsNormalFormat(info.DxgiFormat))
            {
                normalHash = hash;
                normalEmb = null;
                normalBySlot |= hashSlot;
            }
            if (rank < 0 && IsSrgbFormat(info.DxgiFormat)) rank = SrgbRank;
            if (rank < 0 || ranked[rank].Emb is not null || ranked[rank].Hash is not null || !IsColorFormat(info.DxgiFormat)) continue;
            ranked[rank] = (null, hash);
        }

        var normal = normalEmb?.Decode(maxDim);
        uint shader = r.U32(s);
        var textureParams = streamed.Select(t => (t.Param, t.Hash)).ToArray();
        if (!declaresColor && TerrainOf(streamed) is { } terrain)
            return new MeshMaterial(null, false, null, null, hashes, pass) { Normal = normal, NormalHash = normalHash, Terrain = terrain, Shader = shader, Params = textureParams, TintKey = tintKey };
        if (declaresColor)
            ranked[SrgbRank] = default;
        for (int k = 0; k < ranked.Length; k++)
        {
            var (emb, hash) = ranked[k];
            bool fallback = k == SrgbRank;
            if (emb is not null && emb.Decode(maxDim) is { } tex)
                return new MeshMaterial(tex, IsCutout(pass, tex), null, emb.Name, hashes, pass) { Normal = normal, NormalHash = normalHash, Shader = shader, Fallback = fallback, Params = textureParams, TintKey = tintKey };
            if (hash is { } h)
                return new MeshMaterial(null, false, h, null, hashes, pass) { Normal = normal, NormalHash = normalHash, Shader = shader, Fallback = fallback, Params = textureParams, TintKey = tintKey };
        }
        return new MeshMaterial(null, false, null, null, hashes, pass) { Normal = normal, NormalHash = normalHash, Shader = shader, Params = textureParams, TintKey = tintKey };
    }

    private const uint BlendMaskFormat = 28;
    private const uint BlendMaskParam = 0x0cd6_179f;
    private const int MaxTerrainLayers = 4;

    private static TerrainMaterial? TerrainOf(List<(uint Param, ulong Hash, uint Dxgi, uint Width)> streamed)
    {
        var mask = streamed.FindIndex(t => t.Param == BlendMaskParam && t.Width >= 64);
        if (mask < 0) mask = streamed.FindIndex(t => t.Dxgi == BlendMaskFormat && t.Width >= 64);
        if (mask < 0) return null;
        var layers = streamed.Where(t => IsSrgbFormat(t.Dxgi)).Select(t => t.Hash).Distinct().Take(MaxTerrainLayers).ToArray();
        return layers.Length >= 2 ? new TerrainMaterial(streamed[mask].Hash, layers) : null;
    }

    public static IEnumerable<string> Describe(RageResource r, TextureCatalog? catalog)
    {
        var (arrayOff, count) = FindShaderArray(r);
        var txd = count > 0 ? FindEmbeddedDictionary(r, arrayOff, count) : null;
        var embedded = txd is { } t ? EmbeddedTextures.Read(r, t) : [];
        yield return $"shaders {count}, embedded dictionary {(txd is null ? "none" : $"0x{txd:x}")} ({embedded.Count} textures)";
        for (int k = 0; k < count; k++)
        {
            if (r.SysOff(r.U64(arrayOff + 8 * k)) is not { } s) continue;
            yield return $"shader {k} name 0x{r.U32(s):x8} pass {(s + 0x3c < r.Sys.Length ? r.Sys[s + 0x3c] : 0)}";
            if (r.SysOff(r.U64(s + 0x20)) is not { } pars || r.SysOff(r.U64(s + 0x10)) is not { } slots) continue;
            int n = pars + 1 < r.Sys.Length ? r.Sys[pars + 1] : 0;
            for (int i = 0; i < n; i++)
            {
                int ent = pars + 8 + 8 * i;
                uint name = r.U32(ent), code = r.U32(ent + 4);
                string what = "value";
                if (code % 4 == 0 && slots + (long)code * 2 + 8 <= r.Sys.Length && r.SysOff(r.U64(slots + (int)code * 2)) is { } tp)
                {
                    if (embedded.TryGetValue(tp, out var e))
                        what = $"embedded '{e.Name}' dxgi {e.Dxgi} {e.Width}x{e.Height}";
                    else if (tp + 0x38 <= r.Sys.Length && r.SysOff(r.U64(tp + 0x30)) is { } rf && rf + 0x14 <= r.Sys.Length)
                    {
                        ulong h = r.U64(rf + 8);
                        var info = catalog?.Find(h);
                        what = $"ref tag 0x{r.U64(tp + 0x10):x} kind {r.U32(rf + 0x10)} hash {h:x16} -> {(info is null ? "missing" : $"{info.FormatName} {info.Width}x{info.Height}")}";
                    }
                    else
                        what = $"ptr 0x{tp:x}";
                }
                else if (slots + (long)code * 2 + 16 <= r.Sys.Length)
                {
                    int v = slots + (int)code * 2;
                    what = $"value ({r.F32(v):0.####}, {r.F32(v + 4):0.####}, {r.F32(v + 8):0.####}, {r.F32(v + 12):0.####})";
                }
                yield return $"  param 0x{name:x8} code {code}: {what}";
            }
        }
    }

    public static bool IsCutout(MaterialPass pass, DecodedTexture tex) =>
        pass == MaterialPass.Cutout || (pass is MaterialPass.Alpha or MaterialPass.Decal && Imaging.AlphaAnalysis.IsCutout(tex.Rgba));

    private static int?[] GeometryShaderIndex(RageResource r, List<Geometry> geoms, int shaderCount)
    {
        var result = new int?[geoms.Count];
        var byRec = new Dictionary<int, int>();
        for (int i = 0; i < geoms.Count; i++)
            byRec[(int)geoms[i].RecordOff - 8] = i;

        for (int o = 0; o + 0x30 <= r.Sys.Length; o += 8)
        {
            int n = r.U16(o + 8);
            if (n == 0 || n != r.U16(o + 10)
                || r.SysOff(r.U64(o)) is not { } a
                || r.SysOff(r.U64(o + 0x18)) is not { } m)
                continue;
            for (int k = 0; k < n; k++)
            {
                if (r.SysOff(r.U64(a + 8 * k)) is not { } rec || !byRec.TryGetValue(rec, out int gi) || result[gi] is not null)
                    continue;
                int si = r.U16(m + 2 * k);
                if (si < shaderCount)
                    result[gi] = si;
            }
        }
        if (shaderCount == 1)
            for (int i = 0; i < result.Length; i++)
                result[i] ??= 0;
        return result;
    }
}
