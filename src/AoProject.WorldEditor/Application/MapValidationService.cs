using System.Text;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct MapValidationIssue(int X, int Y, string Message);

internal sealed class MapValidationService
{
    public IReadOnlyList<MapValidationIssue> Validate(WorldMap map, IReadOnlyList<ObjectData>? objects = null, IReadOnlyList<NpcData>? npcs = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        var issues = new List<MapValidationIssue>();
        var minX = Math.Clamp((int)map.CsmMapSize.XMin, 1, WorldMap.Width);
        var maxX = Math.Clamp((int)map.CsmMapSize.XMax, 1, WorldMap.Width);
        var minY = Math.Clamp((int)map.CsmMapSize.YMin, 1, WorldMap.Height);
        var maxY = Math.Clamp((int)map.CsmMapSize.YMax, 1, WorldMap.Height);
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            var block = map.Blocks[x, y];
            if (block.NpcIndex > 0 && (npcs is null || block.NpcIndex >= npcs.Count || npcs[block.NpcIndex] is null))
                issues.Add(new(x, y, $"NPC inválido: {block.NpcIndex}"));
            if (block.Object.Index > 0 && (objects is null || block.Object.Index >= objects.Count || objects[block.Object.Index] is null))
                issues.Add(new(x, y, $"Objeto inválido: {block.Object.Index}"));
            if (block.TileExit.Map != 0 && (block.TileExit.X is < 1 or > 100 || block.TileExit.Y is < 1 or > 100))
                issues.Add(new(x, y, "Traslado con destino fuera de rango."));
            if (!IsBlocked(map, x, y) && IsBlocked(map, x - 1, y) && IsBlocked(map, x + 1, y) && IsBlocked(map, x, y - 1) && IsBlocked(map, x, y + 1))
                issues.Add(new(x, y, "Falta bloqueo en una celda rodeada."));
        }
        return issues;
    }

    public string Format(IReadOnlyList<MapValidationIssue> issues)
    {
        var builder = new StringBuilder("Informe de errores del mapa");
        foreach (var issue in issues) builder.AppendLine().Append(issue.X).Append(", ").Append(issue.Y).Append(" :::: ").Append(issue.Message);
        return builder.ToString();
    }

    private static bool IsBlocked(WorldMap map, int x, int y) =>
        x < map.CsmMapSize.XMin || x > map.CsmMapSize.XMax ||
        y < map.CsmMapSize.YMin || y > map.CsmMapSize.YMax ||
        x is < 1 or > WorldMap.Width || y is < 1 or > WorldMap.Height ||
        (map.Blocks[x, y].Blocked & 0xF) == 0xF;
}
