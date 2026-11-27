/* RAGEFORGE - Archetype Definitions */

using RageForge.Core.Carve;

namespace RageForge.Core.World;

public readonly record struct ArchetypeDef(uint Name, float LodDistance, float Radius);

public static class ArchetypeDefs
{
    public const uint Field = 0x82d6fc83;
    public const int RecordSize = 0xb8;

    public static ArchetypeDef[]? Parse(RageResource r)
    {
        if (MapPlacement.ReadFields(r) is not { } fields || !fields.TryGetValue(Field, out var f) || f.Size == 0 || f.Size % RecordSize != 0)
            return null;
        var result = new ArchetypeDef[f.Size / RecordSize];
        for (int k = 0; k < result.Length; k++)
        {
            int b = f.Offset + k * RecordSize;
            result[k] = new ArchetypeDef(r.U32(b + 0x74), r.F32(b + 0x38), r.F32(b + 0x64));
        }
        return result;
    }
}
