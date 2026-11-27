/* RAGEFORGE - Mesh Decode */

using System.Buffers.Binary;

namespace RageForge.Core.Model;

public readonly record struct Vec3(float X, float Y, float Z);
public readonly record struct Vec2(float U, float V);

public sealed record TerrainLayers(RageForge.Core.Ttpf.DecodedTexture Mask, RageForge.Core.Ttpf.DecodedTexture?[] Layers, List<Vec2>? LayerUv);

public sealed class DecodedMesh
{
    public required List<Vec3> Positions { get; init; }
    public List<Vec3>? Normals { get; set; }
    public List<(uint Slot, List<Vec2> Uv)> Uvs { get; init; } = [];
    public byte[]? Colors { get; set; }
    public byte[]? VertexAlpha { get; set; }

    public bool IsTranslucent => Pass is MaterialPass.Alpha or MaterialPass.Decal;

    public void KeepVertexAlpha()
    {
        if (!IsTranslucent || Colors is not { } colors || colors.Length != 4 * Positions.Count) return;
        var alpha = new byte[Positions.Count];
        for (int i = 0; i < alpha.Length; i++)
            alpha[i] = colors[4 * i + 3];
        VertexAlpha = alpha;
    }
    public TintPalette? Tint { get; set; }

    public byte[]? VertexTint { get; set; }

    public byte[]? TintIndex { get; set; }

    public int TintRows => Tint?.Rows ?? 0;

    public void KeepTintIndex()
    {
        if (Tint is null || Colors is not { } colors || colors.Length != 4 * Positions.Count) return;
        var index = new byte[Positions.Count];
        for (int i = 0; i < index.Length; i++)
            index[i] = colors[4 * i + 2];
        TintIndex = index;
    }

    public uint? TintColour(int row, int vertex)
    {
        if (Tint is { } palette && TintIndex is { } index && vertex < index.Length && index.Length == Positions.Count)
            return palette.Colour(row, index[vertex]);
        if (Tint is { } p && Colors is { } colors && colors.Length == 4 * Positions.Count && vertex < Positions.Count)
            return p.Colour(row, colors[4 * vertex + 2]);
        if (VertexTint is { } tint && tint.Length == 4 * Positions.Count && vertex < Positions.Count)
            return BitConverter.ToUInt32(tint, 4 * vertex);
        return null;
    }

    public void KeepVertexTint(int row = 0)
    {
        if (Tint is not { } palette || Colors is not { } colors || colors.Length != 4 * Positions.Count) return;
        var tint = new byte[colors.Length];
        for (int i = 0; i < Positions.Count; i++)
            BitConverter.TryWriteBytes(tint.AsSpan(4 * i, 4), palette.Colour(row, colors[4 * i + 2]));
        VertexTint = tint;
    }

    public required List<uint> Indices { get; set; }
    public int Flipped { get; set; }
    public int Dropped { get; set; }
    public int RebuiltNormals { get; set; }
    public int[] AxisOrder { get; set; } = [0, 1, 2];
    public List<ulong> TextureHashes { get; set; } = [];
    public ulong? TextureHash { get; set; }
    public RageForge.Core.Ttpf.DecodedTexture? Texture { get; set; }
    public bool TextureCutout { get; set; }
    public string? TextureName { get; set; }
    public RageForge.Core.Ttpf.DecodedTexture? NormalMap { get; set; }
    public TerrainMaterial? TerrainMaterial { get; set; }
    public uint Shader { get; set; }
    public bool TextureFallback { get; set; }
    public IReadOnlyList<(uint Param, ulong Hash)> TextureParams { get; set; } = [];
    public TerrainLayers? Terrain { get; set; }

    public void ReleaseGeometry()
    {
        Positions.Clear();
        Positions.TrimExcess();
        Indices.Clear();
        Indices.TrimExcess();
        Normals = null;
        Colors = null;
        VertexTint = null;
        TintIndex = null;
        Tint = null;
        Uvs.Clear();
        Texture = null;
        NormalMap = null;
        Terrain = null;
        VertexAlpha = null;
    }
    public ulong? NormalHash { get; set; }
    public MaterialPass Pass { get; set; }
}

public static class MeshDecode
{
    private static uint U32(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b[o..]);
    private static ulong U64(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt64LittleEndian(b[o..]);
    private static float F32(ReadOnlySpan<byte> b, int o) => BitConverter.UInt32BitsToSingle(U32(b, o));
    private static short I16(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadInt16LittleEndian(b[o..]);

    private static readonly int[][] Orders =
    [
        [0, 1, 2], [2, 0, 1], [1, 2, 0], [0, 2, 1], [2, 1, 0], [1, 0, 2],
    ];

    private static readonly uint[] UvSlots = [3, 4, 14, 15];

    public static List<Vec3> DecodePositions(ReadOnlySpan<byte> b, MeshBlobHeader h)
    {
        int n = (int)(h.VertexCount ?? 0);
        var v = new List<Vec3>(n);
        if (h.FloatPositions)
        {
            for (int i = 0; i < n; i++)
            {
                int o = 0x60 + 12 * i;
                if (o + 12 > b.Length) break;
                v.Add(new Vec3(F32(b, o), F32(b, o + 4), F32(b, o + 8)));
            }
            return v;
        }
        var bx = h.PositionBase;
        var sx = h.PositionStep;
        for (int i = 0; i < n; i++)
        {
            int o = 0x80 + 8 * i;
            if (o + 8 > b.Length) break;
            ulong q = U64(b, o);
            float x = (((q >> 16) & 0xffff) << 8 | (q & 0xff)) * sx[0] + bx[0];
            float y = (((q >> 32) & 0xffff) << 8 | ((q >> 8) & 0xff)) * sx[1] + bx[1];
            float z = (q >> 48) * sx[2] + bx[2];
            v.Add(new Vec3(x, y, z));
        }
        return v;
    }

    private static Vec3? OctaChecked(uint w)
    {
        if (w >> 31 == 0) return null;
        return Octa(w);
    }

    private static Vec3 Octa(uint w)
    {
        float x = (w & 1023) / 1023f * 2f - 1f;
        float y = ((w >> 10) & 1023) / 1023f * 2f - 1f;
        float z = 1f - MathF.Abs(x) - MathF.Abs(y);
        float t = MathF.Max(-z, 0f);
        x += x >= 0 ? -t : t;
        y += y >= 0 ? -t : t;
        float l = MathF.Max(MathF.Sqrt(x * x + y * y + z * z), 1e-12f);
        return new Vec3(x / l, y / l, z / l);
    }

    public static (List<uint> Indices, int Dropped) DecodeMeshlets(ReadOnlySpan<byte> ibSpan, int vertexCount)
    {
        byte[] ib = ibSpan.ToArray();
        int? U16(int o) => o + 2 <= ib.Length ? BinaryPrimitives.ReadUInt16LittleEndian(ib.AsSpan(o)) : null;
        if (U16(0) is not { } n || U16(4) is not { } ntri || U16(8) is not { } nremap0)
            return ([], 0);

        int tri0 = 0x0c + 16 * n;
        int rem0 = tri0 + 4 * ntri;
        var starts = new int[n];
        for (int k = 0; k < n; k++)
            starts[k] = U16(0x10 + 16 * k) ?? 0;
        int nremap = Math.Min(nremap0, Math.Max(0, ib.Length - rem0) / 2);

        var idx = new List<uint>(ntri * 3);
        int dropped = 0;
        for (int t = 0; t < ntri; t++)
        {
            if (tri0 + 4 * t + 4 > ib.Length) break;
            uint w = U32(ib, tri0 + 4 * t);
            int ml = (int)(w >> 21);
            if (ml >= starts.Length) { dropped++; continue; }
            int basei = starts[ml];
            uint t0 = 0, t1 = 0, t2 = 0;
            bool ok = true;
            for (int k = 0; k < 3; k++)
            {
                int rr = basei + (int)((w >> (7 * k)) & 0x7f);
                int? vv = rr < nremap ? U16(rem0 + 2 * rr) : null;
                if (vv is not { } val || val >= vertexCount) { ok = false; break; }
                if (k == 0) t0 = (uint)val; else if (k == 1) t1 = (uint)val; else t2 = (uint)val;
            }
            if (ok && t0 != t1 && t1 != t2 && t0 != t2)
            {
                idx.Add(t0);
                idx.Add(t1);
                idx.Add(t2);
            }
            else
                dropped++;
        }
        return (idx, dropped);
    }

    private static Vec3 Permute(Vec3 p, int[] o)
    {
        float x = 0, y = 0, z = 0;
        Set(o[0], p.X);
        Set(o[1], p.Y);
        Set(o[2], p.Z);
        return new Vec3(x, y, z);

        void Set(int k, float v)
        {
            if (k == 0) x = v; else if (k == 1) y = v; else z = v;
        }
    }

    private static Vec3 Face(Vec3 a, Vec3 b, Vec3 c)
    {
        float ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
        float wx = c.X - a.X, wy = c.Y - a.Y, wz = c.Z - a.Z;
        return new Vec3(uy * wz - uz * wy, uz * wx - ux * wz, ux * wy - uy * wx);
    }

    private static Vec3 Sum(Vec3 a, Vec3 b, Vec3 c) => new(a.X + b.X + c.X, a.Y + b.Y + c.Y, a.Z + b.Z + c.Z);

    private static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static DecodedMesh Decode(ReadOnlySpan<byte> blob, MeshBlobHeader h, ReadOnlySpan<byte> ib)
    {
        var positions = DecodePositions(blob, h);
        int v = positions.Count;

        (int Off, uint Fmt)? Stream(uint slot)
        {
            foreach (var (s, off, fmt) in h.Sections)
                if (s == slot) return ((int)off, fmt);
            return null;
        }

        var invalid = new List<int>();
        List<Vec3>? normals = null;
        if (Stream(2) is { Fmt: 0x21 } ns && ns.Off + 4 * v <= blob.Length)
        {
            normals = new List<Vec3>(v);
            for (int i = 0; i < v; i++)
            {
                uint w = U32(blob, ns.Off + 4 * i);
                var nn = OctaChecked(w);
                if (nn is { } val) normals.Add(val);
                else { invalid.Add(i); normals.Add(default); }
            }
        }

        var uvs = new List<(uint, List<Vec2>)>();
        foreach (var slot in UvSlots)
        {
            if (Stream(slot) is not { } st) continue;
            List<Vec2>? uv = null;
            if (st.Fmt == 0x28 && st.Off + 4 * v <= blob.Length)
            {
                uv = new List<Vec2>(v);
                for (int i = 0; i < v; i++)
                    uv.Add(new Vec2(I16(blob, st.Off + 4 * i) / 8192f, I16(blob, st.Off + 4 * i + 2) / 8192f));
            }
            else if (st.Fmt == 0x42 && st.Off + 8 * v <= blob.Length)
            {
                uv = new List<Vec2>(v);
                for (int i = 0; i < v; i++)
                    uv.Add(new Vec2(F32(blob, st.Off + 8 * i), F32(blob, st.Off + 8 * i + 4)));
            }
            if (uv is not null) uvs.Add((slot, uv));
        }

        byte[]? colors = null;
        if (Stream(1) is { Fmt: 0x26 } cs && cs.Off + 4 * v <= blob.Length)
            colors = blob.Slice(cs.Off, 4 * v).ToArray();

        var (indices, dropped) = DecodeMeshlets(ib, v);

        int mode = h.FloatPositions ? 2 : (0x6c + 4 <= blob.Length ? (int)U32(blob, 0x6c) : 2);
        int hinted = mode switch { 1 => 1, 0 => 4, _ => 0 };

        float Score(int[] o)
        {
            if (normals is null) return 0f;
            float sum = 0; int cnt = 0;
            int step = 1 + indices.Count / 3 / 4000;
            for (int ti = 0; ti + 2 < indices.Count; ti += 3 * step)
            {
                var f = Face(Permute(positions[(int)indices[ti]], o), Permute(positions[(int)indices[ti + 1]], o), Permute(positions[(int)indices[ti + 2]], o));
                var nn = Sum(normals[(int)indices[ti]], normals[(int)indices[ti + 1]], normals[(int)indices[ti + 2]]);
                float lf = MathF.Sqrt(Dot(f, f));
                float ln = MathF.Sqrt(Dot(nn, nn));
                if (lf > 0 && ln > 0) { sum += MathF.Abs(Dot(f, nn) / (lf * ln)); cnt++; }
            }
            return cnt == 0 ? 0f : sum / cnt;
        }

        int[] axisOrder = Orders[hinted];
        if (normals is not null)
        {
            float sh = Score(Orders[hinted]);
            int bi = hinted; float bs = sh;
            for (int i = 0; i < Orders.Length; i++)
            {
                float s = Score(Orders[i]);
                if (s > bs + 0.05f) { bs = s; bi = i; }
            }
            axisOrder = Orders[bi];
        }

        if (axisOrder[0] != 0 || axisOrder[1] != 1 || axisOrder[2] != 2)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                positions[i] = Permute(positions[i], axisOrder);
            }
        }

        int flipped = 0;
        if (normals is not null)
        {
            for (int ti = 0; ti + 2 < indices.Count; ti += 3)
            {
                var f = Face(positions[(int)indices[ti]], positions[(int)indices[ti + 1]], positions[(int)indices[ti + 2]]);
                float s = Dot(f, Sum(normals[(int)indices[ti]], normals[(int)indices[ti + 1]], normals[(int)indices[ti + 2]]));
                if (s < 0) { (indices[ti + 1], indices[ti + 2]) = (indices[ti + 2], indices[ti + 1]); flipped++; }
            }
        }

        int rebuilt = 0;
        if (normals is not null && invalid.Count > 0)
        {
            var acc = new Dictionary<(long, long, long), float[]>();
            (long, long, long) Key(Vec3 p) => ((long)MathF.Round(p.X * 1e4f), (long)MathF.Round(p.Y * 1e4f), (long)MathF.Round(p.Z * 1e4f));
            var invalidSet = new HashSet<int>(invalid);
            for (int ti = 0; ti + 2 < indices.Count; ti += 3)
            {
                var f = Face(positions[(int)indices[ti]], positions[(int)indices[ti + 1]], positions[(int)indices[ti + 2]]);
                for (int k = 0; k < 3; k++)
                {
                    int i = (int)indices[ti + k];
                    if (invalidSet.Contains(i))
                    {
                        var key = Key(positions[i]);
                        if (!acc.TryGetValue(key, out var e)) { e = new float[3]; acc[key] = e; }
                        e[0] += f.X; e[1] += f.Y; e[2] += f.Z;
                    }
                }
            }
            foreach (int i in invalid)
            {
                if (acc.TryGetValue(Key(positions[i]), out var g))
                {
                    float l = MathF.Sqrt(g[0] * g[0] + g[1] * g[1] + g[2] * g[2]);
                    if (l > 0) { normals[i] = new Vec3(g[0] / l, g[1] / l, g[2] / l); rebuilt++; }
                }
            }
        }

        return new DecodedMesh
        {
            Positions = positions,
            Normals = normals,
            Uvs = uvs.Select(x => (x.Item1, x.Item2)).ToList(),
            Colors = colors,
            Indices = indices,
            Flipped = flipped,
            Dropped = dropped,
            RebuiltNormals = rebuilt,
            AxisOrder = axisOrder,
        };
    }
}
