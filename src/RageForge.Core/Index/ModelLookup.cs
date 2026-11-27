/* RAGEFORGE - Model Lookup */

using RageForge.Core.Hashing;
using RageForge.Core.World;

namespace RageForge.Core.Index;

public readonly record struct ModelRef(IndexEntry Entry, int? Drawable)
{
    public string Name => Drawable is null ? Entry.Name : $"{Entry.Name}:{Drawable:x}";
}

public sealed class ModelLookup
{
    private ModelLookup(Dictionary<uint, ModelRef> models, Dictionary<uint, ArchetypeDef> archetypes)
    {
        Models = models;
        Archetypes = archetypes;
    }

    public static ModelLookup Empty { get; } = new([], []);

    public IReadOnlyDictionary<uint, ModelRef> Models { get; }

    public IReadOnlyDictionary<uint, ArchetypeDef> Archetypes { get; }

    public static ModelLookup Build(IEnumerable<IndexEntry> all)
    {
        var models = new Dictionary<uint, ModelRef>();
        var archetypes = new Dictionary<uint, ArchetypeDef>();
        var list = all as IReadOnlyCollection<IndexEntry> ?? all.ToList();
        foreach (var e in list)
        {
            if (e.Children is { } children)
                foreach (var (hash, drawable) in children)
                    models.TryAdd(hash, new ModelRef(e, drawable));
            if (e.Archetypes is { } defs)
                foreach (var def in defs)
                    archetypes.TryAdd(def.Name, def);
        }
        foreach (var e in list.Where(e => e.Tag == "dr" && e.Children is null))
            models.TryAdd(Joaat.Hash(e.Name), new ModelRef(e, null));
        foreach (var e in list.Where(e => e.Tag == "ft"))
            models.TryAdd(Joaat.Hash(e.Name), new ModelRef(e, null));
        return new ModelLookup(models, archetypes);
    }

    public float LodDistance(in MapEntity e) =>
        e.LodDistance > 0 ? e.LodDistance : Archetypes.TryGetValue(e.Archetype, out var def) && def.LodDistance > 0 ? def.LodDistance : 0;

    public bool IsLodParent(in MapEntity e) =>
        e.HasLodLevel ? e.IsLod : e.ChildLodDistance > 0 || Models.TryGetValue(e.Archetype, out var m) && IsLodName(m.Name);

    public static bool IsLodName(string name) =>
        name.Contains("_lod", StringComparison.OrdinalIgnoreCase)
        || name.Contains("slod", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("lod_", StringComparison.OrdinalIgnoreCase);

    public float? Radius(uint archetype) =>
        Archetypes.TryGetValue(archetype, out var def) && def.Radius > 0 ? def.Radius : null;
}
