/* RAGEFORGE - Browser Items */

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RageForge.Core.Carve;
using RageForge.Core.Index;
using RageForge.Core.Ttpf;

namespace RageForge.Gui.Models;

public enum NodeKind
{
    CategoriesRoot,
    Category,
    ArchivesRoot,
    Directory,
    Archive,
    ArchiveCategory,
    TexturesRoot,
    TextureStore,
    Search,
}

public sealed partial class TreeNode : ObservableObject
{
    public required NodeKind Kind { get; init; }
    public required string Name { get; init; }
    public TreeNode? Parent { get; init; }
    public AssetCategory? Category { get; init; }
    public string? Archive { get; init; }
    public TtpfStore? Store { get; init; }
    public string? Query { get; init; }

    [ObservableProperty] private int _count = -1;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    public ObservableCollection<TreeNode> Children { get; } = [];

    public string Glyph => Kind switch
    {
        NodeKind.CategoriesRoot => "",
        NodeKind.ArchivesRoot => "",
        NodeKind.TexturesRoot => "",
        NodeKind.Search => "",
        NodeKind.Archive => "",
        _ => "",
    };

    public string Label => Count >= 0 ? $"{Name}  ({Count:N0})" : Name;

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(Label));

    public string Path => Parent is null ? Name : $"{Parent.Path}\\{Name}";
}

public sealed class RowItem
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required ulong Size { get; init; }
    public string Location { get; init; } = string.Empty;
    public string Offset { get; init; } = string.Empty;
    public required string Glyph { get; init; }
    public TreeNode? Folder { get; init; }
    public IndexEntry? Entry { get; init; }
    public TtpfStore? Store { get; init; }
    public TtpfTexture? Texture { get; init; }

    public bool IsFolder => Folder is not null;

    public string SizeText => IsFolder
        ? (Folder!.Count >= 0 ? $"{Folder.Count:N0} items" : string.Empty)
        : FormatSize(Size);

    public static string FormatSize(ulong b) => b switch
    {
        >= 1UL << 30 => $"{b / (double)(1UL << 30):F2} GB",
        >= 1UL << 20 => $"{b / (double)(1UL << 20):F1} MB",
        >= 1UL << 10 => $"{b / 1024.0:F1} KB",
        _ => $"{b} B",
    };

    public static RowItem ForFolder(TreeNode n) => new()
    {
        Name = n.Name,
        Type = n.Kind switch
        {
            NodeKind.Archive => "RPF9 archive",
            NodeKind.TextureStore => "TTPF store",
            _ => "Folder",
        },
        Size = 0,
        Glyph = n.Glyph,
        Folder = n,
    };

    public static RowItem ForEntry(IndexEntry e) => new()
    {
        Name = e.Display,
        Type = TypeOf(e),
        Size = e.Size,
        Location = e.Archive,
        Offset = $"0x{e.Offset:x}",
        Glyph = GlyphOf(e),
        Entry = e,
    };

    public static RowItem ForTexture(TtpfStore store, TtpfTexture t) => new()
    {
        Name = t.HashHex,
        Type = $"Texture {t.FormatName} {t.Width}x{t.Height}",
        Size = t.Size,
        Location = System.IO.Path.GetFileName(store.Path),
        Offset = $"0x{t.Virt:x}",
        Glyph = "",
        Store = store,
        Texture = t,
    };

    public static string TypeOf(IndexEntry e) => e.Kind switch
    {
        ResourceKind.Audio => "Audio (AWC)",
        ResourceKind.Video => "Video (Bink 2)",
        ResourceKind.Shader => "Shader cache (PS09)",
        ResourceKind.Xml => "XML",
        ResourceKind.Text => "Text",
        ResourceKind.Pso => "PSO",
        ResourceKind.Rbf => "RBF",
        ResourceKind.Gfx => "Scaleform GFX",
        ResourceKind.Png => "PNG image",
        ResourceKind.Dds => "DDS image",
        _ when e.Tag is "dr" => "Drawable",
        _ when e.Tag is "ft" => "Fragment",
        _ when e.Tag is "td" => "Texture dictionary",
        _ when e.Tag is "it" => "Interior",
        _ when e.Tag is "mp" => "Map placement",
        _ when e.Tag is { } t => $"Resource .#{t}",
        ResourceKind.Resource => $"Resource r{e.Sub}",
        ResourceKind.RageObj => "RAGE object",
        _ => "Binary",
    };

    private static string GlyphOf(IndexEntry e) => e.Kind switch
    {
        ResourceKind.Audio => "",
        ResourceKind.Video => "",
        ResourceKind.Shader => "",
        ResourceKind.Xml or ResourceKind.Text or ResourceKind.Pso or ResourceKind.Rbf => "",
        ResourceKind.Png or ResourceKind.Dds => "",
        _ when Categorizer.IsModel(e.Category) => "",
        _ => "",
    };
}
