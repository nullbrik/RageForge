/* RAGEFORGE - Archive Entry */

using RageForge.Core.Rpf9;

namespace RageForge.Gui.Models;

public sealed class ArchiveEntry
{
    public required string RelativePath { get; init; }
    public required string FullPath { get; init; }
    public required Rpf9Header Header { get; init; }

    public string Display => RelativePath;
    public string ShapeText => Header.Shape == 2 ? "hierarchical" : "flat";
    public string KeyIdHex => Header.KeyId.ToString("x8");
    public string TocHashHex => Header.TocHashHex;
    public uint Count => Header.Count;
    public uint TocEnd => Header.TocEnd;
    public uint TocFieldB => Header.TocFieldB;
    public uint NamesSize => Header.NamesSize;
    public uint TailSize => Header.TailSize;
    public bool LayoutOk => Header.ConstantsMatch;

    public static ArchiveEntry FromFile(string path, string root)
    {
        Span<byte> head = stackalloc byte[Rpf9Header.Size];
        using (var fs = File.OpenRead(path))
        {
            if (fs.Read(head) < Rpf9Header.Size)
                throw new InvalidDataException("File too small to contain an RPF9 header.");
        }

        return new ArchiveEntry
        {
            RelativePath = Path.GetRelativePath(root, path).Replace('\\', '/'),
            FullPath = path,
            Header = Rpf9Header.Read(head),
        };
    }
}
