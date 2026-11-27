/* RAGEFORGE - Preview View Model */

using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RageForge.Core.Audio;
using RageForge.Core.Carve;
using RageForge.Core.Imaging;
using RageForge.Core.Index;
using RageForge.Core.Model;
using RageForge.Core.Shaders;
using RageForge.Core.Ttpf;
using RageForge.Core.Video;
using RageForge.Gui.Audio;
using RageForge.Gui.Imaging;
using RageForge.Gui.Models;

namespace RageForge.Gui.ViewModels;

public enum PreviewMode
{
    None,
    Model,
    Image,
    Audio,
    Video,
    Text,
    Shader,
}

public sealed record DictTexture(string Name, uint Width, uint Height, string Format, EmbeddedTexture Source)
{
    public string Display => $"{Name}    {Width}x{Height}    {Format}";
}

public sealed record AudioTrack(int Index, DecodedAudio Audio)
{
    public string Display => $"Track {Index:D2}    {Audio.Channels} ch    {Audio.SampleRate / 1000.0:0.#} kHz    {Audio.Seconds:F1} s";
}

public sealed partial class PreviewViewModel : ViewModelBase, IDisposable
{
    private const int MaxListed = 50000;
    private const int MaxTextBytes = 2 << 20;
    private const int HexBytes = 64 << 10;

    private readonly RowItem _row;
    private readonly string _gameRoot;
    private readonly Func<Task<TextureCatalog>> _catalog;
    private readonly AudioPlayer _player = new();
    private readonly CancellationTokenSource _cts = new();
    private CancellationTokenSource? _shaderCts;

    private byte[]? _data;
    private DecodedTexture? _texture;
    private Ps09Cache? _shaderCache;
    private List<DecodedMesh>? _meshList;
    private byte[]? _frame;
    private float _yaw = 0.6f, _pitch = 0.5f, _zoom = 1f;
    private int _viewW = 640, _viewH = 480;
    private bool _rendering, _renderPending;

    private readonly Func<ModelLookup>? _modelEntries;

    public PreviewViewModel(RowItem row, string gameRoot, Func<Task<TextureCatalog>> catalog, Func<ModelLookup>? modelEntries = null)
    {
        _row = row;
        _gameRoot = gameRoot;
        _catalog = catalog;
        _modelEntries = modelEntries;
        Title = row.Name;
        _player.Stopped += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => IsPlaying = false);
    }

    public string Title { get; }
    public ObservableCollection<AudioTrack> Tracks { get; } = [];
    public ObservableCollection<DictTexture> DictTextures { get; } = [];

    [ObservableProperty] private DictTexture? _selectedDictTexture;

    public bool HasDictionary => DictTextures.Count > 0;

    partial void OnSelectedDictTextureChanged(DictTexture? value) => _ = ShowDictTextureAsync(value);

    private async Task ShowDictTextureAsync(DictTexture? value)
    {
        if (value is null) return;
        var ct = _cts.Token;
        Info = $"{DictTextures.Count} textures    {value.Name}    decoding...";
        DecodedTexture? tex;
        try
        {
            tex = await Task.Run(() => value.Source.Decode(), ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (ct.IsCancellationRequested || _disposed || value != SelectedDictTexture) return;
        if (tex is not { } t)
        {
            Image = null;
            _texture = null;
            Info = $"{DictTextures.Count} textures    {value.Name}    cannot be decoded";
            return;
        }
        _texture = t;
        Image = BitmapFactory.FromRgba(t.Rgba, (int)t.Width, (int)t.Height);
        Info = $"{DictTextures.Count} textures    {value.Name}    {value.Width}x{value.Height}    {value.Format}";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModel), nameof(IsImage), nameof(IsAudio), nameof(IsVideo), nameof(IsText), nameof(IsShader), nameof(IsLoading), nameof(IsGpuModel), nameof(IsSoftwareModel))]
    private PreviewMode _mode;

    public bool IsModel => Mode == PreviewMode.Model;
    public bool IsImage => Mode == PreviewMode.Image;
    public bool IsAudio => Mode == PreviewMode.Audio;
    public bool IsVideo => Mode == PreviewMode.Video;
    public bool IsText => Mode == PreviewMode.Text;
    public bool IsShader => Mode == PreviewMode.Shader;
    public bool IsLoading => Mode == PreviewMode.None;

    [ObservableProperty] private string _info = "Loading...";
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private AudioTrack? _selectedTrack;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private ObservableCollection<ShaderEntry> _shaders = [];
    [ObservableProperty] private ShaderEntry? _selectedShader;
    [ObservableProperty] private bool _showHlsl;
    [ObservableProperty] private string _shaderText = string.Empty;
    [ObservableProperty] private string _shaderFilter = string.Empty;
    [ObservableProperty] private IReadOnlyList<DecodedMesh>? _meshes;
    [ObservableProperty] private RageForge.Core.World.Scene? _scene;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGpuModel), nameof(IsSoftwareModel))]
    private bool _useSoftware;

    public bool IsGpuModel => IsModel && !UseSoftware;
    public bool IsSoftwareModel => IsModel && UseSoftware;

    partial void OnUseSoftwareChanged(bool value)
    {
        if (value)
            RequestRender();
    }

    private bool _disposed;

    partial void OnImageChanged(Bitmap? oldValue, Bitmap? newValue)
    {
        oldValue?.Dispose();
        if (_disposed)
            newValue?.Dispose();
    }

    public void ReloadScene()
    {
        if (Scene is null || _disposed) return;
        Scene = null;
        _ = LoadAsync();
    }

    public void ShowText(string info, string text)
    {
        Info = info;
        Text = text;
        Mode = PreviewMode.Text;
    }

    public async Task LoadAsync()
    {
        var ct = _cts.Token;
        try
        {
            if (_row.Texture is { } tex && _row.Store is { } store)
            {
                await LoadTextureAsync(store, tex, ct);
                return;
            }
            if (_row.Entry is { } e)
                await LoadEntryAsync(e, ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Info = $"Failed: {ex.Message}";
            Text = ex.ToString();
            Mode = PreviewMode.Text;
        }
    }

    private async Task LoadTextureAsync(TtpfStore store, TtpfTexture t, CancellationToken ct)
    {
        if (!t.Decodable)
        {
            Text = $"{t.HashHex}\n{t.Width} x {t.Height}  {t.FormatName}\n\nNot decodable (BCPack or unsupported tile mode).";
            Info = $"{t.Width} x {t.Height}  {t.FormatName}";
            Mode = PreviewMode.Text;
            return;
        }
        var decoded = await Task.Run(() => TtpfDecoder.Decode(store, t), ct);
        ct.ThrowIfCancellationRequested();
        _texture = decoded;
        Image = BitmapFactory.FromRgba(decoded.Rgba, (int)decoded.Width, (int)decoded.Height);
        Mode = PreviewMode.Image;
        Info = $"{t.Width} x {t.Height}  {t.FormatName}" + (decoded.Mip == 0 ? string.Empty : $"  (shown mip {decoded.Mip})");
    }

    private async Task LoadEntryAsync(IndexEntry e, CancellationToken ct)
    {
        var path = Path.Combine(_gameRoot, e.Archive);
        byte[]? data = null;
        if (e.Tag is "it" or "mp")
        {
            var entries = await Task.Run(() => (_modelEntries?.Invoke() ?? ModelLookup.Empty).Models, ct);
            var placement = await Task.Run(() => RageForge.Core.World.MapPlacement.Parse(RageResource.Load(path, e.ToCarved(), withGfx: false)), ct);
            ct.ThrowIfCancellationRequested();
            if (placement is not null)
            {
                var names = entries.ToDictionary(k => k.Key, k => k.Value.Name);
                var (summary, body) = DescribePlacement(placement, names);
                Text = body;
                Info = $"Loading models...    {summary}";
                var catalog = (await _catalog()).Resized(256, 1L << 30);
                var progress = new Progress<(int Done, int Total)>(p => Info = $"Loading models {p.Done:N0}/{p.Total:N0}...");
                var scene = await Task.Run(() => RageForge.Core.World.Scene.Build(placement, entries, _gameRoot, catalog, progress, ct), ct);
                ct.ThrowIfCancellationRequested();
                Scene = scene;
                Mode = PreviewMode.Model;
                Info = $"{summary}    {scene.Models.Count:N0} models loaded" + (scene.MissingModels > 0 ? $", {scene.MissingModels:N0} missing" : string.Empty)
                    + "    WASD/ZQSD move, E/Space up, C down, Shift faster, Ctrl slower";
                return;
            }
        }
        if (e.Tag == "td")
        {
            var textures = await Task.Run(() =>
            {
                var r = RageResource.Load(path, e.ToCarved(), withGfx: true);
                return TextureDictionary.Read(r).Values
                    .Select(t => new DictTexture(t.Name, t.Width, t.Height, TtpfFormats.DxgiName(t.Dxgi), t))
                    .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }, ct);
            ct.ThrowIfCancellationRequested();
            if (textures.Count > 0)
            {
                foreach (var t in textures) DictTextures.Add(t);
                OnPropertyChanged(nameof(HasDictionary));
                Mode = PreviewMode.Image;
                SelectedDictTexture = DictTextures[0];
                return;
            }
        }
        if (Categorizer.IsModel(e.Category))
        {
            var catalog = await _catalog();
            ct.ThrowIfCancellationRequested();
            var (meshes, raw) = await Task.Run(() =>
            {
                var r = RageResource.Load(path, e.ToCarved(), withGfx: true);
                ct.ThrowIfCancellationRequested();
                var m = Drawable.ExtractMeshes(r, catalog);
                return (m, m.Count == 0 ? [.. r.Sys, .. r.Gfx] : null as byte[]);
            }, ct);
            ct.ThrowIfCancellationRequested();
            if (meshes.Count > 0)
            {
                ShowModel(e, meshes);
                return;
            }
            data = raw;
        }

        data ??= await Task.Run(() => RageResource.ReadAll(path, e.ToCarved()), ct);
        ct.ThrowIfCancellationRequested();
        _data = data;

        switch (e.Kind)
        {
            case ResourceKind.Audio:
                var tracks = await Task.Run(() => AwcDecoder.Decode(data), ct);
                ct.ThrowIfCancellationRequested();
                for (int i = 0; i < tracks.Count; i++)
                    Tracks.Add(new AudioTrack(i, tracks[i]));
                SelectedTrack = Tracks.FirstOrDefault();
                Mode = PreviewMode.Audio;
                Info = $"{tracks.Count} track(s)    {RowItem.FormatSize((ulong)data.Length)}";
                return;

            case ResourceKind.Video:
                var v = Bk2Info.Parse(data, (ulong)data.Length);
                Text = v is { } bk
                    ? $"Bink 2 video\n\nResolution   {bk.Width} x {bk.Height}\nFrames       {bk.Frames:N0}\nFrame rate   {bk.Fps:0.##} fps\nDuration     {bk.Seconds:F1} s\nSize         {RowItem.FormatSize((ulong)data.Length)}"
                    : "Bink 2 video (header not recognised)";
                Mode = PreviewMode.Video;
                Info = RowItem.FormatSize((ulong)data.Length);
                return;

            case ResourceKind.Shader:
                var cache = await Task.Run(() => Ps09Cache.Parse(data), ct);
                ct.ThrowIfCancellationRequested();
                if (cache is not null)
                {
                    _shaderCache = cache;
                    Mode = PreviewMode.Shader;
                    Info = $"Driver {cache.Driver}    {cache.Shaders.Count:N0} shaders    {cache.GraphicsPsos:N0} graphics + {cache.ComputePsos:N0} compute PSOs";
                    await ApplyShaderFilterAsync();
                    return;
                }
                break;

            case ResourceKind.Png:
                using (var ms = new MemoryStream(data))
                    Image = new Bitmap(ms);
                Mode = PreviewMode.Image;
                Info = $"{Image.PixelSize.Width} x {Image.PixelSize.Height}  PNG";
                return;

            case ResourceKind.Xml or ResourceKind.Text:
                Text = Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, MaxTextBytes));
                Mode = PreviewMode.Text;
                Info = RowItem.FormatSize((ulong)data.Length);
                return;
        }

        var hex = await Task.Run(() => Hex(data), ct);
        ct.ThrowIfCancellationRequested();
        Text = hex;
        Mode = PreviewMode.Text;
        Info = $"{_row.Type}    {RowItem.FormatSize((ulong)data.Length)}    {Categorizer.Label(e.Category)}";
    }

    private static (string Summary, string Body) DescribePlacement(RageForge.Core.World.MapPlacement mp, IReadOnlyDictionary<uint, string>? names)
    {
        string N(uint h) => names is not null && names.TryGetValue(h, out var n) ? n : $"0x{h:x8}";
        int resolved = names is null ? 0 : mp.Entities.Count(x => names.ContainsKey(x.Archetype));
        string summary = mp.IsInterior
            ? $"Interior    {mp.Entities.Count:N0} objects ({resolved:N0} with a known model)    {mp.Rooms.Count} rooms    {mp.PortalCount} portals    {mp.EntitySets.Count} entity sets"
            : $"Map placement    {mp.Entities.Count:N0} objects ({resolved:N0} with a known model)";

        var sb = new StringBuilder();
        sb.AppendLine(summary.Replace("    ", "\n"));
        sb.AppendLine();
        if (mp.IsInterior)
        {
            sb.AppendLine("Rooms:        " + string.Join("  ", mp.Rooms.Select(r => $"0x{r.Name:x8}")));
            sb.AppendLine("Entity sets:  " + string.Join("  ", mp.EntitySets.Select(s => $"0x{s.Name:x8}")));
            sb.AppendLine();
        }
        sb.AppendLine($"{"#",5}  {"Model",-48} {"Position (x, y, z)",-30} {"Rotation (x, y, z, w)",-30} Scale");
        int i = 0;
        foreach (var e in mp.Entities)
        {
            sb.Append($"{i++,5}  {N(e.Archetype),-48} ");
            sb.Append($"{e.Position.X,9:F2} {e.Position.Y,9:F2} {e.Position.Z,9:F2}   ");
            sb.Append($"{e.Rotation.X,6:F2} {e.Rotation.Y,6:F2} {e.Rotation.Z,6:F2} {e.Rotation.W,6:F2}   ");
            sb.AppendLine($"{e.ScaleXY:F2}/{e.ScaleZ:F2}");
        }
        return (summary, sb.ToString());
    }

    private static string Hex(byte[] data)
    {
        int n = Math.Min(data.Length, HexBytes);
        var sb = new StringBuilder(n * 4 + 64);
        for (int i = 0; i < n; i += 16)
        {
            int len = Math.Min(16, n - i);
            sb.Append(i.ToString("x8")).Append("   ");
            for (int k = 0; k < 16; k++)
                sb.Append(k < len ? data[i + k].ToString("x2") + " " : "   ");
            sb.Append("  ");
            for (int k = 0; k < len; k++)
            {
                byte c = data[i + k];
                sb.Append(c is >= 0x20 and < 0x7f ? (char)c : '.');
            }
            sb.AppendLine();
        }
        if (data.Length > n)
            sb.AppendLine($"... {data.Length - n:N0} more bytes");
        return sb.ToString();
    }

    partial void OnShaderFilterChanged(string value) => _ = ApplyShaderFilterAsync();

    private async Task ApplyShaderFilterAsync()
    {
        _shaderCts?.Cancel();
        _shaderCts?.Dispose();
        var cts = _shaderCts = new CancellationTokenSource();
        var cache = _shaderCache;
        if (cache is null) return;
        var f = ShaderFilter.Trim();
        List<ShaderEntry> hits;
        try
        {
            if (f.Length > 0)
                await Task.Delay(150, cts.Token);
            hits = await Task.Run(() =>
            {
                IEnumerable<ShaderEntry> items = cache.Shaders;
                if (f.Length > 0)
                    items = items.Where(s => s.Display.Contains(f, StringComparison.OrdinalIgnoreCase) || s.PsoHash.ToString("x16").Contains(f, StringComparison.OrdinalIgnoreCase));
                return items.Take(MaxListed).ToList();
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested) return;
        Shaders = new ObservableCollection<ShaderEntry>(hits);
        SelectedShader = Shaders.FirstOrDefault();
    }

    partial void OnSelectedShaderChanged(ShaderEntry? value) => _ = UpdateShaderTextAsync();

    partial void OnShowHlslChanged(bool value) => _ = UpdateShaderTextAsync();

    private async Task UpdateShaderTextAsync()
    {
        var cache = _shaderCache;
        var s = SelectedShader;
        if (cache is null || s is null)
        {
            ShaderText = string.Empty;
            return;
        }
        bool hlsl = ShowHlsl;
        var text = await Task.Run(() => hlsl ? Rdna2Disassembler.PseudoHlsl(s, cache.Code(s)) : Rdna2Disassembler.Listing(s, cache.Code(s)));
        if (s == SelectedShader && hlsl == ShowHlsl)
            ShaderText = text;
    }

    [RelayCommand]
    private void PlayAudio()
    {
        if (SelectedTrack is not { } t) return;
        try
        {
            _player.Play(t.Audio);
            IsPlaying = true;
        }
        catch (Exception ex)
        {
            Status = $"Playback failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void StopAudio()
    {
        _player.Stop();
        IsPlaying = false;
    }

    private void ShowModel(IndexEntry e, List<DecodedMesh> meshes)
    {
        _meshList = meshes;
        Meshes = meshes;
        Mode = PreviewMode.Model;
        RequestRender();
        long verts = meshes.Sum(m => (long)m.Positions.Count);
        long tris = meshes.Sum(m => (long)m.Indices.Count / 3);
        int textured = meshes.Count(m => m.Texture is not null);
        Info = $"{Categorizer.Label(e.Category)}    {meshes.Count} meshes ({textured} textured)    {verts:N0} vertices    {tris:N0} triangles";
    }

    public void Rotate(float dYaw, float dPitch)
    {
        _yaw += dYaw;
        _pitch = Math.Clamp(_pitch + dPitch, -1.5f, 1.5f);
        RequestRender();
    }

    public void Zoom(float factor)
    {
        _zoom = Math.Clamp(_zoom * factor, 0.1f, 20f);
        RequestRender();
    }

    public void ResetView()
    {
        _yaw = 0.6f;
        _pitch = 0.5f;
        _zoom = 1f;
        RequestRender();
    }

    public void SetViewport(int width, int height)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        if (width == _viewW && height == _viewH) return;
        _viewW = width;
        _viewH = height;
        RequestRender();
    }

    private async void RequestRender()
    {
        if (_meshList is null || !UseSoftware) return;
        if (_rendering)
        {
            _renderPending = true;
            return;
        }

        _rendering = true;
        try
        {
            do
            {
                _renderPending = false;
                var meshes = _meshList;
                if (meshes is null) break;
                int w = _viewW, h = _viewH;
                float yaw = _yaw, pitch = _pitch, zoom = _zoom;
                var frame = _frame;
                var rgba = await Task.Run(() => SoftwareRenderer.Render(meshes, w, h, yaw, pitch, zoom, frame));
                _frame = rgba;
                if (meshes == _meshList && !_disposed)
                    Image = BitmapFactory.FromRgba(rgba, w, h);
            }
            while (_renderPending);
        }
        catch (Exception ex)
        {
            Status = $"Render failed: {ex.Message}";
        }
        finally
        {
            _rendering = false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _player.Dispose();
        _cts.Cancel();
        _shaderCts?.Cancel();
        _shaderCts?.Dispose();
        _meshList = null;
        Meshes = null;
        Scene = null;
        Image = null;
    }
}
