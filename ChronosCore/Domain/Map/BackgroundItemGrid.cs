using System.Collections.Generic;

namespace Chronos.Core.Domain.Map;

public interface IBackgroundItemSpatialIndex
{
    void Build(IReadOnlyList<BackgroundItem> items);
    void Query(int camX, int camY, int vw, int vh, List<BackgroundItem> results);
    void Clear();
}

public sealed class BackgroundItemGrid : IBackgroundItemSpatialIndex
{
    private const int CellSize = 256;
    private readonly Dictionary<ulong, List<BackgroundItem>> _cells = new();

    public void Build(IReadOnlyList<BackgroundItem> items)
    {
        _cells.Clear();
        foreach (var item in items)
        {
            ulong key = Key(item.WorldX / CellSize, item.WorldY / CellSize);
            if (!_cells.TryGetValue(key, out var cell))
                _cells[key] = cell = new List<BackgroundItem>();
            cell.Add(item);
        }
    }

    public void Query(int camX, int camY, int vw, int vh, List<BackgroundItem> results)
    {
        results.Clear();
        int x0 = camX / CellSize - 1, x1 = (camX + vw)  / CellSize + 1;
        int y0 = camY / CellSize - 1, y1 = (camY + vh) / CellSize + 1;
        for (int cx = x0; cx <= x1; cx++)
        for (int cy = y0; cy <= y1; cy++)
            if (_cells.TryGetValue(Key(cx, cy), out var cell))
                results.AddRange(cell);
    }

    public void Clear() => _cells.Clear();

    private static ulong Key(int x, int y) => ((ulong)(uint)x << 32) | (uint)y;
}
