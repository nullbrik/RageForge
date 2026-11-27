/* RAGEFORGE - Map Grid */

using System.Numerics;

namespace RageForge.Core.World;

public readonly record struct GridCell(int X, int Y);

public sealed class MapGrid<T>
{
    private readonly Dictionary<GridCell, List<T>> _cells = [];

    public MapGrid(IEnumerable<T> items, Func<T, Vector4> bounds, float cellSize)
    {
        CellSize = cellSize;
        foreach (var item in items)
        {
            var b = bounds(item);
            if (!float.IsFinite(b.X) || !float.IsFinite(b.Y) || !float.IsFinite(b.W)) continue;
            var lo = CellOf(new Vector2(b.X - b.W, b.Y - b.W));
            var hi = CellOf(new Vector2(b.X + b.W, b.Y + b.W));
            for (int x = lo.X; x <= hi.X; x++)
                for (int y = lo.Y; y <= hi.Y; y++)
                {
                    var cell = new GridCell(x, y);
                    if (!_cells.TryGetValue(cell, out var list))
                        _cells[cell] = list = [];
                    list.Add(item);
                }
        }
    }

    public float CellSize { get; }

    public int Count => _cells.Count;

    public GridCell CellOf(Vector2 p) => new((int)MathF.Floor(p.X / CellSize), (int)MathF.Floor(p.Y / CellSize));

    public IReadOnlyList<T> ItemsOf(GridCell cell) => _cells.TryGetValue(cell, out var list) ? list : [];

    public bool Contains(GridCell cell, Vector3 p) => CellOf(new Vector2(p.X, p.Y)) == cell;

    public float Distance(GridCell cell, Vector2 p)
    {
        float x0 = cell.X * CellSize, y0 = cell.Y * CellSize;
        float dx = MathF.Max(MathF.Max(x0 - p.X, 0), p.X - (x0 + CellSize));
        float dy = MathF.Max(MathF.Max(y0 - p.Y, 0), p.Y - (y0 + CellSize));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public List<GridCell> CellsWithin(Vector2 p, float radius)
    {
        var lo = CellOf(p - new Vector2(radius));
        var hi = CellOf(p + new Vector2(radius));
        var result = new List<(GridCell Cell, float Distance)>();
        for (int x = lo.X; x <= hi.X; x++)
            for (int y = lo.Y; y <= hi.Y; y++)
            {
                var cell = new GridCell(x, y);
                if (!_cells.ContainsKey(cell)) continue;
                float d = Distance(cell, p);
                if (d <= radius) result.Add((cell, d));
            }
        return [.. result.OrderBy(r => r.Distance).Select(r => r.Cell)];
    }
}
