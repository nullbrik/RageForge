/* RAGEFORGE - Map Placement */

using System.Numerics;
using RageForge.Core.Carve;

namespace RageForge.Core.World;

public enum LodLevel : byte
{
    Hd = 0,
    Lod = 1,
    Slod1 = 2,
    Slod2 = 3,
    Slod3 = 4,
    OrphanHd = 5,
    Slod4 = 6,
    Unknown = 255,
}

public readonly record struct MapEntity(
    uint Archetype,
    Vector3 Position,
    Quaternion Rotation,
    float ScaleXY,
    float ScaleZ,
    int Parent,
    float LodDistance,
    uint Flags,
    ulong Guid,
    float ChildLodDistance = 0,
    uint Tint = 0,
    LodLevel Level = LodLevel.Unknown)
{
    public bool HasLodLevel => Level is >= LodLevel.Hd and <= LodLevel.Slod4;

    public bool IsLod => Level is LodLevel.Lod or LodLevel.Slod1 or LodLevel.Slod2 or LodLevel.Slod3 or LodLevel.Slod4;

    public int LodParentIndex => (Parent & 0xffff) != 0xffff && (Parent >>> 16) == 0xffff ? Parent & 0xffff : -1;

    public int Depth => Level switch
    {
        LodLevel.Lod => 1,
        LodLevel.Slod1 => 2,
        LodLevel.Slod2 => 3,
        LodLevel.Slod3 => 4,
        LodLevel.Slod4 => 5,
        _ => 0,
    };
}

public readonly record struct MapRoom(uint Name);

public readonly record struct MapEntitySet(uint Name);

public sealed class MapPlacement
{
    public const uint EntitiesField = 0xce501483;
    public const uint RoomsField = 0x0b1811f1;
    public const uint EntitySetsField = 0xd6a799f9;
    public const uint PortalsField = 0x00000033;
    public const uint BlockField = 0xd3593fa6;
    public const uint ParentBlocksField = 0x0000004f;
    public const uint TintMember = 0x3c852527;
    public const uint LodLevelMember = 0x6c8f1715;
    public const int DefaultTintOffset = 0x70;
    public const int DefaultLodLevelOffset = 0x7e;

    private const int EntitySize = 0x90;
    private const int RoomSize = 0x70;
    private const int EntitySetSize = 0x28;
    private const int PortalSize = 0x40;

    public required IReadOnlyList<MapEntity> Entities { get; init; }
    public required IReadOnlyList<MapRoom> Rooms { get; init; }
    public required IReadOnlyList<MapEntitySet> EntitySets { get; init; }
    public required int PortalCount { get; init; }
    public required IReadOnlyDictionary<uint, (int Offset, int Size)> Fields { get; init; }
    public ulong Name { get; init; }
    public IReadOnlyList<ulong> ParentBlocks { get; init; } = [];

    public bool IsInterior => Rooms.Count > 0;

    public Vector4 Bounds()
    {
        if (Entities.Count == 0) return Vector4.Zero;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var e in Entities)
        {
            if (!float.IsFinite(e.Position.X) || !float.IsFinite(e.Position.Y) || !float.IsFinite(e.Position.Z)) continue;
            min = Vector3.Min(min, e.Position);
            max = Vector3.Max(max, e.Position);
        }
        if (min.X > max.X) return Vector4.Zero;
        var c = (min + max) * 0.5f;
        return new Vector4(c, MathF.Max(1f, (max - min).Length() * 0.5f));
    }

    public static IReadOnlyDictionary<uint, (int Offset, int Size)>? ReadFields(RageResource r)
    {
        if (r.SysOff(r.U64(0x38)) is not { } table) return null;
        int n = r.U16(0x4c);
        if (n is 0 or > 1024 || table + (long)n * 16 > r.Sys.Length) return null;
        var fields = new Dictionary<uint, (int, int)>();
        for (int i = 0; i < n; i++)
        {
            int row = table + 16 * i;
            uint hash = r.U32(row);
            int size = (int)Math.Min(r.U32(row + 4), int.MaxValue);
            if (r.SysOff(r.U64(row + 8)) is not { } off || size <= 0 || off + (long)size > r.Sys.Length) continue;
            fields.TryAdd(hash, (off, size));
        }
        return fields;
    }

    public static bool Probe(RageResource r) =>
        ReadFields(r) is { } f && f.TryGetValue(EntitiesField, out var e) && e.Size % EntitySize == 0;

    public static MapPlacement? Parse(RageResource r)
    {
        if (ReadFields(r) is not { } fields || !fields.TryGetValue(EntitiesField, out var ent) || ent.Size % EntitySize != 0)
            return null;

        var schema = PlacementSchema.Struct(r, EntitiesField);
        int tintAt = schema?.OffsetOf(TintMember) is { } t && t + 4 <= EntitySize ? t : DefaultTintOffset;
        int levelAt = schema?.OffsetOf(LodLevelMember) is { } l && l < EntitySize ? l : DefaultLodLevelOffset;
        var entities = new List<MapEntity>(ent.Size / EntitySize);
        for (int b = ent.Offset; b + EntitySize <= ent.Offset + ent.Size; b += EntitySize)
        {
            var rot = new Quaternion(r.F32(b + 0x10), r.F32(b + 0x14), r.F32(b + 0x18), r.F32(b + 0x1c));
            entities.Add(new MapEntity(
                Archetype: r.U32(b + 0x44),
                Position: new Vector3(r.F32(b + 0x38), r.F32(b + 0x3c), r.F32(b + 0x40)),
                Rotation: rot,
                ScaleXY: r.F32(b + 0x54),
                ScaleZ: r.F32(b + 0x58),
                Parent: (int)r.U32(b + 0x5c),
                LodDistance: r.F32(b + 0x60),
                Flags: r.U32(b + 0x48),
                Guid: r.U64(b + 0x08),
                ChildLodDistance: r.F32(b + 0x64),
                Tint: r.U32(b + tintAt),
                Level: r.Sys[b + levelAt] is var level && level <= (byte)LodLevel.Slod4 ? (LodLevel)level : LodLevel.Unknown));
        }

        var rooms = new List<MapRoom>();
        if (fields.TryGetValue(RoomsField, out var rf) && rf.Size % RoomSize == 0)
            for (int b = rf.Offset; b + RoomSize <= rf.Offset + rf.Size; b += RoomSize)
                rooms.Add(new MapRoom(r.U32(b)));

        var sets = new List<MapEntitySet>();
        if (fields.TryGetValue(EntitySetsField, out var sf) && sf.Size % EntitySetSize == 0)
            for (int b = sf.Offset; b + EntitySetSize <= sf.Offset + sf.Size; b += EntitySetSize)
                sets.Add(new MapEntitySet(r.U32(b)));

        int portals = fields.TryGetValue(PortalsField, out var pf) && pf.Size % PortalSize == 0 ? pf.Size / PortalSize : 0;

        ulong name = 0;
        var parents = new List<ulong>();
        if (fields.TryGetValue(BlockField, out var block) && block.Size >= 0x20)
        {
            name = r.U64(block.Offset + 0x10);
            int count = (int)r.U32(block.Offset + 0x18);
            if (count > 0 && fields.TryGetValue(ParentBlocksField, out var parentField) && parentField.Size == 8 * count)
                for (int k = 0; k < count; k++)
                    parents.Add(r.U64(parentField.Offset + 8 * k));
        }

        return new MapPlacement { Entities = entities, Rooms = rooms, EntitySets = sets, PortalCount = portals, Fields = fields, Name = name, ParentBlocks = parents };
    }

    public static (int Block, int Entity)? ResolveLodParent(in MapEntity child, IReadOnlyList<MapPlacement?> parentBlocks)
    {
        int index = child.LodParentIndex;
        if (index < 0) return null;
        (int Block, int Entity)? best = null;
        float bestDistance = float.MaxValue;
        var at = new Vector2(child.Position.X, child.Position.Y);
        for (int k = 0; k < parentBlocks.Count; k++)
        {
            if (parentBlocks[k] is not { } mp || index >= mp.Entities.Count) continue;
            var parent = mp.Entities[index];
            if (!parent.IsLod || child.HasLodLevel && parent.Depth <= child.Depth) continue;
            float d = Vector2.Distance(at, new Vector2(parent.Position.X, parent.Position.Y));
            if (d < bestDistance)
            {
                bestDistance = d;
                best = (k, index);
            }
        }
        return best;
    }
}
