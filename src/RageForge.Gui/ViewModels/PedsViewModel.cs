/* RAGEFORGE - Peds View Model */

using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RageForge.Core.Index;
using RageForge.Core.Model;
using RageForge.Core.Peds;

namespace RageForge.Gui.ViewModels;

public sealed record PedItem(PedCharacter Character)
{
    public string Name => Character.Name;
    public string Kind => Character.KindLabel;
    public string Glyph => "";
}

public sealed partial class PedsViewModel : ViewModelBase, IDisposable
{
    private readonly Func<string> _gameRoot;
    private readonly Func<IReadOnlyList<IndexEntry>> _index;
    private readonly Func<Task<TextureCatalog>> _catalog;
    private readonly Action<string> _status;
    private IReadOnlyList<IndexEntry>? _builtFrom;
    private List<PedCharacter> _all = [];
    private Task<FacePresets>? _faces;
    private IReadOnlyList<IndexEntry>? _facesFrom;
    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _loadCts;

    public PedsViewModel(Func<string> gameRoot, Func<IReadOnlyList<IndexEntry>> index, Func<Task<TextureCatalog>> catalog, Action<string> status)
    {
        _gameRoot = gameRoot;
        _index = index;
        _catalog = catalog;
        _status = status;
    }

    public IReadOnlyList<string> Filters { get; } = ["All", "People", "Animals"];

    [ObservableProperty] private ObservableCollection<PedItem> _peds = [];
    [ObservableProperty] private PedItem? _selectedPed;
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string _filter = "All";
    [ObservableProperty] private IReadOnlyList<DecodedMesh>? _meshes;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _info = "Pick a character to assemble it.";
    [ObservableProperty] private string _listInfo = string.Empty;

    partial void OnSearchChanged(string value) => _ = RefreshAsync(debounce: true);

    partial void OnFilterChanged(string value) => _ = RefreshAsync(debounce: false);

    partial void OnSelectedPedChanged(PedItem? value)
    {
        if (value is not null) _ = ShowAsync(value.Character);
    }

    public void Activate()
    {
        if (!ReferenceEquals(_builtFrom, _index())) _ = RefreshAsync(debounce: false);
    }

    private async Task RefreshAsync(bool debounce)
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        var cts = _filterCts = new CancellationTokenSource();
        var index = _index();
        var search = Search.Trim();
        var filter = Filter;
        List<PedItem> items;
        try
        {
            if (debounce) await Task.Delay(180, cts.Token);
            if (!ReferenceEquals(_builtFrom, index))
            {
                _all = await Task.Run(() => PedLibrary.Build(index), cts.Token);
                _builtFrom = index;
            }
            var all = _all;
            items = await Task.Run(() => all
                .Where(c => filter == "All" || (filter == "Animals") == (c.Kind == PedKind.Animal))
                .Where(c => search.Length == 0 || c.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                .Select(c => new PedItem(c)).ToList(), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested) return;
        Peds = new ObservableCollection<PedItem>(items);
        ListInfo = index.Count == 0 ? "Open a game folder and load its index first." : $"{items.Count:N0} characters";
    }

    private Task<FacePresets> Faces()
    {
        var index = _index();
        var root = _gameRoot();
        if (_faces is null || !ReferenceEquals(_facesFrom, index))
        {
            _facesFrom = index;
            _faces = Task.Run(() =>
            {
                try
                {
                    return FacePresets.Load(root, index);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    return FacePresets.Empty;
                }
            });
        }
        return _faces;
    }

    private async Task<AssembledPed> AssembleAsync(PedCharacter ped, TextureCatalog catalog, CancellationToken ct)
    {
        var faces = ped.Head is null ? FacePresets.Empty : await Faces();
        ct.ThrowIfCancellationRequested();
        var root = _gameRoot();
        return await Task.Run(() => PedAssembler.Assemble(root, ped, catalog, faces), ct);
    }

    private async Task ShowAsync(PedCharacter ped)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = _loadCts = new CancellationTokenSource();
        IsLoading = true;
        Info = $"Assembling {ped.Name}...";
        _status($"Peds: assembling {ped.Name} ({ped.Pieces.Count} pieces)...");
        try
        {
            var catalog = await _catalog();
            var assembled = await AssembleAsync(ped, catalog, cts.Token);
            if (cts.IsCancellationRequested) return;
            Meshes = assembled.Meshes;
            long verts = assembled.Meshes.Sum(m => (long)m.Positions.Count);
            Info = $"{ped.Name}    {ped.KindLabel}    {ped.Pieces.Count} pieces    {assembled.Meshes.Count} meshes ({assembled.Textured} textured)    {verts:N0} vertices"
                + (assembled.FaceSource is { } f && f != ped.Head?.Name ? $"    face borrowed from {f}" : string.Empty);
            _status($"Peds: {ped.Name}: {string.Join(", ", ped.Pieces.Select(p => p.Name))}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException or UnauthorizedAccessException)
        {
            if (!cts.IsCancellationRequested) Info = $"{ped.Name}: {ex.Message}";
        }
        finally
        {
            if (!cts.IsCancellationRequested) IsLoading = false;
        }
    }

    public void Dispose()
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _loadCts?.Cancel();
        _loadCts?.Dispose();
    }
}
