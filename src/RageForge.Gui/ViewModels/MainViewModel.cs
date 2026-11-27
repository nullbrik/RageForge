/* RAGEFORGE - Main View Model */

using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RageForge.Core.Audio;
using RageForge.Core.Carve;
using RageForge.Core.Imaging;
using RageForge.Core.Index;
using RageForge.Core.Model;
using RageForge.Core.Ttpf;
using RageForge.Gui.Models;

namespace RageForge.Gui.ViewModels;

public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private const int MaxRows = 50000;

    private static readonly string SettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RageForge", "lastfolder.txt");

    private static readonly string ThemePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RageForge", "theme.txt");

    private readonly Stack<TreeNode> _back = new();
    private readonly Stack<TreeNode> _forward = new();
    private readonly TreeNode _categoriesRoot = new() { Kind = NodeKind.CategoriesRoot, Name = "Categories" };
    private readonly TreeNode _archivesRoot = new() { Kind = NodeKind.ArchivesRoot, Name = "Archives" };
    private readonly TreeNode _texturesRoot = new() { Kind = NodeKind.TexturesRoot, Name = "Texture stores" };

    private Dictionary<AssetCategory, List<IndexEntry>> _byCategory = [];
    private Dictionary<(string, AssetCategory), List<IndexEntry>> _byArchive = [];
    private List<IndexEntry> _all = [];
    private readonly List<TreeNode> _archiveNodes = [];
    private Task<TextureCatalog>? _catalogTask;
    private string? _catalogRoot;
    private CancellationTokenSource? _rowsCts;
    private CancellationTokenSource? _indexCts;
    private bool _navigating;
    private bool _silentSearch;

    public MainViewModel()
    {
        Roots = [_categoriesRoot, _archivesRoot, _texturesRoot];
        World = new WorldViewModel(() => FolderPath, () => _all, ModelEntries, GetCatalogAsync, s => Status = s);
        Peds = new PedsViewModel(() => FolderPath, () => _all, GetCatalogAsync, s => Status = s);
        try
        {
            if (File.Exists(SettingsPath))
                _folderPath = File.ReadAllText(SettingsPath).Trim();
            _isDark = File.Exists(ThemePath) && File.ReadAllText(ThemePath).Trim() == "dark";
        }
        catch (IOException)
        {
        }
        ApplyTheme(_isDark);
    }

    public ObservableCollection<TreeNode> Roots { get; }

    public WorldViewModel World { get; }

    public PedsViewModel Peds { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExplorerMode))]
    private bool _isWorldMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExplorerMode))]
    private bool _isPedsMode;

    public bool IsExplorerMode
    {
        get => !IsWorldMode && !IsPedsMode;
        set
        {
            if (!value)
            {
                OnPropertyChanged(nameof(IsExplorerMode));
                return;
            }
            IsWorldMode = false;
            IsPedsMode = false;
        }
    }

    partial void OnIsWorldModeChanged(bool value)
    {
        if (value) IsPedsMode = false;
    }

    partial void OnIsPedsModeChanged(bool value)
    {
        if (!value) return;
        IsWorldMode = false;
        Peds.Activate();
    }

    public void OpenWorldByName(string name)
    {
        if (name == "*")
        {
            IsWorldMode = true;
            World.LoadWorldCommand.Execute(null);
            return;
        }
        var hit = _all.FirstOrDefault(e => e.Tag is "it" or "mp" && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (hit is null)
        {
            Status = $"'{name}' is not an indexed place.";
            return;
        }
        IsWorldMode = true;
        _ = World.LoadAsync(hit);
    }

    public void OpenInWorld(RowItem? row)
    {
        if (row?.Entry is not { Tag: "it" or "mp" } e)
        {
            Status = "Open in World works on interiors and map placements.";
            return;
        }
        IsWorldMode = true;
        _ = World.LoadAsync(e);
    }

    public event Action<PreviewViewModel>? ViewerRequested;

    [ObservableProperty] private string _folderPath = string.Empty;
    [ObservableProperty] private string _status = "Ready.";
    [ObservableProperty] private string _itemsText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private TreeNode? _currentNode;
    [ObservableProperty] private TreeNode? _selectedTreeNode;
    [ObservableProperty] private ObservableCollection<RowItem> _rows = [];
    [ObservableProperty] private RowItem? _selectedRow;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _sortColumn = "Name";
    [ObservableProperty] private bool _sortDescending;
    [ObservableProperty] private bool _isDark;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private bool _canGoBack;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ForwardCommand))]
    private bool _canGoForward;

    public string LocationText => CurrentNode?.Path ?? string.Empty;

    public string NameHeader => Header("Name");
    public string TypeHeader => Header("Type");
    public string SizeHeader => Header("Size");
    public string LocationHeader => Header("Location");

    private string Header(string column) =>
        SortColumn == column ? $"{column}  {(SortDescending ? "▾" : "▴")}" : column;

    partial void OnSortColumnChanged(string value) => NotifyHeaders();

    partial void OnSortDescendingChanged(bool value) => NotifyHeaders();

    private void NotifyHeaders()
    {
        OnPropertyChanged(nameof(NameHeader));
        OnPropertyChanged(nameof(TypeHeader));
        OnPropertyChanged(nameof(SizeHeader));
        OnPropertyChanged(nameof(LocationHeader));
    }

    public bool HasIndex => _all.Count > 0;

    partial void OnCurrentNodeChanged(TreeNode? value) => OnPropertyChanged(nameof(LocationText));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(UpdateTip))]
    private Update.UpdateInfo? _update;

    public bool HasUpdate => Update is not null;

    public string UpdateTip => Update is { } u
        ? $"RageForge {u.Tag} is available (you have v{RageForge.Gui.Update.Updater.CurrentVersion}). Click to open the release page."
        : string.Empty;

    public async Task CheckForUpdateAsync()
    {
        try
        {
            Update = await RageForge.Gui.Update.Updater.CheckAsync();
        }
        catch (Exception)
        {
            Update = null;
        }
    }

    [RelayCommand]
    private void OpenUpdatePage()
    {
        if (Update is not { } u) return;
        try
        {
            RageForge.Gui.Update.Updater.OpenReleasePage(u);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Status = $"Cannot open the release page: {ex.Message}";
        }
    }

    public string ThemeGlyph => IsDark ? "" : "";

    public string ThemeTip => IsDark ? "Switch to light theme" : "Switch to dark theme";

    [RelayCommand]
    private void ToggleTheme() => IsDark = !IsDark;

    partial void OnIsDarkChanged(bool value)
    {
        ApplyTheme(value);
        OnPropertyChanged(nameof(ThemeGlyph));
        OnPropertyChanged(nameof(ThemeTip));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ThemePath)!);
            File.WriteAllText(ThemePath, value ? "dark" : "light");
        }
        catch (IOException)
        {
        }
    }

    private static void ApplyTheme(bool dark)
    {
        if (Avalonia.Application.Current is { } app)
            app.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
    }

    public Task<TextureCatalog> GetCatalogAsync()
    {
        var root = FolderPath;
        if (_catalogTask is null || _catalogRoot != root)
        {
            _catalogRoot = root;
            _catalogTask = Task.Run(() =>
            {
                try
                {
                    return Directory.Exists(root) ? TextureCatalog.Build(root) : new TextureCatalog();
                }
                catch (Exception)
                {
                    return new TextureCatalog();
                }
            });
        }
        return _catalogTask;
    }

    public async Task OpenGameFolderAsync(string folder)
    {
        FolderPath = folder;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            await File.WriteAllTextAsync(SettingsPath, folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("save settings", ex);
        }
        await InitializeAsync();
    }

    public async Task InitializeAsync()
    {
        try
        {
            await InitializeCoreAsync();
        }
        catch (Exception ex)
        {
            CrashLog.Write("open game folder", ex);
            Status = $"Cannot open the game folder: {ex.Message} (details in {CrashLog.PathName})";
        }
    }

    private async Task InitializeCoreAsync()
    {
        _back.Clear();
        _forward.Clear();
        UpdateHistory();
        _all = [];
        _byCategory = [];
        _byArchive = [];
        _catalogTask = null;
        foreach (var r in Roots)
        {
            r.Children.Clear();
            r.Count = -1;
        }

        if (!Directory.Exists(FolderPath))
        {
            Status = "Use File > Open game folder to start.";
            Navigate(_archivesRoot);
            return;
        }

        _archiveNodes.Clear();
        var dirs = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in RageForge.Core.Index.GameIndexer.FindFiles(FolderPath, "*.rpf")
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(FolderPath, file).Replace('\\', '/');
            int slash = rel.LastIndexOf('/');
            var parent = DirectoryNode(dirs, slash < 0 ? string.Empty : rel[..slash]);
            var node = new TreeNode { Kind = NodeKind.Archive, Name = rel[(slash + 1)..], Archive = rel, Parent = parent };
            parent.Children.Add(node);
            _archiveNodes.Add(node);
        }
        _archivesRoot.Count = _archiveNodes.Count;
        _archivesRoot.IsExpanded = true;
        Navigate(_archivesRoot);

        await LoadIndexAsync();
        _ = LoadTextureStoresAsync();
    }

    private TreeNode DirectoryNode(Dictionary<string, TreeNode> dirs, string rel)
    {
        if (rel.Length == 0) return _archivesRoot;
        if (dirs.TryGetValue(rel, out var existing)) return existing;
        int slash = rel.LastIndexOf('/');
        var parent = DirectoryNode(dirs, slash < 0 ? string.Empty : rel[..slash]);
        var node = new TreeNode { Kind = NodeKind.Directory, Name = rel[(slash + 1)..], Parent = parent };
        int at = parent.Children.TakeWhile(c => c.Kind == NodeKind.Directory).Count();
        parent.Children.Insert(at, node);
        dirs[rel] = node;
        return node;
    }

    private async Task LoadTextureStoresAsync()
    {
        var task = GetCatalogAsync();
        TextureCatalog catalog;
        try
        {
            catalog = await task;
        }
        catch (Exception ex)
        {
            CrashLog.Write("texture stores", ex);
            Status = $"Cannot read the texture stores: {ex.Message} (details in {CrashLog.PathName})";
            return;
        }
        if (task != _catalogTask) return;
        _texturesRoot.Children.Clear();
        foreach (var s in catalog.Stores.OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(FolderPath, s.Path).Replace('\\', '/');
            _texturesRoot.Children.Add(new TreeNode { Kind = NodeKind.TextureStore, Name = rel, Store = s, Parent = _texturesRoot, Count = s.Textures.Count });
        }
        _texturesRoot.Count = _texturesRoot.Children.Count;
        if (CurrentNode == _texturesRoot)
            Refresh();
    }

    [RelayCommand]
    private async Task LoadIndexAsync()
    {
        var root = FolderPath;
        if (!Directory.Exists(root)) return;
        IsBusy = true;
        Status = "Loading index...";
        try
        {
            var all = await Task.Run(() => GameIndexer.LoadCached(root, GameIndexer.DefaultIndexDir));
            if (root != FolderPath) return;
            await SetIndexAsync(all);
            Status = all.Count == 0
                ? "No index for this folder yet. Use Index > Build index (one-time scan, cached per archive)."
                : $"Index loaded: {all.Count:N0} entries found by scanning (tables of contents are encrypted and not decrypted; names come from resource data or are generated).";
        }
        catch (Exception ex)
        {
            Status = $"Cannot load the index: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BuildIndexAsync()
    {
        var root = FolderPath;
        if (!Directory.Exists(root))
        {
            Status = "Open the game folder first.";
            return;
        }
        IsBusy = true;
        using var cts = _indexCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<string>(s => Status = s);
            var all = await Task.Run(() => GameIndexer.IndexGame(root, GameIndexer.DefaultIndexDir, progress, cts.Token), cts.Token);
            if (root != FolderPath) return;
            await SetIndexAsync(all);
            Status = $"Index ready: {all.Count:N0} entries found by scanning (tables of contents are encrypted and not decrypted; names come from resource data or are generated).";
        }
        catch (OperationCanceledException)
        {
            Status = "Indexing cancelled. Finished archives stay cached.";
        }
        catch (Exception ex)
        {
            Status = $"Indexing failed: {ex.Message}";
        }
        finally
        {
            _indexCts = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelIndex() => _indexCts?.Cancel();

    private async Task SetIndexAsync(List<IndexEntry> all)
    {
        var (byCat, byArc) = await Task.Run(() =>
        {
            var c = all.GroupBy(e => e.Category).ToDictionary(g => g.Key, g => g.ToList());
            var a = all.GroupBy(e => (e.Archive, e.Category)).ToDictionary(g => g.Key, g => g.ToList());
            return (c, a);
        });
        _all = all;
        _byCategory = byCat;
        _byArchive = byArc;
        OnPropertyChanged(nameof(HasIndex));
        World.Refresh();

        _categoriesRoot.Children.Clear();
        foreach (var (cat, list) in byCat.OrderBy(k => k.Key))
            _categoriesRoot.Children.Add(new TreeNode { Kind = NodeKind.Category, Name = Categorizer.Label(cat), Category = cat, Parent = _categoriesRoot, Count = list.Count });
        _categoriesRoot.Count = all.Count > 0 ? all.Count : -1;
        _categoriesRoot.IsExpanded = all.Count > 0;

        var perArchive = byArc.GroupBy(k => k.Key.Item1).ToDictionary(g => g.Key, g => g.OrderBy(k => k.Key.Item2).ToList());
        foreach (var arc in _archiveNodes)
        {
            arc.Children.Clear();
            if (arc.Archive is null || !perArchive.TryGetValue(arc.Archive, out var groups))
            {
                arc.Count = -1;
                continue;
            }
            foreach (var g in groups)
                arc.Children.Add(new TreeNode { Kind = NodeKind.ArchiveCategory, Name = Categorizer.Label(g.Key.Item2), Category = g.Key.Item2, Archive = arc.Archive, Parent = arc, Count = g.Value.Count });
            arc.Count = groups.Sum(g => g.Value.Count);
        }

        if (all.Count > 0 && CurrentNode == _archivesRoot)
            Navigate(_categoriesRoot, record: false);
        else
            Refresh();
    }

    public const string EncryptedTocNotice =
        "The archive's table of contents is encrypted and RageForge does not decrypt it. Entries were located by scanning for compressed data: archive paths and file names from the table of contents are not available. Displayed names come from data inside each resource, or are generated.";

    partial void OnSelectedTreeNodeChanged(TreeNode? value)
    {
        if (!_navigating && value is not null && value != CurrentNode)
            Navigate(value);
        if (value is { Kind: NodeKind.Archive })
            Status = $"{value.Name}: {EncryptedTocNotice}";
    }

    public void Navigate(TreeNode node, bool record = true)
    {
        if (record && CurrentNode is { } cur && cur != node)
        {
            _back.Push(cur);
            _forward.Clear();
        }
        _navigating = true;
        try
        {
            CurrentNode = node;
            for (var p = node.Parent; p is not null; p = p.Parent)
                p.IsExpanded = true;
            SelectedTreeNode = node.Kind == NodeKind.Search ? null : node;
        }
        finally
        {
            _navigating = false;
        }
        UpdateHistory();
        _silentSearch = true;
        if (node.Kind != NodeKind.Search)
            SearchText = string.Empty;
        _silentSearch = false;
        Refresh();
    }

    private void UpdateHistory()
    {
        CanGoBack = _back.Count > 0;
        CanGoForward = _forward.Count > 0;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (_back.Count == 0 || CurrentNode is null) return;
        _forward.Push(CurrentNode);
        Navigate(_back.Pop(), record: false);
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Forward()
    {
        if (_forward.Count == 0 || CurrentNode is null) return;
        _back.Push(CurrentNode);
        Navigate(_forward.Pop(), record: false);
    }

    [RelayCommand]
    private void Up()
    {
        if (CurrentNode?.Parent is { } p)
            Navigate(p);
    }

    [RelayCommand]
    private void GlobalSearch()
    {
        var q = SearchText.Trim();
        if (q.Length == 0) return;
        if (!HasIndex)
        {
            Status = "Global search needs the index (Index > Load or Build).";
            return;
        }
        Navigate(new TreeNode { Kind = NodeKind.Search, Name = $"Search \"{q}\"", Query = q });
    }

    [RelayCommand]
    private void SortBy(string column)
    {
        if (SortColumn == column)
            SortDescending = !SortDescending;
        else
        {
            SortColumn = column;
            SortDescending = false;
        }
        Refresh();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_silentSearch && CurrentNode?.Kind != NodeKind.Search)
            _ = RefreshRowsAsync(debounce: true);
    }

    [RelayCommand]
    private void Refresh() => _ = RefreshRowsAsync(debounce: false);

    private async Task RefreshRowsAsync(bool debounce)
    {
        _rowsCts?.Cancel();
        _rowsCts?.Dispose();
        var cts = _rowsCts = new CancellationTokenSource();
        var node = CurrentNode;
        if (node is null) return;
        var filter = node.Kind == NodeKind.Search ? string.Empty : SearchText.Trim();
        var column = SortColumn;
        bool desc = SortDescending;
        var folders = node.Children.ToList();

        List<RowItem> rows;
        int total;
        try
        {
            if (debounce)
                await Task.Delay(180, cts.Token);
            (rows, total) = await Task.Run(() => BuildRows(node, folders, filter, column, desc, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested || node != CurrentNode) return;

        Rows = new ObservableCollection<RowItem>(rows);
        ItemsText = total > rows.Count ? $"{total:N0} items ({rows.Count:N0} shown, refine with search)" : $"{total:N0} items";
        if (node.Kind == NodeKind.Archive && node.Children.Count == 0)
            Status = "Build the index (Index > Build index) to browse this archive.";
    }

    private List<IndexEntry> EntriesOf(TreeNode node) => node.Kind switch
    {
        NodeKind.Category when node.Category is { } c => _byCategory.GetValueOrDefault(c) ?? [],
        NodeKind.ArchiveCategory when node.Category is { } c && node.Archive is { } a => _byArchive.GetValueOrDefault((a, c)) ?? [],
        _ => [],
    };

    private (List<RowItem>, int) BuildRows(TreeNode node, List<TreeNode> folders, string filter, string column, bool desc, CancellationToken ct)
    {
        bool Match(string name) => filter.Length == 0 || name.Contains(filter, StringComparison.OrdinalIgnoreCase);

        var folderRows = folders.Where(f => Match(f.Name)).Select(RowItem.ForFolder).ToList();
        Comparison<RowItem> rowCmp = column switch
        {
            "Type" => (a, b) => string.Compare(a.Type, b.Type, StringComparison.OrdinalIgnoreCase),
            "Size" => (a, b) => a.Size.CompareTo(b.Size),
            "Location" => (a, b) => string.Compare(a.Location, b.Location, StringComparison.OrdinalIgnoreCase),
            _ => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
        };
        Comparison<RowItem> rowOrder = desc ? (a, b) => rowCmp(b, a) : rowCmp;
        folderRows.Sort(rowOrder);
        int room = Math.Max(0, MaxRows - folderRows.Count);

        if (node.Kind == NodeKind.TextureStore && node.Store is { } store)
        {
            var textures = store.Textures.Where(t => Match(t.HashHex) || Match(t.FormatName)).Select(t => RowItem.ForTexture(store, t)).ToList();
            ct.ThrowIfCancellationRequested();
            textures.Sort(rowOrder);
            int totalTex = folderRows.Count + textures.Count;
            folderRows.AddRange(textures.Take(room));
            return (folderRows, totalTex);
        }

        var source = node.Kind == NodeKind.Search ? _all : EntriesOf(node);
        var needle = node.Kind == NodeKind.Search ? node.Query ?? string.Empty : filter;
        var matched = new List<IndexEntry>();
        for (int i = 0; i < source.Count; i++)
        {
            if ((i & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            var e = source[i];
            if (needle.Length == 0 || e.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                matched.Add(e);
        }
        int total = folderRows.Count + matched.Count;

        IEnumerable<IndexEntry> ordered;
        if (column == "Type")
        {
            var keys = new string[matched.Count];
            for (int i = 0; i < keys.Length; i++) keys[i] = RowItem.TypeOf(matched[i]);
            var arr = matched.ToArray();
            Array.Sort(keys, arr, StringComparer.OrdinalIgnoreCase);
            ordered = desc ? arr.Reverse() : arr;
        }
        else
        {
            Comparison<IndexEntry> cmp = column switch
            {
                "Size" => (a, b) => a.Size.CompareTo(b.Size),
                "Location" => (a, b) => string.Compare(a.Archive, b.Archive, StringComparison.OrdinalIgnoreCase) is var c and not 0 ? c : a.Offset.CompareTo(b.Offset),
                _ => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) is var c and not 0 ? c : string.CompareOrdinal(a.Tag, b.Tag),
            };
            matched.Sort(desc ? (a, b) => cmp(b, a) : cmp);
            ordered = matched;
        }
        ct.ThrowIfCancellationRequested();

        folderRows.AddRange(ordered.Take(room).Select(RowItem.ForEntry));
        return (folderRows, total);
    }

    public void Open(RowItem? row)
    {
        if (row is null) return;
        if (row.Folder is { } f)
        {
            Navigate(f);
            return;
        }
        var vm = new PreviewViewModel(row, FolderPath, GetCatalogAsync, ModelEntries);
        ViewerRequested?.Invoke(vm);
        _ = vm.LoadAsync();
    }

    private ModelLookup? _modelEntries;
    private List<IndexEntry>? _modelEntriesSource;
    private readonly Lock _modelEntriesGate = new();

    public ModelLookup ModelEntries()
    {
        lock (_modelEntriesGate)
        {
            var source = _all;
            if (_modelEntries is { } cached && ReferenceEquals(_modelEntriesSource, source))
                return cached;
            var map = ModelLookup.Build(source);
            _modelEntries = map;
            _modelEntriesSource = source;
            return map;
        }
    }

    public void OpenByName(string name)
    {
        var hit = _all.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && Categorizer.IsModel(e.Category))
            ?? _all.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (hit is null)
        {
            Status = $"'{name}' is not in the index.";
            return;
        }
        Open(RowItem.ForEntry(hit));
    }

    public void ShowProperties(RowItem? row)
    {
        if (row?.Folder is not { Kind: NodeKind.Archive, Archive: { } rel }) return;
        try
        {
            var a = ArchiveEntry.FromFile(Path.Combine(FolderPath, rel), FolderPath);
            int indexed = row.Folder.Count;
            var text =
                $"Archive        {a.RelativePath}\n" +
                $"Size           {RowItem.FormatSize((ulong)new FileInfo(a.FullPath).Length)}\n" +
                $"Entries (TOC)  {a.Count:N0}\n" +
                $"Indexed        {(indexed >= 0 ? indexed.ToString("N0") : "not indexed")}\n" +
                $"Shape          {a.ShapeText}\n" +
                $"Key id         {a.KeyIdHex}\n" +
                $"TOC hash       {a.TocHashHex}\n" +
                $"TOC end        0x{a.TocEnd:x}\n" +
                $"Names size     {a.NamesSize:N0}\n" +
                $"Layout ok      {a.LayoutOk}\n" +
                $"TOC            encrypted";
            var vm = new PreviewViewModel(row, FolderPath, GetCatalogAsync);
            vm.ShowText("Archive properties", text);
            ViewerRequested?.Invoke(vm);
        }
        catch (Exception ex)
        {
            Status = $"Cannot read archive header: {ex.Message}";
        }
    }

    public string PathOf(RowItem row) => row.Entry is { } e
        ? $"{e.Archive}@0x{e.Offset:x}/{e.Display}"
        : row.Store is { } s ? $"{Path.GetRelativePath(FolderPath, s.Path)}/{row.Name}" : row.Folder?.Path ?? row.Name;

    public void Dispose()
    {
        World.Dispose();
        Peds.Dispose();
        _rowsCts?.Cancel();
        _rowsCts?.Dispose();
        _indexCts?.Cancel();
    }
}
