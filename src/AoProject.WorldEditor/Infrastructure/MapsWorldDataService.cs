using System.Text;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

internal sealed class MapsWorldDataService
{
    internal sealed record WorldLayout(int WorldId, int Width, int Height, IReadOnlyList<short> MapIds);
    public IReadOnlyList<int> IgnoredWorldIds { get; private set; } = [];

    public IReadOnlyList<WorldLayout> Load(string path)
    {
        if (!File.Exists(path)) return [];
        var sections = new List<(int Id, int Width, int Height, Dictionary<int, short> Cells)>();
        var totalWorlds = 0;
        (int Id, int Width, int Height, Dictionary<int, short> Cells)? current = null;
        foreach (var raw in File.ReadLines(path, Encoding.Default))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';') continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (current is { } completed) sections.Add(completed);
                current = null;
                var name = line[1..^1];
                if (name.Equals("INIT", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.StartsWith("WORLDMAP", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(name[8..], out var worldId)) current = (worldId, 0, 0, []);
                continue;
            }
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (current is null)
            {
                if (key.Equals("TOTALWORLDS", StringComparison.OrdinalIgnoreCase)) int.TryParse(value, out totalWorlds);
                continue;
            }
            var section = current.Value;
            if (key.Equals("ANCHO", StringComparison.OrdinalIgnoreCase)) int.TryParse(value, out section.Width);
            else if (key.Equals("ALTO", StringComparison.OrdinalIgnoreCase)) int.TryParse(value, out section.Height);
            else if (int.TryParse(key, out var index) && short.TryParse(value, out var mapId)) section.Cells[index] = mapId;
            current = section;
        }
        if (current is { } last) sections.Add(last);
        IgnoredWorldIds = sections.Where(item => totalWorlds > 0 && item.Id > totalWorlds)
            .Select(item => item.Id).ToArray();
        return sections.Where(item => totalWorlds == 0 || item.Id <= totalWorlds)
            .Where(item => item.Width > 0 && item.Height > 0)
            .Select(item => new WorldLayout(item.Id, item.Width, item.Height,
                Enumerable.Range(1, item.Width * item.Height).Select(index => item.Cells.GetValueOrDefault(index)).ToArray()))
            .ToArray();
    }

    public static short ChooseRoot(WorldLayout layout, IReadOnlyCollection<short> availableMaps)
    {
        var ids = layout.MapIds.Where(id => id > 0 && availableMaps.Contains(id)).ToArray();
        if (ids.Length == 0) return 0;
        var repeated = ids.GroupBy(id => id).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet();
        foreach (var id in ids)
            if (!repeated.Contains(id)) return id;
        return ids[0];
    }

    public static GlobalWorldAssembly? BuildAssembly(
        IReadOnlyList<WorldLayout> layouts,
        IReadOnlyDictionary<short, WorldMap> maps,
        short rootMap)
        => BuildAssembly(layouts, maps.Keys.ToArray(), rootMap);

    public static GlobalWorldAssembly? BuildAssembly(
        IReadOnlyList<WorldLayout> layouts,
        IReadOnlyCollection<short> availableMaps,
        short rootMap)
    {
        var layout = layouts.FirstOrDefault(item => item.MapIds.Contains(rootMap));
        if (layout is null) return null;
        var placements = new List<GlobalMapPlacement>();
        for (var index = 0; index < layout.MapIds.Count; index++)
        {
            var mapId = layout.MapIds[index];
            if (mapId <= 0 || !availableMaps.Contains(mapId)) continue;
            // Repeated ids (filler, e.g. 300 in WORLDMAP2) share one WorldMap
            // instance: editing one cell changes every copy.
            placements.Add(new(mapId, new(index % layout.Width, index / layout.Width), $"world-{layout.WorldId}"));
        }
        return new GlobalWorldAssembly
        {
            Placements = placements,
            Width = layout.Width,
            Height = layout.Height,
            WrapX = true
        };
    }
}
