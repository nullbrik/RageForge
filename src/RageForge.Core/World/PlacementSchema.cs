/* RAGEFORGE - Placement Schema */

using RageForge.Core.Carve;

namespace RageForge.Core.World;

public readonly record struct SchemaMember(uint Name, int Offset, uint Type, uint Reference);

public sealed record SchemaStruct(uint Name, int Size, IReadOnlyList<SchemaMember> Members)
{
    public int? OffsetOf(uint member)
    {
        foreach (var m in Members)
            if (m.Name == member) return m.Offset;
        return null;
    }
}

public readonly record struct SchemaEnum(uint Name, IReadOnlyDictionary<uint, int> Values);

public static class PlacementSchema
{
    private const int StructsPointer = 0x28;
    private const int EnumsPointer = 0x30;
    private const int StructCount = 0x48;
    private const int EnumCount = 0x4a;
    private const int StructSize = 0x20;
    private const int MemberSize = 0x10;
    private const int EnumSize = 0x18;
    private const int EnumValueSize = 0x10;

    public static IReadOnlyList<SchemaStruct> Structs(RageResource r)
    {
        var result = new List<SchemaStruct>();
        if (r.SysOff(r.U64(StructsPointer)) is not { } table) return result;
        int n = r.U16(StructCount);
        if (n > 4096 || table + (long)n * StructSize > r.Sys.Length) return result;
        for (int i = 0; i < n; i++)
        {
            int row = table + StructSize * i;
            int count = r.U16(row + 0x1e);
            if (r.SysOff(r.U64(row + 0x10)) is not { } members || members + (long)count * MemberSize > r.Sys.Length) continue;
            var list = new List<SchemaMember>(count);
            for (int k = 0; k < count; k++)
            {
                int m = members + MemberSize * k;
                list.Add(new SchemaMember(r.U32(m), (int)Math.Min(r.U32(m + 4), int.MaxValue), r.U32(m + 8), r.U32(m + 12)));
            }
            result.Add(new SchemaStruct(r.U32(row), (int)Math.Min(r.U32(row + 0x18), int.MaxValue), list));
        }
        return result;
    }

    public static SchemaStruct? Struct(RageResource r, uint name)
    {
        foreach (var s in Structs(r))
            if (s.Name == name) return s;
        return null;
    }

    public static IReadOnlyList<SchemaEnum> Enums(RageResource r)
    {
        var result = new List<SchemaEnum>();
        if (r.SysOff(r.U64(EnumsPointer)) is not { } table) return result;
        int n = r.U16(EnumCount);
        if (n > 4096 || table + (long)n * EnumSize > r.Sys.Length) return result;
        for (int i = 0; i < n; i++)
        {
            int row = table + EnumSize * i;
            int count = (int)Math.Min(r.U32(row + 0x10), 4096);
            if (r.SysOff(r.U64(row + 8)) is not { } values || values + (long)count * EnumValueSize > r.Sys.Length) continue;
            var map = new Dictionary<uint, int>(count);
            for (int k = 0; k < count; k++)
                map.TryAdd(r.U32(values + EnumValueSize * k + 8), (int)r.U32(values + EnumValueSize * k));
            result.Add(new SchemaEnum(r.U32(row), map));
        }
        return result;
    }
}
