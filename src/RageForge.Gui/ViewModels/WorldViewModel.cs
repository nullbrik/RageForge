/* RAGEFORGE - World View Model */

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RageForge.Core.Carve;
using RageForge.Core.Index;
using RageForge.Core.Model;
using RageForge.Core.Ttpf;
using RageForge.Core.World;

namespace RageForge.Gui.ViewModels;

public sealed record PlaceItem(IndexEntry Entry)
{
    public bool IsInterior => Entry.Tag == "it";
    public string Name => Entry.Name;
    public string Kind => IsInterior ? "Interior" : "Map";
    public string Glyph => IsInterior ? "" : "";
    public string Location => Entry.HasBounds ? $"{Entry.Bounds.X:F0}, {Entry.Bounds.Y:F0}" : string.Empty;
}

public enum StreamTier
{
    Far,
    Near,
    Detail,
    Lod,
    Slod1,
    Slod2,
    Slod3,
}

public readonly record struct StreamChunk(GridCell Cell, StreamTier Tier)
{
    public int Level => Tier >= StreamTier.Lod ? Tier - StreamTier.Lod + 1 : 0;
}

public sealed partial class WorldViewModel : ViewModelBase, IDisposable
{
    private const int MaxListed = 5000;
    private const int MaxEntities = 120_000;
    private const float MaxBlockRadius = 1500f;
    private const float CellSize = 200f;
    private const int MaxInflight = 2;
    private const long GeometryBudget = 2048L << 20;
    private const int MaxLevel = 4;
    private const float Hysteresis = 120f;
    private const double RetireGrace = 4;
    private readonly Dictionary<StreamChunk, double> _retiring = [];
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private const float DetailRadius = 120f;
    private const float NearRadius = 220f;
    private const float HdRadius = 300f;
    private const float LodRadius = 700f;
    private const float Slod1Radius = 1500f;
    private const float Slod2Radius = 3000f;
    private const float SmallModelRadius = 5f;
    private const float LargeModelRadius = 15f;
    private const int PlacementCacheSize = 1024;

    private readonly Func<string> _gameRoot;
    private readonly Func<IReadOnlyList<IndexEntry>> _index;
    private readonly Func<ModelLookup> _models;
    private readonly Func<Task<TextureCatalog>> _catalog;
    private readonly Action<string> _status;
    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _loadCts;

    public WorldViewModel(
        Func<string> gameRoot,
        Func<IReadOnlyList<IndexEntry>> index,
        Func<ModelLookup> models,
        Func<Task<TextureCatalog>> catalog,
        Action<string> status)
    {
        _gameRoot = gameRoot;
        _index = index;
        _models = models;
        _catalog = catalog;
        _status = status;
    }

    public event Action<StreamChunk, Scene>? ChunkReady;
    public event Action<StreamChunk>? ChunkDropped;

    public Func<int> UploadBacklog { get; set; } = () => 0;

    public Func<TtpfFormat, bool> NativeFormats { get; set; } = _ => false;

    private TextureCatalog? _textureSource;

    public MipChain? TextureMips(ulong hash, uint maxDim) => _textureSource?.Mips(hash, maxDim, NativeFormats);

    private bool IsNative(TtpfFormat format) => NativeFormats(format);

    private const int MaxUploadBacklog = 2;

    public void ResumeStreaming()
    {
        if (_streaming) Pump();
    }
    public event Action<Vector3, float, float>? StreamStarted;

    public IReadOnlyList<string> Filters { get; } = ["All", "Interiors", "Map"];
    public IReadOnlyList<int> Radii { get; } = [700, 1500, 3000, 6000, WholeMapRadius];

    private const int WholeMapRadius = 16000;
    private const float WorldStartRadius = 180f;

    public bool IsDawn
    {
        get => Math.Abs(TimeOfDay - DawnHour) < 0.01;
        set { if (value) TimeOfDay = DawnHour; }
    }

    public bool IsDay
    {
        get => Math.Abs(TimeOfDay - DayHour) < 0.01;
        set { if (value) TimeOfDay = DayHour; }
    }

    public bool IsSunset
    {
        get => Math.Abs(TimeOfDay - SunsetHour) < 0.01;
        set { if (value) TimeOfDay = SunsetHour; }
    }

    public bool IsNight
    {
        get => Math.Abs(TimeOfDay - NightHour) < 0.01;
        set { if (value) TimeOfDay = NightHour; }
    }

    private const double DawnHour = 6.75;
    private const double DayHour = 13.0;
    private const double SunsetHour = 18.25;
    private const double NightHour = 23.0;

    [RelayCommand]
    private void SetTime(string preset) => TimeOfDay = preset switch
    {
        "dawn" => DawnHour,
        "sunset" => SunsetHour,
        "night" => NightHour,
        _ => DayHour,
    };

    [RelayCommand]
    private void ToggleWater() => ShowWater = !ShowWater;

    partial void OnTimeOfDayChanged(double value)
    {
        OnPropertyChanged(nameof(IsDawn));
        OnPropertyChanged(nameof(IsDay));
        OnPropertyChanged(nameof(IsSunset));
        OnPropertyChanged(nameof(IsNight));
    }

    [RelayCommand]
    private async Task LoadWorldAsync()
    {
        StopStreaming();
        _loadCts?.Cancel();
        var blocks = await Task.Run(() => _index().Where(e => e.Tag == "mp" && e.HasBounds && e.Bounds.W <= MaxBlockRadius).ToList());
        if (blocks.Count == 0)
        {
            SceneInfo = "No map blocks indexed.";
            return;
        }
        float Percentile(IEnumerable<float> values, double q)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[(int)Math.Clamp(q * (sorted.Count - 1), 0, sorted.Count - 1)];
        }
        var densest = blocks
            .GroupBy(b => ((int)MathF.Floor(b.Bounds.X / 1000f), (int)MathF.Floor(b.Bounds.Y / 1000f)))
            .MaxBy(g => g.Count())!;
        var center = new Vector3(
            Percentile(densest.Select(b => b.Bounds.X), 0.5),
            Percentile(densest.Select(b => b.Bounds.Y), 0.5),
            Percentile(densest.Select(b => b.Bounds.Z), 0.5));
        Radius = WholeMapRadius;
        await StartStreamingAsync(center, "the whole map", WorldStartRadius, null);
    }

    [ObservableProperty] private ObservableCollection<PlaceItem> _places = [];
    [ObservableProperty] private PlaceItem? _selectedPlace;
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string _filter = "All";
    [ObservableProperty] private int _radius = 3000;
    [ObservableProperty] private Vector4 _focus;
    [ObservableProperty] private Scene? _scene;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _sceneInfo = "Pick an interior or a map block, then Load.";
    [ObservableProperty] private string _listInfo = string.Empty;
    [ObservableProperty] private double _timeOfDay = 13.0;
    [ObservableProperty] private bool _showWater = true;


    partial void OnSearchChanged(string value) => _ = RefreshAsync(debounce: true);

    partial void OnFilterChanged(string value) => _ = RefreshAsync(debounce: false);

    public void Refresh() => _ = RefreshAsync(debounce: false);

    private async Task RefreshAsync(bool debounce)
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        var cts = _filterCts = new CancellationTokenSource();
        var index = _index();
        var search = Search.Trim();
        var filter = Filter;
        List<PlaceItem> items;
        int total;
        try
        {
            if (debounce) await Task.Delay(180, cts.Token);
            (items, total) = await Task.Run(() =>
            {
                var q = index.Where(e => e.Tag is "it" or "mp");
                if (filter == "Interiors") q = q.Where(e => e.Tag == "it");
                else if (filter == "Map") q = q.Where(e => e.Tag == "mp");
                if (search.Length > 0) q = q.Where(e => e.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
                var all = q.OrderBy(e => e.Tag == "it" ? 0 : 1).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
                return (all.Take(MaxListed).Select(e => new PlaceItem(e)).ToList(), all.Count);
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested) return;
        Places = new ObservableCollection<PlaceItem>(items);
        ListInfo = total > items.Count ? $"{total:N0} places ({items.Count:N0} shown)" : $"{total:N0} places";
    }

    [RelayCommand]
    private Task LoadSelectedAsync() => SelectedPlace is { } p ? LoadAsync(p.Entry) : Task.CompletedTask;

    [RelayCommand]
    private void CancelLoad() => _loadCts?.Cancel();

    public async Task LoadAsync(IndexEntry place)
    {
        StopStreaming();
        if (place.Tag == "mp" && place.HasBounds)
        {
            _loadCts?.Cancel();
            await StartStreamingAsync(new Vector3(place.Bounds.X, place.Bounds.Y, place.Bounds.Z), place.Name, 0, place);
            return;
        }
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = _loadCts = new CancellationTokenSource();
        var ct = cts.Token;
        var root = _gameRoot();
        int radius = Radius;
        IsLoading = true;
        SceneInfo = $"Loading {place.Name}...";
        try
        {
            var lookupAll = await Task.Run(_models, ct);
            var models = lookupAll.Models;
            var index = _index();
            var blocks = place.Tag == "it" || !place.HasBounds
                ? [place]
                : index.Where(e => e == place || e.Tag == "mp" && e.HasBounds && e.Bounds.W <= MaxBlockRadius
                    && Vector2.Distance(new(e.Bounds.X, e.Bounds.Y), new(place.Bounds.X, place.Bounds.Y)) <= radius + MathF.Min(e.Bounds.W, 150f)).ToList();

            int skipped = 0;
            var entities = await Task.Run(() =>
            {
                var list = new List<MapEntity>();
                foreach (var b in blocks)
                {
                    ct.ThrowIfCancellationRequested();
                    MapPlacement? mp;
                    try
                    {
                        mp = MapPlacement.Parse(RageResource.Load(Path.Combine(root, b.Archive), b.ToCarved(), withGfx: false));
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException)
                    {
                        skipped++;
                        continue;
                    }
                    if (mp is null) { skipped++; continue; }
                    foreach (var e in mp.Entities)
                    {
                        if (lookupAll.IsLodParent(e)) continue;
                        list.Add(e);
                        if (list.Count >= MaxEntities) return list;
                    }
                }
                return list;
            }, ct);

            var catalog = (await _catalog()).Resized(place.Tag == "it" ? 256u : 128u, 1L << 30, IsNative);
            _textureSource = catalog;
            var progress = new Progress<(int Done, int Total)>(p => SceneInfo = $"Loading {place.Name}: models {p.Done:N0}/{p.Total:N0}...");
            var scene = await Task.Run(() => Scene.Build(entities, models, root, catalog, progress, ct), ct);
            ct.ThrowIfCancellationRequested();
            Focus = place.Tag == "it" || !place.HasBounds
                ? Vector4.Zero
                : new Vector4(place.Bounds.X, place.Bounds.Y, place.Bounds.Z, MathF.Max(radius * 0.5f, 40f));
            Scene = scene;
            string where = place.Tag == "it" ? "interior" : $"{blocks.Count:N0} map blocks within {radius} m";
            SceneInfo = $"{place.Name}    {where}    {scene.Instances.Count:N0} objects    {scene.Models.Count:N0} models"
                + (scene.MissingModels > 0 ? $" ({scene.MissingModels:N0} missing)" : string.Empty)
                + (skipped > 0 ? $", {skipped:N0} unreadable blocks skipped" : string.Empty);
            _lastPlace = place;
            _status($"World: {SceneInfo}");
        }
        catch (OperationCanceledException)
        {
            if (cts == _loadCts) SceneInfo = "Loading cancelled.";
        }
        catch (Exception ex)
        {
            if (cts == _loadCts) SceneInfo = $"Loading failed: {ex.Message}";
        }
        finally
        {
            if (cts == _loadCts) IsLoading = false;
        }
    }

    private IndexEntry? _lastPlace;

    public void Reload()
    {
        if (_streaming)
        {
            _loaded.Clear();
            Pump();
        }
        else if (_lastPlace is { } place)
            _ = LoadAsync(place);
    }

    private readonly MapGrid<IndexEntry>?[] _grids = new MapGrid<IndexEntry>?[MaxLevel + 1];
    private IReadOnlyList<IndexEntry>? _gridSource;
    private ModelLookup? _streamModels;
    private TextureCatalog? _streamCatalog;
    private string _streamRoot = string.Empty;
    private readonly Dictionary<StreamChunk, long> _loaded = [];
    private readonly Dictionary<StreamChunk, CancellationTokenSource> _loading = [];
    private readonly ConcurrentDictionary<IndexEntry, MapPlacement?> _placements = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<IndexEntry, MapEntity[]> _lodEntities = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<uint, float> _radii = [];
    private bool _streaming;
    private int _generation;
    private int _failedCells;
    private Vector2 _camera;
    private float _height;

    partial void OnRadiusChanged(int value)
    {
        if (!_streaming) return;
        Evict();
        Pump();
    }

    private async Task StartStreamingAsync(Vector3 center, string label, float cameraRadius, IndexEntry? place)
    {
        int generation = ++_generation;
        IsLoading = true;
        SceneInfo = $"Preparing map streaming around {label}...";
        try
        {
            var index = _index();
            _streamRoot = _gameRoot();
            var lookup = await Task.Run(_models);
            if (!ReferenceEquals(lookup, _streamModels))
            {
                _lodEntities.Clear();
                _radii.Clear();
            }
            _streamModels = lookup;
            if (!ReferenceEquals(index, _gridSource) || _grids[0] is null)
            {
                var blocks = index.Where(e => e.Tag == "mp" && e.HasBounds).ToList();
                await Task.Run(() =>
                {
                    _grids[0] = new MapGrid<IndexEntry>(blocks.Where(e => e.Bounds.W <= MaxBlockRadius), e => e.Bounds, CellSize);
                    for (int level = 1; level <= MaxLevel; level++)
                        _grids[level] = new MapGrid<IndexEntry>(blocks, e => e.Bounds, SizeOf(level));
                });
                _gridSource = index;
                _placements.Clear();
                _lodEntities.Clear();
            }
            _streamCatalog = (await _catalog()).Resized(128, 64L << 20, IsNative);
            _textureSource = _streamCatalog;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (generation == _generation)
            {
                SceneInfo = $"Loading failed: {ex.Message}";
                IsLoading = false;
            }
            return;
        }
        if (generation != _generation) return;
        if (place is not null) _lastPlace = place;
        _failedCells = 0;
        Scene = null;
        Focus = Vector4.Zero;
        _streaming = true;
        _camera = new Vector2(center.X, center.Y);
        _height = cameraRadius * 1.5f;
        StreamStarted?.Invoke(center, Radius, cameraRadius);
        Pump();
        _status($"World: streaming {label}");
    }

    public void UpdateCamera(Vector3 target, float height)
    {
        if (!_streaming) return;
        _camera = new Vector2(target.X, target.Y);
        _height = height;
        Evict();
        Pump();
    }

    private void StopStreaming()
    {
        _generation++;
        foreach (var cts in _loading.Values)
            cts.Cancel();
        _loading.Clear();
        foreach (var chunk in _loaded.Keys)
            ChunkDropped?.Invoke(chunk);
        _loaded.Clear();
        _retiring.Clear();
        _streaming = false;
    }

    private static bool IsLodTier(StreamTier tier) => tier >= StreamTier.Lod;

    private static bool IsOptional(StreamTier tier) => tier is StreamTier.Near or StreamTier.Detail;

    private static int LevelOf(StreamTier tier) => IsLodTier(tier) ? tier - StreamTier.Lod + 1 : 0;

    private static float SizeOf(int level) => CellSize * (1 << level);

    private static float SplitOf(int level) => level switch
    {
        1 => HdRadius,
        2 => LodRadius,
        3 => Slod1Radius,
        _ => Slod2Radius,
    };

    private MapGrid<IndexEntry>? GridOf(StreamTier tier) => _grids[LevelOf(tier)];

    private float NearOf(GridCell cell, int level)
    {
        float size = SizeOf(level), x0 = cell.X * size, y0 = cell.Y * size;
        float dx = MathF.Max(MathF.Max(x0 - _camera.X, 0), _camera.X - (x0 + size));
        float dy = MathF.Max(MathF.Max(y0 - _camera.Y, 0), _camera.Y - (y0 + size));
        return MathF.Sqrt(dx * dx + dy * dy + _height * _height);
    }

    private static GridCell ParentOf(GridCell cell) => new((int)MathF.Floor(cell.X / 2f), (int)MathF.Floor(cell.Y / 2f));

    private float Distance(StreamChunk chunk) => NearOf(chunk.Cell, LevelOf(chunk.Tier));

    private bool Rendered(StreamChunk chunk, float slack)
    {
        int level = LevelOf(chunk.Tier);
        float near = NearOf(chunk.Cell, level);
        if (near > Radius + slack) return false;
        if (level > 0 && near <= SplitOf(level) - slack) return false;
        if (level < MaxLevel && NearOf(ParentOf(chunk.Cell), level + 1) > SplitOf(level + 1) + slack) return false;
        return chunk.Tier switch
        {
            StreamTier.Detail => near <= DetailRadius + slack,
            StreamTier.Near => near <= NearRadius + slack,
            _ => true,
        };
    }

    private void Evict()
    {
        foreach (var (chunk, cts) in _loading.Where(kv => !Rendered(kv.Key, Hysteresis)).ToList())
        {
            cts.Cancel();
            _loading.Remove(chunk);
        }
    }

    private static readonly StreamTier[][] TiersByLevel =
    [
        [StreamTier.Far, StreamTier.Near, StreamTier.Detail],
        [StreamTier.Lod],
        [StreamTier.Slod1],
        [StreamTier.Slod2],
        [StreamTier.Slod3],
    ];

    private float ReachOf(int level) =>
        level >= MaxLevel ? Radius : MathF.Min(Radius, SplitOf(level + 1) + SizeOf(level + 1) * 1.5f);

    private List<StreamChunk> Wanted()
    {
        var wanted = new List<(StreamChunk Chunk, float Priority)>();
        for (int level = 0; level <= MaxLevel; level++)
        {
            if (_grids[level] is not { } grid) continue;
            foreach (var cell in grid.CellsWithin(_camera, ReachOf(level)))
            {
                float d = NearOf(cell, level);
                foreach (var tier in TiersByLevel[level])
                {
                    var chunk = new StreamChunk(cell, tier);
                    if (!Rendered(chunk, 0)) continue;
                    float priority = level > 0 ? 40f + d * 0.2f : d + (int)tier * CellSize * 0.4f;
                    wanted.Add((chunk, priority));
                }
            }
        }
        wanted.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        return wanted.ConvertAll(w => w.Chunk);
    }

    private (float X0, float Y0, float X1, float Y1) AreaOf(StreamChunk chunk)
    {
        float size = SizeOf(LevelOf(chunk.Tier));
        return (chunk.Cell.X * size, chunk.Cell.Y * size, (chunk.Cell.X + 1) * size, (chunk.Cell.Y + 1) * size);
    }

    private bool Overlaps(StreamChunk a, StreamChunk b)
    {
        var (ax0, ay0, ax1, ay1) = AreaOf(a);
        var (bx0, by0, bx1, by1) = AreaOf(b);
        return ax0 < bx1 && bx0 < ax1 && ay0 < by1 && by0 < ay1;
    }

    private void Retire(List<StreamChunk> wanted)
    {
        List<StreamChunk>? missing = null;
        double now = _clock.Elapsed.TotalSeconds;
        foreach (var chunk in _loaded.Keys.Where(c => !Rendered(c, Hysteresis)).ToList())
        {
            if (!_retiring.TryGetValue(chunk, out double since))
                _retiring[chunk] = since = now;
            bool gone = now - since > RetireGrace || Distance(chunk) > Radius + Hysteresis * 2;
            if (!gone)
            {
                missing ??= wanted.FindAll(w => !_loaded.ContainsKey(w));
                if (missing.Exists(w => Overlaps(w, chunk))) continue;
            }
            _retiring.Remove(chunk);
            _loaded.Remove(chunk);
            ChunkDropped?.Invoke(chunk);
        }
        foreach (var chunk in _retiring.Keys.Where(c => !_loaded.ContainsKey(c) || Rendered(c, Hysteresis)).ToList())
            _retiring.Remove(chunk);
    }

    private void Pump()
    {
        if (!_streaming || _grids[0] is null) return;
        var wanted = Wanted();
        long total = _loaded.Values.Sum();
        List<StreamChunk>? evictable = null;
        foreach (var chunk in wanted)
        {
            if (_loading.Count >= MaxInflight || UploadBacklog() > MaxUploadBacklog) break;
            if (_loaded.ContainsKey(chunk) || _loading.ContainsKey(chunk)) continue;
            if (total >= GeometryBudget && IsOptional(chunk.Tier))
            {
                float d = Distance(chunk);
                evictable ??= [.. _loaded.Keys.Where(c => IsOptional(c.Tier) || !Rendered(c, Hysteresis)).OrderBy(Distance)];
                while (total >= GeometryBudget && evictable.Count > 0 && Distance(evictable[^1]) > d)
                {
                    var farthest = evictable[^1];
                    evictable.RemoveAt(evictable.Count - 1);
                    total -= _loaded[farthest];
                    _loaded.Remove(farthest);
                    _retiring.Remove(farthest);
                    ChunkDropped?.Invoke(farthest);
                }
                if (total >= GeometryBudget) continue;
            }
            _ = LoadChunkAsync(chunk, _generation);
        }
        Retire(wanted);
        UpdateStreamInfo();
    }

    private async Task LoadChunkAsync(StreamChunk chunk, int generation)
    {
        var cts = new CancellationTokenSource();
        _loading[chunk] = cts;
        try
        {
            var (scene, bytes) = await Task.Run(() => BuildChunk(chunk, cts.Token), cts.Token);
            if (generation != _generation || cts.IsCancellationRequested) return;
            _loaded[chunk] = bytes;
            if (scene is not null) ChunkReady?.Invoke(chunk, scene);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception) when (generation == _generation && !cts.IsCancellationRequested)
        {
            _loaded[chunk] = 0;
            _failedCells++;
        }
        catch (Exception)
        {
        }
        finally
        {
            if (_loading.TryGetValue(chunk, out var current) && current == cts)
                _loading.Remove(chunk);
            cts.Dispose();
            if (generation == _generation)
                Pump();
        }
    }

    private StreamTier? LodTierOf(in MapEntity e, ModelLookup lookup)
    {
        if (e.HasLodLevel)
            return e.Level switch
            {
                LodLevel.Lod => StreamTier.Lod,
                LodLevel.Slod1 => StreamTier.Slod1,
                LodLevel.Slod2 => StreamTier.Slod2,
                LodLevel.Slod3 or LodLevel.Slod4 => StreamTier.Slod3,
                _ => null,
            };
        bool named = lookup.Models.TryGetValue(e.Archetype, out var model);
        if (!lookup.IsLodParent(e)) return null;
        float lod = lookup.LodDistance(e);
        if (lod <= 0 && named)
        {
            var name = model.Name;
            if (name.Contains("slod3", StringComparison.OrdinalIgnoreCase) || name.Contains("slod4", StringComparison.OrdinalIgnoreCase)) return StreamTier.Slod3;
            if (name.Contains("slod2", StringComparison.OrdinalIgnoreCase)) return StreamTier.Slod2;
            if (name.Contains("slod", StringComparison.OrdinalIgnoreCase)) return StreamTier.Slod1;
            return StreamTier.Lod;
        }
        return lod switch
        {
            <= 750f => StreamTier.Lod,
            <= 1600f => StreamTier.Slod1,
            <= 3200f => StreamTier.Slod2,
            _ => StreamTier.Slod3,
        };
    }

    private StreamTier HdTierOf(float radius) =>
        radius < SmallModelRadius ? StreamTier.Detail : radius < LargeModelRadius ? StreamTier.Near : StreamTier.Far;

    private (Scene? Scene, long Bytes) BuildChunk(StreamChunk chunk, CancellationToken ct)
    {
        if (GridOf(chunk.Tier) is not { } grid || _streamModels is not { } lookup) return (null, 0);
        var entities = new List<MapEntity>();
        foreach (var block in grid.ItemsOf(chunk.Cell))
        {
            ct.ThrowIfCancellationRequested();
            if (IsLodTier(chunk.Tier))
            {
                foreach (var e in LodEntities(block, lookup))
                    if (grid.Contains(chunk.Cell, e.Position) && LodTierOf(e, lookup) == chunk.Tier)
                        entities.Add(e);
                continue;
            }
            if (Placement(block) is not { } mp) continue;
            foreach (var e in mp.Entities)
            {
                if (!grid.Contains(chunk.Cell, e.Position) || LodTierOf(e, lookup) is not null) continue;
                float? radius = lookup.Radius(e.Archetype) ?? (_radii.TryGetValue(e.Archetype, out var learned) ? learned : null);
                if (radius is { } r && HdTierOf(r) != chunk.Tier) continue;
                entities.Add(e);
            }
        }
        if (entities.Count == 0) return (null, 0);
        Func<uint, float, bool>? keep = IsLodTier(chunk.Tier) ? null : (hash, radius) =>
        {
            _radii[hash] = radius;
            return HdTierOf(lookup.Radius(hash) ?? radius) == chunk.Tier;
        };
        var scene = Scene.Build(entities, lookup.Models, _streamRoot, _streamCatalog, null, ct, keep, SizeOf(LevelOf(chunk.Tier)));
        if (scene.Instances.Count == 0) return (null, 0);
        long bytes = scene.Batches.Concat(scene.ModelBatches.Values.SelectMany(l => l))
            .Sum(b => (long)b.Vertices.Length * sizeof(float) + (long)b.Indices.Length * (b.Vertices.Length / SceneBatcher.Stride <= ushort.MaxValue + 1 ? sizeof(ushort) : sizeof(uint)));
        bytes += scene.InstanceGroups.Sum(g => (long)g.Transforms.Length * 64);
        return (scene, bytes);
    }

    private MapEntity[] LodEntities(IndexEntry block, ModelLookup lookup)
    {
        if (_lodEntities.TryGetValue(block, out var cached)) return cached;
        var list = Placement(block) is { } mp ? mp.Entities.Where(e => LodTierOf(e, lookup) is not null).ToArray() : [];
        _lodEntities[block] = list;
        return list;
    }

    private MapPlacement? Placement(IndexEntry block)
    {
        if (_placements.TryGetValue(block, out var cached)) return cached;
        MapPlacement? mp;
        try
        {
            mp = MapPlacement.Parse(RageResource.Load(Path.Combine(_streamRoot, block.Archive), block.ToCarved(), withGfx: false));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            mp = null;
        }
        if (_placements.Count >= PlacementCacheSize) _placements.Clear();
        _placements[block] = mp;
        return mp;
    }

    private void UpdateStreamInfo()
    {
        if (!_streaming) return;
        long total = _loaded.Values.Sum();
        int hd = _loaded.Keys.Count(c => !IsLodTier(c.Tier)), lod = _loaded.Keys.Count(c => IsLodTier(c.Tier));
        SceneInfo = $"Map streaming    {_camera.X:F0}, {_camera.Y:F0}    {hd:N0} detail + {lod:N0} LOD chunks"
            + (_loading.Count > 0 ? $", {_loading.Count} loading" : string.Empty)
            + $"    {total >> 20:N0} MB geometry"
            + (total >= GeometryBudget ? " (memory budget reached)" : string.Empty)
            + (_failedCells > 0 ? $"    {_failedCells} failed" : string.Empty);
        IsLoading = _loading.Count > 0;
    }

    public void Dispose()
    {
        StopStreaming();
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _loadCts?.Cancel();
        _loadCts?.Dispose();
    }
}
