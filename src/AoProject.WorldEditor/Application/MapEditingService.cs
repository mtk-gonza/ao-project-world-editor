using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

/// <summary>Atomic map editing operations with the VB6 30-step undo limit.</summary>
internal sealed class MapEditingService
{
    private const int MaxUndo = 30;
    private readonly LinkedList<TileChange> _undo = [];

    public bool CanUndo => _undo.Count > 0;

    public void SetGraphic(WorldSector map, int x, int y, int layer, int graphicIndex)
    {
        Validate(map, x, y, layer);
        var block = map.Blocks[x, y];
        var old = block.Graphics[layer].GraphicIndex;
        if (old == graphicIndex)
            return;
        _undo.AddFirst(new TileChange(x, y, layer, old, graphicIndex));
        while (_undo.Count > MaxUndo)
            _undo.RemoveLast();
        block.Graphics[layer].GraphicIndex = graphicIndex;
        map.Info.Changed = true;
    }

    public void ClearGraphic(WorldSector map, int x, int y, int layer) => SetGraphic(map, x, y, layer, 0);

    public bool Undo(WorldSector map)
    {
        if (_undo.First is null)
            return false;
        var change = _undo.First.Value;
        _undo.RemoveFirst();
        map.Blocks[change.X, change.Y].Graphics[change.Layer].GraphicIndex = change.OldGraphic;
        map.Info.Changed = true;
        return true;
    }

    public void ClearHistory() => _undo.Clear();

    private static void Validate(WorldSector map, int x, int y, int layer)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (x < map.CsmSectorSize.XMin || x > map.CsmSectorSize.XMax ||
            y < map.CsmSectorSize.YMin || y > map.CsmSectorSize.YMax ||
            x is < 1 or > WorldSector.Width || y is < 1 or > WorldSector.Height)
            throw new ArgumentOutOfRangeException(nameof(x), "La coordenada esta fuera del mapa.");
        if (layer is < 0 or >= 4)
            throw new ArgumentOutOfRangeException(nameof(layer), "La capa debe estar entre 0 y 3.");
    }

    private readonly record struct TileChange(int X, int Y, int Layer, int OldGraphic, int NewGraphic);
}
