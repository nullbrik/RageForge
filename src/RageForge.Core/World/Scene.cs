/* RAGEFORGE - Scene */

using System.Collections.Concurrent;
using System.Numerics;
using RageForge.Core.Carve;
using RageForge.Core.Index;
using RageForge.Core.Model;

namespace RageForge.Core.World;

public sealed record SceneModel(uint Archetype, string Name, IReadOnlyList<DecodedMesh> Meshes, Vector3 Center, float Radius)
{
    public int TintRows { get; init; }

    public int RowOf(uint tint) => TintRows > 0 ? (int)Math.Min(tint, (uint)TintRows - 1) : 0;
}

public readonly record struct SceneInstance(int Model, int Entity, Matrix4x4 Transform, int Tint = 0);

public sealed class Scene
{
    public required IReadOnlyList<SceneModel> Models { get; init; }
    public required IReadOnlyList<SceneInstance> Instances { get; init; }
    public MapPlacement? Placement { get; init; }
    public IReadOnlyList<SceneBatch> Batches { get; init; } = [];
    public IReadOnlyList<InstanceGroup> InstanceGroups { get; init; } = [];
    public IReadOnlyDictionary<(int Model, int Tint), List<SceneBatch>> ModelBatches { get; init; } = new Dictionary<(int Model, int Tint), List<SceneBatch>>();
    public required int MissingModels { get; init; }

    public static Matrix4x4 EntityTransform(in MapEntity e)
    {
        var q = e.Rotation;
        var rot = q == Quaternion.Identity || q.LengthSquared() < 1e-6f ? Quaternion.Identity : Quaternion.Normalize(Quaternion.Conjugate(q));
        return Matrix4x4.CreateScale(e.ScaleXY, e.ScaleXY, e.ScaleZ)
            * Matrix4x4.CreateFromQuaternion(rot)
            * Matrix4x4.CreateTranslation(e.Position);
    }

    public static Scene Build(
        MapPlacement placement,
        IReadOnlyDictionary<uint, ModelRef> models,
        string gameRoot,
        TextureCatalog? catalog,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        var scene = Build(placement.Entities, models, gameRoot, catalog, progress, ct);
        return new Scene { Models = scene.Models, Instances = scene.Instances, MissingModels = scene.MissingModels, Placement = placement, Batches = scene.Batches, InstanceGroups = scene.InstanceGroups, ModelBatches = scene.ModelBatches };
    }

    public static Scene Build(
        IReadOnlyList<MapEntity> entities,
        IReadOnlyDictionary<uint, ModelRef> models,
        string gameRoot,
        TextureCatalog? catalog,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default,
        Func<uint, float, bool>? keepModel = null,
        float batchCell = 200f)
    {
        entities = entities.Where(IsFinite).ToList();
        var archetypes = entities.Select(e => e.Archetype).Distinct().ToArray();
        var loaded = new ConcurrentDictionary<uint, SceneModel>();
        int done = 0, missing = 0;

        missing = archetypes.Count(h => !models.ContainsKey(h));
        var bySource = archetypes.Where(models.ContainsKey).GroupBy(h => models[h].Entry, ReferenceEqualityComparer.Instance).ToArray();
        var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = LoadParallelism };
        Parallel.ForEach(bySource, options, group =>
        {
            RageResource? r = null;
            try
            {
                var entry = models[group.First()].Entry;
                r = RageResource.Load(Path.Combine(gameRoot, entry.Archive), entry.ToCarved(), withGfx: true);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException)
            {
                Interlocked.Add(ref missing, group.Count());
            }
            if (r is null) return;
            foreach (var hash in group)
            {
                ct.ThrowIfCancellationRequested();
                var model = models[hash];
                try
                {
                    var meshes = Drawable.ExtractMeshes(r, catalog, model.Drawable);
                    meshes.RemoveAll(m => m.Pass == MaterialPass.Decal && m.Texture is null && m.Terrain is null);
                    foreach (var m in meshes)
                    {
                        m.KeepVertexAlpha();
                        m.KeepTintIndex();
                        m.Colors = null;
                        if (m.Uvs.Count > 1) m.Uvs.RemoveRange(1, m.Uvs.Count - 1);
                        m.TextureHashes = [];
                        m.Positions.TrimExcess();
                        m.Indices.TrimExcess();
                        m.Normals?.TrimExcess();
                        foreach (var (_, uv) in m.Uvs) uv.TrimExcess();
                    }
                    if (meshes.Count > 0)
                    {
                        var (center, radius) = Bounds(meshes);
                        if (keepModel is null || keepModel(hash, radius))
                            loaded[hash] = new SceneModel(hash, model.Name, meshes, center, radius) { TintRows = meshes.Max(x => x.TintRows) };
                    }
                    else Interlocked.Increment(ref missing);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException or NullReferenceException)
                {
                    Interlocked.Increment(ref missing);
                }

                int d = Interlocked.Increment(ref done);
                if (d % 8 == 0 || d == archetypes.Length)
                    progress?.Report((d, archetypes.Length));
            }
        });

        var list = loaded.Values.OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
        var indexOf = new Dictionary<uint, int>(list.Count);
        for (int i = 0; i < list.Count; i++)
            indexOf[list[i].Archetype] = i;

        var instances = new List<SceneInstance>(entities.Count);
        for (int i = 0; i < entities.Count; i++)
        {
            var e = entities[i];
            if (indexOf.TryGetValue(e.Archetype, out int m))
                instances.Add(new SceneInstance(m, i, EntityTransform(e), list[m].RowOf(e.Tint)));
        }

        var raw = new Scene { Models = list, Instances = instances, MissingModels = missing };
        ct.ThrowIfCancellationRequested();
        var batches = SceneBatcher.Build(raw, batchCell);
        var groups = SceneBatcher.Groups(raw, MathF.Max(150f, batchCell));
        var modelBatches = SceneBatcher.MergeModels(raw, groups.Select(g => (g.Model, g.Tint)).Distinct());
        foreach (var model in list)
            foreach (var mesh in model.Meshes)
                mesh.ReleaseGeometry();
        return new Scene { Models = list, Instances = instances, MissingModels = missing, Batches = batches, InstanceGroups = groups, ModelBatches = modelBatches };
    }

    private static int LoadParallelism => Math.Max(2, Environment.ProcessorCount / 2);

    public static bool IsFinite(MapEntity e) =>
        float.IsFinite(e.Position.X) && float.IsFinite(e.Position.Y) && float.IsFinite(e.Position.Z)
        && float.IsFinite(e.Rotation.X) && float.IsFinite(e.Rotation.Y) && float.IsFinite(e.Rotation.Z) && float.IsFinite(e.Rotation.W)
        && float.IsFinite(e.ScaleXY) && float.IsFinite(e.ScaleZ) && e.ScaleXY != 0 && e.ScaleZ != 0
        && MathF.Abs(e.Position.X) < 100_000 && MathF.Abs(e.Position.Y) < 100_000 && MathF.Abs(e.Position.Z) < 100_000;

    private static (Vector3 Center, float Radius) Bounds(IReadOnlyList<DecodedMesh> meshes)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var m in meshes)
            foreach (var p in m.Positions)
            {
                if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) continue;
                var v = new Vector3(p.X, p.Y, p.Z);
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
        if (min.X > max.X) return (Vector3.Zero, 0f);
        return ((min + max) * 0.5f, (max - min).Length() * 0.5f);
    }
}
