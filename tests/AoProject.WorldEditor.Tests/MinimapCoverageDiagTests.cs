using System.Drawing;
using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Engine.Rendering;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

/// <summary>
/// Temporary diagnostic: which tiles end up unpainted (black) in the smooth
/// minimap, and why (average 0 vs palette 0).
/// </summary>
public sealed class MinimapCoverageDiagTests
{
    [Fact]
    public void Dump_unpainted_tiles()
    {
        if (Environment.GetEnvironmentVariable("AO_MINIMAP_DIAG") != "1") return;
        var root = ResourceRoot();
        if (!Directory.Exists(root)) return;
        var catalog = WorldEditorResourceCatalog.Load(root);
        var output = new List<string>();
        var service = new SectorCsmFileService();
        using var averages = new GraphicAverageColorService(root);
        var mapsDirectory = Path.Combine(root, "Mapas");
        foreach (var mapId in new short[] { 17, 842 })
        {
            var path = Path.Combine(mapsDirectory, $"mapa{mapId}.csm");
            if (!File.Exists(path)) continue;
            var map = service.Load(path);
            var black = 0;
            var total = 0;
            var needsLayer2 = 0;
            var byGraphic = new Dictionary<int, int>();
            for (var x = 1; x <= WorldSector.Width; x++)
            for (var y = 1; y <= WorldSector.Height; y++)
            {
                total++;
                var color = Color.Black;
                for (var layer = 0; layer < 4; layer++)
                {
                    if (layer != 0) continue;
                    var index = map.Blocks[x, y].Graphics[layer].GraphicIndex;
                    if (index <= 0 || index >= catalog.Graphics.Count) continue;
                    var data = catalog.Graphics[index];
                    var argb = averages.Get(data);
                    if (argb == 0 && data.MiniMapColor != 0)
                        argb = MapMiniMapService.FromColorRef(data.MiniMapColor).ToArgb();
                    if (argb != 0) color = Color.FromArgb(argb);
                    else
                    {
                        black++;
                        byGraphic[index] = byGraphic.GetValueOrDefault(index) + 1;
                    }
                }
                if (color.ToArgb() == Color.Black.ToArgb())
                {
                    // Does the tile have a visible colour on layer 2 instead?
                    var second = map.Blocks[x, y].Graphics[1].GraphicIndex;
                    if (second > 0 && second < catalog.Graphics.Count)
                    {
                        var data = catalog.Graphics[second];
                        var argb = averages.Get(data);
                        if (argb == 0 && data.MiniMapColor != 0)
                            argb = MapMiniMapService.FromColorRef(data.MiniMapColor).ToArgb();
                        if (argb != 0) needsLayer2++;
                    }
                }
            }
            output.Add($"mapa{mapId}: tiles={total} layer1_black={black} layer2_would_paint={needsLayer2}");
            foreach (var pair in byGraphic.OrderByDescending(pair => pair.Value).Take(8))
            {
                var data = catalog.Graphics[pair.Key];
                var frame = data.Frames.Length > 0 ? data.Frames[0] : 0;
                var frameInfo = frame > 0 && frame < catalog.Graphics.Count
                    ? $"frame0={frame} file={catalog.Graphics[frame].FileNumber} " +
                        $"px={catalog.Graphics[frame].PixelWidth}x{catalog.Graphics[frame].PixelHeight} " +
                        $"palette={catalog.Graphics[frame].MiniMapColor:X8} " +
                        $"avg={averages.Get(catalog.Graphics[frame]):X8}"
                    : "frame0=-";
                output.Add($"  grh {pair.Key} x{pair.Value}: file={data.FileNumber} " +
                    $"px={data.PixelWidth}x{data.PixelHeight} frames={data.Frames.Length} " +
                    $"active={data.Active} palette={data.MiniMapColor:X8} {frameInfo}");
            }
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "evidence");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "minimap-coverage.txt"), output);
    }

    [Fact]
    public void Dump_npcs_near_the_bridge()
    {
        if (Environment.GetEnvironmentVariable("AO_MINIMAP_DIAG") != "1") return;
        var root = ResourceRoot();
        if (!Directory.Exists(root)) return;
        var catalog = WorldEditorResourceCatalog.Load(root);
        var map = new SectorCsmFileService().Load(Path.Combine(root, "Mapas", "mapa17.csm"));
        var lines = new List<string>();
        for (var x = 28; x <= 32; x++)
        for (var y = 21; y <= 25; y++)
        {
            var index = map.Blocks[x, y].NpcIndex;
            if (index <= 0 || index >= catalog.Npcs.Count) continue;
            var npc = catalog.Npcs[index];
            var body = npc.Body > 0 && npc.Body < catalog.Bodies.Length ? catalog.Bodies[npc.Body] : null;
            lines.Add($"npc({x},{y}) idx={index} char={map.Blocks[x, y].CharacterIndex} npcGrh={map.Blocks[x, y].NpcIndex} name='{npc.Name}' body={npc.Body} head={npc.Head} " +
                $"heading={npc.Heading} helmet={npc.Helmet} weapon={npc.Weapon} shield={npc.Shield} " +
                $"headOffset={(body is null ? "-" : $"{body.HeadOffset.X},{body.HeadOffset.Y}")} " +
                $"walk={(body is null ? "-" : string.Join("/", Enumerable.Range(1, 4).Select(d => body.Walk[d].GraphicIndex)))} " +
                $"headGrh={(npc.Head > 0 && npc.Head < catalog.Heads.Length ? catalog.Heads[npc.Head].Head[3].GraphicIndex : 0)} " +
                $"headH={(npc.Head > 0 && npc.Head < catalog.Heads.Length && catalog.Heads[npc.Head].Head[3].GraphicIndex > 0 && catalog.Heads[npc.Head].Head[3].GraphicIndex < catalog.Graphics.Count ? catalog.Graphics[catalog.Heads[npc.Head].Head[3].GraphicIndex].PixelHeight : 0)}");
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "evidence");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "npcs-near-bridge.txt"), lines);
    }

    [Fact]
    public void Dump_void_tiles_of_the_beach()
    {
        if (Environment.GetEnvironmentVariable("AO_MINIMAP_DIAG") != "1") return;
        var root = ResourceRoot();
        if (!Directory.Exists(root)) return;
        var catalog = WorldEditorResourceCatalog.Load(root);
        var service = new SectorCsmFileService();
        var lines = new List<string>();
        foreach (var mapId in new short[] { 842, 17, 401, 407 })
        {
            var path = Path.Combine(root, "Mapas", $"mapa{mapId}.csm");
            if (!File.Exists(path)) continue;
            var map = service.Load(path);
            var zero = 0;
            var one = 0;
            var real = 0;
            var missing = 0;
            var histogram = new Dictionary<int, int>();
            for (var x = 1; x <= WorldSector.Width; x++)
            for (var y = 1; y <= WorldSector.Height; y++)
            {
                var index = map.Blocks[x, y].Graphics[0].GraphicIndex;
                if (index == 0) zero++;
                else if (index == 1) one++;
                else
                {
                    real++;
                    if (index >= catalog.Graphics.Count || catalog.Graphics[index].FileNumber <= 0) missing++;
                    histogram[index] = histogram.GetValueOrDefault(index) + 1;
                }
            }
            var top = string.Join(", ", histogram.OrderByDescending(pair => pair.Value).Take(5)
                .Select(pair => $"{pair.Key}x{pair.Value}"));
            lines.Add($"mapa{mapId}: l1 zero={zero} uno={one} real={real} sinArchivo={missing} top=[{top}]");
        }
        var grh1 = catalog.Graphics.Count > 1 ? catalog.Graphics[1] : null;
        lines.Add(grh1 is null
            ? "grh1 inexistente"
            : $"grh1: file={grh1.FileNumber} px={grh1.PixelWidth}x{grh1.PixelHeight} " +
                $"frames={grh1.Frames.Length} active={grh1.Active} palette={grh1.MiniMapColor:X8}");
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "evidence");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "void-tiles.txt"), lines);
    }

    [Fact]
    public void Dump_tiles_without_atlas_region()
    {
        if (Environment.GetEnvironmentVariable("AO_MINIMAP_DIAG") != "1") return;
        var root = ResourceRoot();
        if (!Directory.Exists(root)) return;
        var catalog = WorldEditorResourceCatalog.Load(root);
        var service = new SectorCsmFileService();
        var lines = new List<string>();
        using var atlas = new GraphicAtlas(root);
        foreach (var mapId in new short[] { 842, 17 })
        {
            var path = Path.Combine(root, "Mapas", $"mapa{mapId}.csm");
            if (!File.Exists(path)) continue;
            var map = service.Load(path);
            var missing = new Dictionary<int, int>();
            for (var x = 1; x <= WorldSector.Width; x++)
            for (var y = 1; y <= WorldSector.Height; y++)
            for (var layer = 0; layer < 4; layer++)
            {
                var index = map.Blocks[x, y].Graphics[layer].GraphicIndex;
                if (index <= 0 || index >= catalog.Graphics.Count) continue;
                if (atlas.TryResolve(catalog.Graphics[index], out _)) continue;
                missing[index] = missing.GetValueOrDefault(index) + 1;
            }
            lines.Add($"mapa{mapId}: graficos sin region={missing.Count} tiles={missing.Values.Sum()}");
            foreach (var pair in missing.OrderByDescending(pair => pair.Value).Take(10))
            {
                var data = catalog.Graphics[pair.Key];
                lines.Add($"  grh {pair.Key} x{pair.Value}: file={data.FileNumber} " +
                    $"px={data.PixelWidth}x{data.PixelHeight} src={data.SourceX},{data.SourceY} " +
                    $"frames={data.Frames.Length} active={data.Active} palette={data.MiniMapColor:X8}");
            }
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "evidence");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "missing-atlas.txt"), lines);
    }

    [Fact]
    public void Dump_tile_layers()
    {
        if (Environment.GetEnvironmentVariable("AO_MINIMAP_DIAG") != "1") return;
        var root = ResourceRoot();
        if (!Directory.Exists(root)) return;
        var catalog = WorldEditorResourceCatalog.Load(root);
        var lines = new List<string>();
        foreach (var mapId in new short[] { 842, 17 })
        {
            var path = Path.Combine(root, "Mapas", $"mapa{mapId}.csm");
            if (!File.Exists(path)) continue;
            var map = new SectorCsmFileService().Load(path);
            for (var y = 20; y <= 26; y++)
            for (var x = 28; x <= 34; x++)
            {
                var block = map.Blocks[x, y];
                var layers = string.Join(" ", Enumerable.Range(0, 4)
                    .Select(layer => block.Graphics[layer].GraphicIndex));
                lines.Add($"mapa{mapId} ({x},{y}) capas=[{layers}] obj={block.ObjectGraphic.GraphicIndex} char={block.CharacterIndex} " +
                    $"objIndex={block.Object.Index} npc={block.NpcIndex} tileExit={block.TileExit.Map}");
            }
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "evidence");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "tile-layers.txt"), lines);
    }

    [Fact]
    public void Compare_binary_index_with_text_catalog()
    {
        if (Environment.GetEnvironmentVariable("AO_MINIMAP_DIAG") != "1") return;
        var root = ResourceRoot();
        if (!Directory.Exists(root)) return;
        var ini = Path.Combine(root, "init", "graficos.ini");
        if (!File.Exists(ini)) return;
        var entries = new Dictionary<int, string>();
        var inSection = false;
        foreach (var raw in File.ReadLines(ini, System.Text.Encoding.Default))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';') continue;
            if (line[0] == '[')
            {
                inSection = line.Equals("[Graphics]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection || !line.StartsWith("Grh", StringComparison.OrdinalIgnoreCase)) continue;
            var separator = line.IndexOf('=');
            if (separator <= 3 || !int.TryParse(line[3..separator], out var index) || index <= 0) continue;
            entries[index] = line[(separator + 1)..];
        }
        // Read the binary index exactly like the editor does.
        var graphics = new GraphicIndexLoader().Load(root);
        int same = 0, diff = 0, onlyText = 0;
        var samples = new List<string>();
        foreach (var pair in entries)
        {
            var fields = pair.Value.Split('-', StringSplitOptions.TrimEntries);
            if (fields.Length < 6 || !int.TryParse(fields[0], out var frames) || frames != 1) continue;
            if (!int.TryParse(fields[1], out var file) || !int.TryParse(fields[2], out var sx) ||
                !int.TryParse(fields[3], out var sy)) continue;
            if (pair.Key >= graphics.Count || !graphics[pair.Key].Active)
            {
                onlyText++;
                continue;
            }
            var data = graphics[pair.Key];
            if (data.Frames.Length != 1) continue;
            if (data.FileNumber == file && data.SourceX == sx && data.SourceY == sy) same++;
            else
            {
                diff++;
                if (samples.Count < 10)
                    samples.Add($"grh {pair.Key}: bin={data.FileNumber},{data.SourceX},{data.SourceY} " +
                        $"txt={file},{sx},{sy}");
            }
        }
        var lines = new List<string> { $"iguales={same} distintos={diff} soloTexto={onlyText}" };
        lines.AddRange(samples);
        // Which catalog can actually be drawn? Count entries whose PNG exists.
        var directory = Directory.EnumerateDirectories(root, "Graficos", SearchOption.AllDirectories).FirstOrDefault();
        if (directory is not null)
        {
            var files = Directory.EnumerateFiles(directory)
                .Where(path => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .Where(name => int.TryParse(name, out _)).Select(int.Parse).ToHashSet();
            var binOk = 0; var binBad = 0; var txtOk = 0; var txtBad = 0;
            foreach (var data in graphics.Skip(1))
            {
                if (!data.Active || data.Frames.Length != 1 || data.FileNumber <= 0) continue;
                if (files.Contains(data.FileNumber)) binOk++; else binBad++;
            }
            foreach (var pair in entries)
            {
                var fields = pair.Value.Split('-', StringSplitOptions.TrimEntries);
                if (fields.Length < 6 || !int.TryParse(fields[0], out var frames) || frames != 1) continue;
                if (!int.TryParse(fields[1], out var file) || file <= 0) continue;
                if (files.Contains(file)) txtOk++; else txtBad++;
            }
            var totalTxt = txtOk + txtBad;
            var totalBin = binOk + binBad;
            lines.Add($"binario: conPNG={binOk} sinPNG={binBad} ({(totalBin == 0 ? 0 : 100.0 * binBad / totalBin):0.0}% sin arte)");
            lines.Add($"texto:   conPNG={txtOk} sinPNG={txtBad} ({(totalTxt == 0 ? 0 : 100.0 * txtBad / totalTxt):0.0}% sin arte)");
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "evidence");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "index-vs-catalog.txt"), lines);
    }

    [Fact]
    public void Dump_island_colors_at_53_52()
    {
        if (Environment.GetEnvironmentVariable("AO_MINIMAP_DIAG") != "1") return;
        var root = ResourceRoot();
        if (!Directory.Exists(root)) return;
        var catalog = WorldEditorResourceCatalog.Load(root);
        var map = new SectorCsmFileService().Load(Path.Combine(root, "Mapas", "mapa842.csm"));
        using var averages = new GraphicAverageColorService(root);
        var lines = new List<string>();
        for (var y = 20; y <= 28; y++)
        for (var x = 27; x <= 35; x++)
        {
            var block = map.Blocks[x, y];
            var parts = new List<string>();
            for (var layer = 0; layer < 4; layer++)
            {
                var index = block.Graphics[layer].GraphicIndex;
                if (index <= 0) { parts.Add("0"); continue; }
                var data = index < catalog.Graphics.Count ? catalog.Graphics[index] : default;
                var avg = data is null ? 0 : averages.Get(data);
                var palette = data?.MiniMapColor ?? 0;
                var final = avg != 0 ? avg : (palette != 0 ? MapMiniMapService.FromColorRef(palette).ToArgb() : 0);
                parts.Add($"{index}(avg={avg:X8} pal={palette:X8} final={final:X8})");
            }
            lines.Add($"({x},{y}) " + string.Join(" | ", parts));
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "evidence");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "island-colors.txt"), lines);
    }

    [Fact]
    public void Dump_black_tiles_with_objects()
    {
        if (Environment.GetEnvironmentVariable("AO_MINIMAP_DIAG") != "1") return;
        var root = ResourceRoot();
        if (!Directory.Exists(root)) return;
        var catalog = WorldEditorResourceCatalog.Load(root);
        var service = new SectorCsmFileService();
        using var averages = new GraphicAverageColorService(root);
        var output = new List<string>();
        foreach (var mapId in new short[] { 842, 401, 407, 17, 402 })
        {
        var map = service.Load(Path.Combine(root, "Mapas", $"mapa{mapId}.csm"));
        int black = 0, blackWithObject = 0, withObject = 0;
        int minX = 999, maxX = -1, minY = 999, maxY = -1;
        var samples = new List<string>();
        for (var y = 1; y <= WorldSector.Height; y++)
        for (var x = 1; x <= WorldSector.Width; x++)
        {
            var block = map.Blocks[x, y];
            var index = block.Graphics[0].GraphicIndex;
            var argb = 0;
            if (index > 0 && index < catalog.Graphics.Count)
            {
                argb = averages.Get(catalog.Graphics[index]);
                if (argb == 0 && catalog.Graphics[index].MiniMapColor != 0)
                    argb = MapMiniMapService.FromColorRef(catalog.Graphics[index].MiniMapColor).ToArgb();
            }
            var objectGraphic = block.ObjectGraphic.GraphicIndex;
            if (block.Object.Index > 0 && objectGraphic <= 0 && block.Object.Index < catalog.Objects.Count)
                objectGraphic = catalog.Objects[block.Object.Index].GraphicIndex;
            var hasObject = objectGraphic > 0;
            if (hasObject) withObject++;
            if (argb != 0) continue;
            black++;
            if (hasObject)
            {
                blackWithObject++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                if (samples.Count < 8)
                {
                    var regional = objectGraphic < catalog.Graphics.Count
                        ? averages.Get(catalog.Graphics[objectGraphic]) : 0;
                    samples.Add($"({x},{y}) capa1={index} objGrh={objectGraphic} objIndex={block.Object.Index} " +
                        $"objAvg={regional:X8} objPal={catalog.Graphics[objectGraphic].MiniMapColor:X8}");
                }
            }
        }
        var lines = new List<string>
        {
            $"tiles negros={black} conObjeto={blackWithObject} objetos={withObject} " +
            $"mapa{mapId} bbox=({minX},{minY})-({maxX},{maxY})"
        };
        lines.AddRange(samples);
        output.AddRange(lines);
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "evidence");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "black-with-objects.txt"), output);
    }

    [Fact]
    public void Count_black_pixels_in_smooth_minimap()
    {
        if (Environment.GetEnvironmentVariable("AO_MINIMAP_DIAG") != "1") return;
        var root = ResourceRoot();
        if (!Directory.Exists(root)) return;
        var catalog = WorldEditorResourceCatalog.Load(root);
        var service = new SectorCsmFileService();
        using var averages = new GraphicAverageColorService(root);
        var lines = new List<string>();
        foreach (var mapId in new short[] { 842, 17, 401, 407, 402 })
        {
            var path = Path.Combine(root, "Mapas", $"mapa{mapId}.csm");
            if (!File.Exists(path)) continue;
            var map = service.Load(path);
            using var minimap = new MapMiniMapService().CreateSmooth(map, catalog.Graphics, averages,
                new MapMiniMapOptions(ShowLayer2: false, ShowLayer3: false, ShowLayer4: false));
            int black = 0, transparent = 0;
            for (var y = 0; y < minimap.Height; y++)
            for (var x = 0; x < minimap.Width; x++)
            {
                var color = minimap.GetPixel(x, y);
                if (color.A == 0) transparent++;
                else if (color.R <= 8 && color.G <= 8 && color.B <= 8) black++;
            }
            lines.Add($"mapa{mapId}: negros={black} transparentes={transparent} " +
                $"px(41,56)={minimap.GetPixel(41, 56)} px(42,57)={minimap.GetPixel(42, 57)} " +
                $"px(17,11)={minimap.GetPixel(17, 11)}");
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "evidence");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "smooth-black.txt"), lines);
    }

    [Fact]
    public void Dump_world_placements_overlap()
    {
        if (Environment.GetEnvironmentVariable("AO_MINIMAP_DIAG") != "1") return;
        var root = ResourceRoot();
        if (!Directory.Exists(root)) return;
        var mapsDirectory = Path.Combine(root, "Mapas");
        var files = Directory.EnumerateFiles(mapsDirectory, "*.csm")
            .Select(path => (Path: path, Name: Path.GetFileNameWithoutExtension(path)))
            .Select(item => (item.Path, Id: short.TryParse(new string(item.Name.Where(char.IsDigit).ToArray()), out var id) ? id : (short)0))
            .Where(item => item.Id > 0).ToArray();
        var worldData = new MapsWorldDataService();
        var layouts = worldData.Load(Path.Combine(root, "init", "mapsworlddata.dat"));
        var ids = files.Select(item => item.Id).ToArray();
        var rootMap = layouts.Select(layout => MapsWorldDataService.ChooseRoot(layout, ids)).FirstOrDefault(id => id > 0);
        var assembly = MapsWorldDataService.BuildAssembly(layouts, ids, rootMap);
        var lines = new List<string>();
        if (assembly is null)
        {
            lines.Add("assembly=null");
        }
        else
        {
            lines.Add($"width={assembly.Width} height={assembly.Height} wrapX={assembly.WrapX} " +
                $"placements={assembly.Placements.Count} conflictos={assembly.Conflicts.Count} root={rootMap}");
            // Duplicate chunks and duplicate map ids.
            foreach (var group in assembly.Placements.GroupBy(item => item.Chunk).Where(g => g.Count() > 1))
                lines.Add($"CHUNK DUPLICADO ({group.Key.X},{group.Key.Y}): " +
                    string.Join(",", group.Select(item => item.SectorId)));
            foreach (var group in assembly.Placements.GroupBy(item => item.SectorId).Where(g => g.Count() > 1))
                lines.Add($"MAPA EN VARIOS CHUNKS {group.Key}: " +
                    string.Join(" ", group.Select(item => $"({item.Chunk.X},{item.Chunk.Y})")));
            foreach (var conflict in assembly.Conflicts)
                lines.Add($"conflicto: {conflict.Reason} from={conflict.FromMap} to={conflict.ToMap} " +
                    $"esperado=({conflict.Expected.X},{conflict.Expected.Y}) real=({conflict.Actual.X},{conflict.Actual.Y})");
            // Which placements cover the tiles the user reported.
            foreach (var (gx, gy) in new[] { (30, 23), (17, 11), (49, 11), (30, 22) })
            {
                var covering = assembly.Placements.Where(item =>
                {
                    var hasWest = assembly.Placements.Any(other =>
                        other.Chunk.X == item.Chunk.X - 1 && other.Chunk.Y == item.Chunk.Y);
                    var hasNorth = assembly.Placements.Any(other =>
                        other.Chunk.X == item.Chunk.X && other.Chunk.Y == item.Chunk.Y - 1);
                    var minX = hasWest ? 14 : 13;
                    var minY = hasNorth ? 11 : 10;
                    var left = item.Chunk.X * 75 + minX - 1;
                    var top = item.Chunk.Y * 81 + minY - 1;
                    var right = item.Chunk.X * 75 + 88 - 1;
                    var bottom = item.Chunk.Y * 81 + 91 - 1;
                    return gx >= left && gx <= right && gy >= top && gy <= bottom;
                }).Select(item => item.SectorId).ToArray();
                lines.Add($"global({gx},{gy}) cubierto por: {string.Join(",", covering)}");
            }
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "evidence");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "world-placements.txt"), lines);
    }

    [Fact]
    public void Dump_tiles_with_the_same_graphic()
    {
        if (Environment.GetEnvironmentVariable("AO_MINIMAP_DIAG") != "1") return;
        var root = ResourceRoot();
        if (!Directory.Exists(root)) return;
        var map = new SectorCsmFileService().Load(Path.Combine(root, "Mapas", "mapa17.csm"));
        var lines = new List<string>();
        foreach (var probe in new (int X, int Y, int Layer)[] { (17, 11, 0), (49, 11, 1), (30, 22, 0), (31, 24, 0) })
        {
            var graphic = map.Blocks[probe.X, probe.Y].Graphics[probe.Layer].GraphicIndex;
            var hits = new List<string>();
            for (var y = 1; y <= WorldSector.Height; y++)
            for (var x = 1; x <= WorldSector.Width; x++)
            for (var layer = 0; layer < 4; layer++)
                if (map.Blocks[x, y].Graphics[layer].GraphicIndex == graphic)
                    hits.Add($"({x},{y},c{layer + 1})");
            lines.Add($"grh {graphic} (en {probe.X},{probe.Y} capa{probe.Layer + 1}): {hits.Count} usos: " +
                string.Join(" ", hits.Take(24)));
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "evidence");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "graphic-usage.txt"), lines);
    }

    [Fact]
    public void Compose_the_npc_like_the_vb6_client()
    {
        if (Environment.GetEnvironmentVariable("AO_MINIMAP_DIAG") != "1") return;
        var root = ResourceRoot();
        if (!Directory.Exists(root)) return;
        var catalog = WorldEditorResourceCatalog.Load(root);
        var npc = catalog.Npcs[1249];
        var body = catalog.Bodies[npc.Body];
        var headGrh = catalog.Heads[npc.Head].Head[3].GraphicIndex;
        var bodyGrh = body.Walk[3].GraphicIndex;
        var lines = new List<string>
        {
            $"npc={npc.Name} body={npc.Body} head={npc.Head} heading=3",
            $"bodyGrh={bodyGrh} headGrh={headGrh} headOffset={body.HeadOffset.X},{body.HeadOffset.Y}"
        };
        var resolver = new GraphicSourceResolver(root);
        Bitmap? Load(int grh)
        {
            if (grh <= 0 || grh >= catalog.Graphics.Count) return null;
            var data = catalog.Graphics[grh];
            if (data.FileNumber <= 0 && data.Frames.Length > 0 && data.Frames[0] != grh)
            {
                return Load(data.Frames[0]);
            }
            if (data.FileNumber <= 0) return null;
            var source = resolver.Resolve(data);
            var sheet = new Bitmap(source.Path);
            var region = new Bitmap(source.Width, source.Height);
            using (var canvas = Graphics.FromImage(region))
                canvas.DrawImage(sheet, new Rectangle(0, 0, source.Width, source.Height),
                    new Rectangle(source.SourceX, source.SourceY, source.Width, source.Height), GraphicsUnit.Pixel);
            sheet.Dispose();
            lines.Add($"grh {grh}: file={data.FileNumber} px={data.PixelWidth}x{data.PixelHeight} " +
                $"tiles={data.TileWidth:0.##}x{data.TileHeight:0.##}");
            return region;
        }
        using var bodyImage = Load(bodyGrh);
        using var headImage = Load(headGrh);
        if (bodyImage is null || headImage is null) return;
        // VB6: canvas height 96, TextureY = 96 - 32, center=1 with the *fractional*
        // tile height, OffHead = HeadOffset.Y - 1 (BodyOffset 0).
        const int canvasHeight = 96;
        const int canvasWidth = 128;
        var textureX = canvasWidth / 2 - 16;
        var textureY = canvasHeight - 32;
        using var composed = new Bitmap(canvasWidth, canvasHeight);
        using (var canvas = Graphics.FromImage(composed))
        {
            canvas.Clear(Color.Transparent);
            var bodyTop = textureY;
            if (Math.Abs(bodyImage.Height / 32f - 1f) > 0.001f)
                bodyTop += 32 - (int)MathF.Floor(bodyImage.Height / 32f * 32f);
            var bodyLeft = textureX;
            if (Math.Abs(bodyImage.Width / 32f - 1f) > 0.001f)
                bodyLeft += 16 - (int)MathF.Floor(bodyImage.Width / 32f * 16f);
            canvas.DrawImage(bodyImage, bodyLeft, bodyTop);
            var offHead = body.HeadOffset.Y - 1;
            var headTop = textureY + offHead;
            if (Math.Abs(headImage.Height / 32f - 1f) > 0.001f)
                headTop += 32 - (int)MathF.Floor(headImage.Height / 32f * 32f);
            var headLeft = textureX + body.HeadOffset.X;
            if (Math.Abs(headImage.Width / 32f - 1f) > 0.001f)
                headLeft += 16 - (int)MathF.Floor(headImage.Width / 32f * 16f);
            canvas.DrawImage(headImage, headLeft, headTop);
            lines.Add($"bodyTop={bodyTop} bodyLeft={bodyLeft} offHead={offHead} headTop={headTop} headLeft={headLeft}");
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "evidence");
        Directory.CreateDirectory(folder);
        composed.Save(Path.Combine(folder, "npc-vb6.png"), System.Drawing.Imaging.ImageFormat.Png);
        bodyImage.Save(Path.Combine(folder, "npc-body.png"), System.Drawing.Imaging.ImageFormat.Png);
        headImage.Save(Path.Combine(folder, "npc-head.png"), System.Drawing.Imaging.ImageFormat.Png);
        File.WriteAllLines(Path.Combine(folder, "npc-compose.txt"), lines);
    }

    [Fact]
    public void Compare_shared_border_row_between_neighbour_maps()
    {
        if (Environment.GetEnvironmentVariable("AO_MINIMAP_DIAG") != "1") return;
        var root = ResourceRoot();
        if (!Directory.Exists(root)) return;
        var service = new SectorCsmFileService();
        var lines = new List<string>();
        // Vertical junctions: lower map row 11 vs upper map row 91.
        foreach (var pair in new (short Lower, short Upper)[] { (17, 203), (103, 17), (18, 204) })
        {
            var lower = service.Load(Path.Combine(root, "Mapas", $"mapa{pair.Lower}.csm"));
            var upper = service.Load(Path.Combine(root, "Mapas", $"mapa{pair.Upper}.csm"));
            var same = 0; var both = 0;
            for (var x = 1; x <= WorldSector.Width; x++)
            {
                var a = lower.Blocks[x, 11].Graphics[0].GraphicIndex;
                var b = upper.Blocks[x, 91].Graphics[0].GraphicIndex;
                if (a == 0 && b == 0) continue;
                both++; if (a == b) same++;
            }
            lines.Add($"V {pair.Upper}(91) vs {pair.Lower}(11): iguales={same} de {both}");
        }
        // Horizontal junctions: right map column 14 vs left map column 88.
        foreach (var pair in new (short Right, short Left)[] { (103, 17), (401, 842), (402, 401) })
        {
            var right = service.Load(Path.Combine(root, "Mapas", $"mapa{pair.Right}.csm"));
            var left = service.Load(Path.Combine(root, "Mapas", $"mapa{pair.Left}.csm"));
            var same = 0; var both = 0;
            for (var y = 1; y <= WorldSector.Height; y++)
            {
                var a = right.Blocks[14, y].Graphics[0].GraphicIndex;
                var b = left.Blocks[88, y].Graphics[0].GraphicIndex;
                if (a == 0 && b == 0) continue;
                both++; if (a == b) same++;
            }
            lines.Add($"H {pair.Left}(88) vs {pair.Right}(14): iguales={same} de {both}");
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "evidence");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "border-row.txt"), lines);
    }

    [Fact]
    public void Dump_stitch_shifts()
    {
        if (Environment.GetEnvironmentVariable("AO_MINIMAP_DIAG") != "1") return;
        var root = ResourceRoot();
        if (!Directory.Exists(root)) return;
        var mapsDirectory = Path.Combine(root, "Mapas");
        var files = Directory.EnumerateFiles(mapsDirectory, "*.csm")
            .Select(path => (Path: path, Name: Path.GetFileNameWithoutExtension(path)))
            .Select(item => (item.Path, Id: short.TryParse(new string(item.Name.Where(char.IsDigit).ToArray()), out var id) ? id : (short)0))
            .Where(item => item.Id > 0).ToArray();
        var worldData = new MapsWorldDataService();
        var layouts = worldData.Load(Path.Combine(root, "init", "mapsworlddata.dat"));
        var ids = files.Select(item => item.Id).ToArray();
        var rootMap = layouts.Select(layout => MapsWorldDataService.ChooseRoot(layout, ids)).FirstOrDefault(id => id > 0);
        var assembly = MapsWorldDataService.BuildAssembly(layouts, ids, rootMap);
        if (assembly is null) return;
        var service = new SectorCsmFileService();
        var paths = files.ToDictionary(item => item.Id, item => item.Path);
        var cache = new Dictionary<short, WorldSector?>();
        WorldSector? MapOf(short id)
        {
            if (cache.TryGetValue(id, out var cached)) return cached;
            WorldSector? map = null;
            if (paths.TryGetValue(id, out var path))
            {
                try { map = service.Load(path); } catch { map = null; }
            }
            cache[id] = map;
            return map;
        }
        var stitcher = new WorldBorderStitcher(assembly.Placements,
            id => MapOf(id) is { } map ? WorldBorderStitcher.Sample(map) : null);
        var stitch = stitcher.ForAll(assembly.Placements.Select(item => item.Chunk));
        var lines = new List<string>();
        foreach (var cx in assembly.Placements.Select(item => item.Chunk.X).Distinct().OrderBy(value => value))
        {
            var values = assembly.Placements.Where(item => item.Chunk.X == cx)
                .Select(item => stitch.Columns[item.Chunk]).Distinct().ToArray();
            lines.Add($"columna cx={cx}: shiftX={string.Join(",", values)}");
        }
        foreach (var cy in assembly.Placements.Select(item => item.Chunk.Y).Distinct().OrderBy(value => value))
        {
            var values = assembly.Placements.Where(item => item.Chunk.Y == cy)
                .Select(item => stitch.Rows[item.Chunk]).Distinct().ToArray();
            lines.Add($"fila cy={cy}: shiftY={string.Join(",", values)}");
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "evidence");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "stitch-shifts.txt"), lines);
    }

    private static string ResourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "ao-project-resources");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "ao-project-resources");
    }
}
