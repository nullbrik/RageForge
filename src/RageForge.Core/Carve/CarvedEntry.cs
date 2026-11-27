/* RAGEFORGE - Carved Entry */

namespace RageForge.Core.Carve;

public readonly record struct ZlibStream(ulong Offset, ulong Csize, ulong Usize);

public readonly record struct RawBlob(ulong Offset, ulong Size, ResourceKind Kind, string Sub);

public sealed class CarvedEntry
{
    public required string Id { get; init; }
    public required ulong Offset { get; init; }
    public ulong Csize { get; set; }
    public ulong Usize { get; set; }
    public required List<ZlibStream> Streams { get; init; }
    public required ResourceKind Kind { get; set; }
    public required string Sub { get; init; }
    public required string Ext { get; set; }
    public ulong RawSize { get; init; }
    public string? Name { get; set; }
    public string? RType { get; set; }

    public bool IsRaw => Streams.Count == 0;
    public bool IsDrawable => RType is "dr" or "ft";
}
