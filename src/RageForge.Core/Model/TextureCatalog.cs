/* RAGEFORGE - Texture Catalog */

using System.Collections.Concurrent;
using RageForge.Core.Ttpf;

namespace RageForge.Core.Model;

public sealed class TextureCatalog
{
    private Dictionary<ulong, (TtpfStore Store, TtpfTexture Texture)> _map = [];
    private readonly ConcurrentDictionary<ulong, bool> _cutout = new();
    private List<TtpfStore> _stores = [];
    private readonly Dictionary<ulong, LinkedListNode<CacheSlot>> _cache = [];
    private readonly LinkedList<CacheSlot> _lru = new();
    private readonly Lock _gate = new();
    private long _cachedBytes;

    public uint MaxDimension { get; init; } = 2048;
    public long MaxCacheBytes { get; init; } = 256L << 20;
    public Func<TtpfFormat, bool>? Native { get; init; }

    public bool IsCutout(ulong hash) => _cutout.GetOrAdd(hash, h =>
        Decode(h) is { } t && RageForge.Core.Imaging.AlphaAnalysis.IsCutout(t.Rgba));

    public int Count => _map.Count;

    public IReadOnlyList<TtpfStore> Stores => _stores;

    public TextureCatalog Resized(uint maxDimension, long maxCacheBytes, Func<TtpfFormat, bool>? native = null) =>
        new() { _map = _map, _stores = _stores, MaxDimension = maxDimension, MaxCacheBytes = maxCacheBytes, Native = native };

    public static TextureCatalog Build(string folder, uint maxDimension = 2048)
    {
        var catalog = new TextureCatalog { MaxDimension = maxDimension };
        foreach (var file in RageForge.Core.Index.GameIndexer.FindFiles(folder, "*.ttpf"))
        {
            TtpfStore store;
            try { store = TtpfStore.Open(file); }
            catch { continue; }
            catalog._stores.Add(store);
            foreach (var t in store.Textures)
                catalog._map.TryAdd(t.Hash, (store, t));
        }
        return catalog;
    }

    public TtpfTexture? Find(ulong hash) => _map.TryGetValue(hash, out var e) ? e.Texture : null;

    public MipChain? Mips(ulong hash, uint maxDim, Func<TtpfFormat, bool> native)
    {
        if (!_map.TryGetValue(hash, out var e))
            return null;
        try
        {
            if (native(e.Texture.Format) && TtpfDecoder.ReadBlocks(e.Store, e.Texture, maxDim) is { } chain)
                return chain;
            var d = TtpfDecoder.Decode(e.Store, e.Texture, maxDim);
            return new MipChain { Format = TtpfFormat.Rgba8, Width = d.Width, Height = d.Height, Levels = [d.Rgba] };
        }
        catch
        {
            return null;
        }
    }

    public DecodedTexture? Decode(ulong hash)
    {
        if (!_map.TryGetValue(hash, out var e))
            return null;

        CacheSlot slot;
        lock (_gate)
        {
            if (_cache.TryGetValue(hash, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                slot = node.Value;
            }
            else
            {
                slot = new CacheSlot(hash, new Lazy<DecodedTexture?>(() =>
                {
                    try { return TtpfDecoder.Decode(e.Store, e.Texture, MaxDimension, Native?.Invoke(e.Texture.Format) == true) with { Key = hash }; }
                    catch { return null; }
                }));
                _cache[hash] = _lru.AddFirst(slot);
            }
        }

        var value = slot.Value.Value;
        if (value is { } v)
        {
            lock (_gate)
            {
                if (slot.Bytes == 0 && _cache.TryGetValue(hash, out var current) && current.Value == slot)
                {
                    slot.Bytes = v.Rgba.Length + (v.Blocks?.Bytes ?? 0);
                    _cachedBytes += slot.Bytes;
                }
                for (var node = _lru.Last; _cachedBytes > MaxCacheBytes && node is not null;)
                {
                    var prev = node.Previous;
                    if (node.Value != slot && node.Value.Bytes > 0)
                    {
                        _lru.Remove(node);
                        _cache.Remove(node.Value.Hash);
                        _cachedBytes -= node.Value.Bytes;
                    }
                    node = prev;
                }
            }
        }
        return value;
    }
}

internal sealed class CacheSlot(ulong hash, Lazy<DecodedTexture?> value)
{
    public ulong Hash { get; } = hash;
    public Lazy<DecodedTexture?> Value { get; } = value;
    public long Bytes { get; set; }
}
