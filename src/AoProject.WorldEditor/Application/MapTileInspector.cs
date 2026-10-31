using System.Text;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed class MapTileInspector
{
    public string Describe(WorldSector map, int x, int y, IReadOnlyList<ObjectData>? objects = null, IReadOnlyList<NpcData>? npcs = null)
    {
        if (x < map.CsmSectorSize.XMin || x > map.CsmSectorSize.XMax ||
            y < map.CsmSectorSize.YMin || y > map.CsmSectorSize.YMax ||
            x is < 1 or > WorldSector.Width || y is < 1 or > WorldSector.Height) throw new ArgumentOutOfRangeException(nameof(x));
        var block = map.Blocks[x, y]; var text = new StringBuilder($"Posición {x},{y}");
        if (block.Blocked != 0) text.Append(" (BLOQ)");
        if (block.TileExit.SectorId != 0) text.Append($" (Trans.: {block.TileExit.SectorId},{block.TileExit.X},{block.TileExit.Y})");
        if (block.NpcIndex > 0)
        {
            var name = npcs is not null && block.NpcIndex < npcs.Count ? npcs[block.NpcIndex].Name : string.Empty;
            text.Append($" (NPC{(block.NpcIndex >= 500 ? "-Hostil" : string.Empty)}: {block.NpcIndex} - {name})");
        }
        if (block.Object.Index > 0)
        {
            var name = objects is not null && block.Object.Index < objects.Count ? objects[block.Object.Index].Name : string.Empty;
            text.Append($" (Obj: {block.Object.Index} - {name} - Cant.:{block.Object.Amount})");
        }
        text.AppendLine();
        for (var layer = 0; layer < 4; layer++) text.Append($"Capa{layer + 1}: {block.Graphics[layer].GraphicIndex}{(layer == 3 ? string.Empty : " - ")}");
        if (block.Trigger != 0) text.Append($" - Trigger: {block.Trigger}");
        return text.ToString();
    }
}
