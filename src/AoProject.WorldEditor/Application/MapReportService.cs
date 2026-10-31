using System.Text;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal enum MapReportKind { Objects, Npcs, Transfers, Layer1, Layer2, Layer3, Layer4 }

internal sealed class MapReportService
{
    public string Generate(WorldSector map, MapReportKind kind, IReadOnlyList<ObjectData>? objects = null, IReadOnlyList<NpcData>? npcs = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        var output = new StringBuilder();
        output.AppendLine(kind switch
        {
            MapReportKind.Objects => "Informe de Objetos (X,Y)", MapReportKind.Npcs => "Informe de NPCs/Hostiles (X,Y)",
            MapReportKind.Transfers => "Informe de Translados (X,Y)", _ => $"Informe de capa {(int)kind - (int)MapReportKind.Layer1 + 1}"
        });
        var minX = Math.Clamp((int)map.CsmSectorSize.XMin, 1, WorldSector.Width);
        var maxX = Math.Clamp((int)map.CsmSectorSize.XMax, 1, WorldSector.Width);
        var minY = Math.Clamp((int)map.CsmSectorSize.YMin, 1, WorldSector.Height);
        var maxY = Math.Clamp((int)map.CsmSectorSize.YMax, 1, WorldSector.Height);
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var block = map.Blocks[x, y];
            switch (kind)
            {
                case MapReportKind.Objects when block.Object.Index > 0:
                    var objectName = block.Object.Index < (objects?.Count ?? 0) ? objects![block.Object.Index].Name : string.Empty;
                    output.AppendLine($"{x},{y} tiene {block.Object.Amount} del Objeto {block.Object.Index} - {objectName}"); break;
                case MapReportKind.Npcs when block.NpcIndex > 0:
                    var npcName = block.NpcIndex < (npcs?.Count ?? 0) ? npcs![block.NpcIndex].Name : string.Empty;
                    output.AppendLine($"{x},{y} tiene {npcName}{(block.NpcIndex >= 500 ? " (Hostil)" : string.Empty)}"); break;
                case MapReportKind.Transfers when block.TileExit.SectorId != 0:
                    var warningX = ((x < 20 && block.TileExit.X < 20) || (x > 80 && block.TileExit.X > 80)) && x != block.TileExit.X ? " (X sospechoso)" : string.Empty;
                    var warningY = ((y < 20 && block.TileExit.Y < 20) || (y > 80 && block.TileExit.Y > 80)) && y != block.TileExit.Y ? " (Y sospechoso)" : string.Empty;
                    output.AppendLine($"{x},{y} nos traslada a la posición {block.TileExit.X},{block.TileExit.Y} del Mapa {block.TileExit.SectorId}{warningX}{warningY}"); break;
                default:
                    var layer = (int)kind - (int)MapReportKind.Layer1;
                    if (layer is >= 0 and < 4 && block.Graphics[layer].GraphicIndex > 0)
                        output.AppendLine($"{x},{y} tiene el grafico {block.Graphics[layer].GraphicIndex}");
                    break;
            }
        }
        return output.ToString().TrimEnd();
    }
}
