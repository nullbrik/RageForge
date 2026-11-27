/* RAGEFORGE - Scene Batcher */

using System.Collections.Concurrent;
using System.Numerics;
using RageForge.Core.Model;
using RageForge.Core.Ttpf;

namespace RageForge.Core.World;

public sealed class SceneBatch
{
    public required float[] Vertices { get; set; }
    public required uint[] Indices { get; set; }
    public DecodedTexture? Texture { get; set; }
    public DecodedTexture? Normal { get; set; }
    public DecodedTexture? Mask { get; set; }
    public DecodedTexture?[]? Layers { get; set; }
    public bool Cutout { get; init; }
    public bool Blend { get; init; }
    public bool Decal { get; init; }
    public bool Small { get; init; }
    public Vector3 Center { get; init; }
    public float Radius { get; init; }
    public int Triangles { get; init; }

    public void Release()
    {
        Vertices = [];
        Indices = [];
        Texture = null;
        Normal = null;
        Mask = null;
        Layers = null;
    }
}

public sealed record InstanceGroup(int Model, Matrix4x4[] Transforms, Vector3 Center, float Radius, bool Small, int Tint = 0);

public static class SceneBatcher
{
    public const long DuplicationBudget = 50_000;
    public const int Stride = 9;
    private const int MaxVertices = 1 << 16;

    private readonly record struct Key(int CellX, int CellY, bool Small, byte[]? Texture, byte[]? Normal, bool Cutout, bool Blend, bool Decal,
        byte[]? Mask, byte[]? Layer0, byte[]? Layer1, byte[]? Layer2, byte[]? Layer3);

    private sealed class KeyComparer : IEqualityComparer<Key>
    {
        public static readonly KeyComparer Instance = new();

        public bool Equals(Key a, Key b) =>
            a.CellX == b.CellX && a.CellY == b.CellY && a.Small == b.Small && ReferenceEquals(a.Texture, b.Texture)
            && ReferenceEquals(a.Normal, b.Normal) && a.Cutout == b.Cutout && a.Blend == b.Blend && a.Decal == b.Decal
            && ReferenceEquals(a.Mask, b.Mask) && ReferenceEquals(a.Layer0, b.Layer0) && ReferenceEquals(a.Layer1, b.Layer1)
            && ReferenceEquals(a.Layer2, b.Layer2) && ReferenceEquals(a.Layer3, b.Layer3);

        public int GetHashCode(Key k) => HashCode.Combine(HashCode.Combine(k.CellX, k.CellY, k.Small, Id(k.Texture), Id(k.Normal), k.Cutout, k.Blend, k.Decal),
            Id(k.Mask), Id(k.Layer0), Id(k.Layer1), Id(k.Layer2), Id(k.Layer3));

        private static int Id(byte[]? a) => a is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a);
    }

    private readonly record struct Part(DecodedMesh Mesh, Matrix4x4 Transform, List<Vec2>? Uv, List<Vec2>? Uv1, int Vertices, int Triangles, int Row)
    {
        public byte[]? Alpha => Mesh.VertexAlpha is { } a && a.Length == Vertices ? a : null;

        public float Tint(int vertex) => Mesh.TintColour(Row, vertex) is { } c ? BitConverter.UInt32BitsToSingle(c) : White;
    }

    private sealed class Group(Key key, DecodedTexture? texture, DecodedTexture? normal, TerrainLayers? terrain)
    {
        public readonly Key Key = key;
        public readonly DecodedTexture? Texture = texture;
        public readonly DecodedTexture? Normal = normal;
        public readonly TerrainLayers? Terrain = terrain;
        public readonly List<Part> Parts = [];
        public int Vertices;
    }

    private static (Key Key, DecodedTexture? Texture, DecodedTexture? Normal, TerrainLayers? Terrain, List<Vec2>? Uv, List<Vec2>? Uv1) KeyOf(
        int cellX, int cellY, bool small, DecodedMesh mesh)
    {
        int vc = mesh.Positions.Count;
        var uv = mesh.Uvs.Count > 0 && mesh.Uvs[0].Uv.Count == vc ? mesh.Uvs[0].Uv : null;
        var terrain = uv is not null && mesh.Terrain is { LayerUv: { } lu } t && lu.Count == vc ? t : null;
        var tex = uv is not null && terrain is null ? mesh.Texture : null;
        var nrm = uv is not null ? mesh.NormalMap : null;
        bool cutout = tex is not null && mesh.TextureCutout;
        bool blend = tex is not null && !cutout && mesh.Pass is MaterialPass.Alpha or MaterialPass.Decal;
        var layers = terrain?.Layers;
        var key = new Key(cellX, cellY, small, tex?.Rgba, nrm?.Rgba, cutout, blend, mesh.Pass == MaterialPass.Decal,
            terrain?.Mask.Rgba, Layer(layers, 0), Layer(layers, 1), Layer(layers, 2), Layer(layers, 3));
        return (key, tex, nrm, terrain, uv, terrain?.LayerUv);
    }

    private static byte[]? Layer(DecodedTexture?[]? layers, int k) => layers is not null && k < layers.Length ? layers[k]?.Rgba : null;

    public static bool IsInstanced(int instanceCount, long vertices) =>
        instanceCount >= 2 && vertices * (instanceCount - 1) > DuplicationBudget;

    private static HashSet<int> InstancedModels(Scene scene) =>
        scene.Instances.GroupBy(i => i.Model)
            .Where(g => IsInstanced(g.Count(), scene.Models[g.Key].Meshes.Sum(m => (long)m.Positions.Count)))
            .Select(g => g.Key)
            .ToHashSet();

    public static List<InstanceGroup> Groups(Scene scene, float cellSize = 150f, float smallRadius = 3f)
    {
        var instanced = InstancedModels(scene);
        var result = new List<InstanceGroup>();
        foreach (var g in scene.Instances
                     .Where(i => instanced.Contains(i.Model))
                     .GroupBy(i =>
                     {
                         var c = Vector3.Transform(scene.Models[i.Model].Center, i.Transform);
                         return (i.Model, i.Tint, (int)MathF.Floor(c.X / cellSize), (int)MathF.Floor(c.Y / cellSize));
                     }))
        {
            var model = scene.Models[g.Key.Model];
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            float maxScale = 0f;
            var transforms = new Matrix4x4[g.Count()];
            int k = 0;
            foreach (var inst in g)
            {
                var t = inst.Transform;
                transforms[k++] = t;
                float sc = MathF.Max(new Vector3(t.M11, t.M12, t.M13).Length(), new Vector3(t.M31, t.M32, t.M33).Length());
                maxScale = MathF.Max(maxScale, sc);
                var c = Vector3.Transform(model.Center, t);
                min = Vector3.Min(min, c);
                max = Vector3.Max(max, c);
            }
            float radius = (max - min).Length() * 0.5f + model.Radius * maxScale;
            result.Add(new InstanceGroup(g.Key.Model, transforms, (min + max) * 0.5f, radius, model.Radius * maxScale < smallRadius, g.Key.Tint));
        }
        return result;
    }

    public static List<SceneBatch> Build(Scene scene, float cellSize = 200f, float smallRadius = 3f)
    {
        var normals = new ConcurrentDictionary<DecodedMesh, Vector3[]>();
        var instanced = InstancedModels(scene);
        var perCell = scene.Instances
            .Where(i => !instanced.Contains(i.Model))
            .GroupBy(i =>
            {
                var c = Vector3.Transform(scene.Models[i.Model].Center, i.Transform);
                return ((int)MathF.Floor(c.X / cellSize), (int)MathF.Floor(c.Y / cellSize));
            })
            .ToList();

        var result = new ConcurrentBag<SceneBatch>();
        Parallel.ForEach(perCell, cell =>
        {
            var groups = new Dictionary<Key, List<Group>>(KeyComparer.Instance);
            foreach (var inst in cell)
            {
                var model = scene.Models[inst.Model];
                var t = inst.Transform;
                float scale = MathF.Max(new Vector3(t.M11, t.M12, t.M13).Length(), new Vector3(t.M31, t.M32, t.M33).Length());
                bool small = model.Radius * scale < smallRadius;
                foreach (var mesh in model.Meshes)
                {
                    int vc = mesh.Positions.Count;
                    if (vc == 0 || mesh.Indices.Count < 3) continue;
                    var (key, tex, nrm, terrain, uv, uv1) = KeyOf(cell.Key.Item1, cell.Key.Item2, small, mesh);
                    if (!groups.TryGetValue(key, out var list))
                        groups[key] = list = [new Group(key, tex, nrm, terrain)];
                    var g = list[^1];
                    if (g.Vertices + vc > MaxVertices)
                        list.Add(g = new Group(key, tex, nrm, terrain));
                    g.Parts.Add(new Part(mesh, t, uv, uv1, vc, mesh.Indices.Count / 3, inst.Tint));
                    g.Vertices += vc;
                }
            }
            foreach (var list in groups.Values)
                foreach (var g in list)
                    if (g.Vertices > 0)
                        result.Add(Fill(g, normals));
        });
        return [.. result];
    }

    public static Dictionary<(int Model, int Tint), List<SceneBatch>> MergeModels(Scene scene, IEnumerable<(int Model, int Tint)> variants)
    {
        var normals = new ConcurrentDictionary<DecodedMesh, Vector3[]>();
        var result = new ConcurrentDictionary<(int Model, int Tint), List<SceneBatch>>();
        Parallel.ForEach(variants, variant =>
        {
            var (mi, row) = variant;
            var groups = new Dictionary<Key, List<Group>>(KeyComparer.Instance);
            foreach (var mesh in scene.Models[mi].Meshes)
            {
                int vc = mesh.Positions.Count;
                if (vc == 0 || mesh.Indices.Count < 3) continue;
                var (key, tex, nrm, terrain, uv, uv1) = KeyOf(0, 0, false, mesh);
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = [new Group(key, tex, nrm, terrain)];
                var g = list[^1];
                if (g.Vertices + vc > MaxVertices)
                    list.Add(g = new Group(key, tex, nrm, terrain));
                g.Parts.Add(new Part(mesh, Matrix4x4.Identity, uv, uv1, vc, mesh.Indices.Count / 3, row));
                g.Vertices += vc;
            }
            result[variant] = [.. groups.Values.SelectMany(l => l).Where(g => g.Vertices > 0).Select(g => Fill(g, normals))];
        });
        return new Dictionary<(int Model, int Tint), List<SceneBatch>>(result);
    }

    public static readonly float White = BitConverter.UInt32BitsToSingle(0xffff_ffff);

    public static float PackNormal(Vector3 n, byte alpha)
    {
        static uint Snorm(float v) => (uint)(byte)(sbyte)Math.Clamp(MathF.Round(v * 127f), -127f, 127f);
        uint bits = Snorm(n.X) | Snorm(n.Y) << 8 | Snorm(n.Z) << 16 | (uint)(alpha * 127 / 255) << 24;
        return BitConverter.UInt32BitsToSingle(bits);
    }

    private static SceneBatch Fill(Group g, ConcurrentDictionary<DecodedMesh, Vector3[]> normals)
    {
        long vertexCount = g.Parts.Sum(p => (long)p.Vertices);
        long triangleCount = g.Parts.Sum(p => (long)p.Triangles);
        var vertices = new float[vertexCount * Stride];
        var indices = new uint[triangleCount * 3];
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        int vo = 0, io = 0;
        uint baseIndex = 0;
        foreach (var part in g.Parts)
        {
            var mesh = part.Mesh;
            var t = part.Transform;
            var n = normals.GetOrAdd(mesh, MeshNormals.Compute);
            var normalMatrix = Matrix4x4.Invert(t, out var inverse) ? Matrix4x4.Transpose(inverse) : t;
            for (int i = 0; i < part.Vertices; i++)
            {
                var p = Vector3.Transform(new Vector3(mesh.Positions[i].X, mesh.Positions[i].Y, mesh.Positions[i].Z), t);
                var nn = Vector3.TransformNormal(n[i], normalMatrix);
                float len = nn.Length();
                if (len > 1e-12f) nn /= len;
                vertices[vo++] = p.X;
                vertices[vo++] = p.Y;
                vertices[vo++] = p.Z;
                vertices[vo++] = PackNormal(nn, part.Alpha is { } alpha ? alpha[i] : (byte)255);
                vertices[vo++] = part.Uv is null ? 0f : part.Uv[i].U;
                vertices[vo++] = part.Uv is null ? 0f : part.Uv[i].V;
                vertices[vo++] = part.Uv1 is null ? 0f : part.Uv1[i].U;
                vertices[vo++] = part.Uv1 is null ? 0f : part.Uv1[i].V;
                vertices[vo++] = part.Tint(i);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            var ix = mesh.Indices;
            uint vc = (uint)part.Vertices;
            for (int k = 0; k + 2 < ix.Count; k += 3)
            {
                if (ix[k] >= vc || ix[k + 1] >= vc || ix[k + 2] >= vc) continue;
                indices[io++] = baseIndex + ix[k];
                indices[io++] = baseIndex + ix[k + 1];
                indices[io++] = baseIndex + ix[k + 2];
            }
            baseIndex += vc;
        }
        if (io < indices.Length)
            Array.Resize(ref indices, io);

        return new SceneBatch
        {
            Vertices = vertices,
            Indices = indices,
            Texture = g.Texture,
            Normal = g.Normal,
            Mask = g.Terrain?.Mask,
            Layers = g.Terrain?.Layers,
            Cutout = g.Key.Cutout,
            Blend = g.Key.Blend,
            Decal = g.Key.Decal,
            Small = g.Key.Small,
            Center = (min + max) * 0.5f,
            Radius = (max - min).Length() * 0.5f,
            Triangles = io / 3,
        };
    }
}
