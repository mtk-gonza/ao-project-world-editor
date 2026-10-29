using System.Drawing.Imaging;
using System.Text;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

internal sealed class WorldLayoutAuditService
{
    // The audit PNG uses the canonical 100x100 "mapa grande" projection. The
    // interactive editor view uses the 75x81 playable core; they are different
    // projections of the same data, not a bug.
    public string Export(
        IReadOnlyList<MapsWorldDataService.WorldLayout> layouts,
        IReadOnlyDictionary<short, WorldMap> maps,
        IReadOnlyList<GraphicData> graphics,
        string directory)
    {
        Directory.CreateDirectory(directory);
        var service = new MapMiniMapService();
        // The layout audit is a diagnostic image: draw every layer so roofs and
        // upper floors are visible, regardless of the stock minimap defaults.
        var auditOptions = new MapMiniMapOptions(true, true, true, true);
        var report = new StringBuilder();
        report.AppendLine("AO WorldEditor - auditoria de layout oficial");
        report.AppendLine($"Fecha UTC: {DateTime.UtcNow:O}");
        var exported = 0;
        foreach (var layout in layouts)
        {
            using var image = new Bitmap(layout.Width * WorldMap.Width, layout.Height * WorldMap.Height,
                PixelFormat.Format32bppArgb);
            using var canvas = Graphics.FromImage(image);
            canvas.Clear(Color.Black);
            var missing = new List<string>();
            var empty = 0;
            for (var index = 0; index < layout.MapIds.Count; index++)
            {
                var mapId = layout.MapIds[index];
                if (mapId <= 0) { empty++; continue; }
                if (!maps.TryGetValue(mapId, out var map)) { missing.Add(mapId.ToString()); continue; }
                using var mini = service.Create(map, graphics, auditOptions);
                canvas.DrawImageUnscaled(mini, (index % layout.Width) * WorldMap.Width,
                    (index / layout.Width) * WorldMap.Height);
            }
            image.Save(Path.Combine(directory, $"worldmap{layout.WorldId}-audit.png"), ImageFormat.Png);
            report.AppendLine($"WORLDMAP{layout.WorldId}: {layout.Width}x{layout.Height}; " +
                $"faltantes={missing.Count}; celdas vacias={empty}; " +
                $"mapas faltantes=[{string.Join(",", missing)}]");
            exported++;
        }
        File.WriteAllText(Path.Combine(directory, "world-layout-audit.txt"), report.ToString(), Encoding.UTF8);
        return $"{exported} auditorías exportadas";
    }
}
