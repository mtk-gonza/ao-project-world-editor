using System.Diagnostics;
using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using AoProject.WorldEditor.Domain.Rendering;
using AoProject.WorldEditor.Engine.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace AoProject.WorldEditor.Tests;

/// <summary>
/// Heavy world-map diagnostics. They load hundreds of real maps and are not
/// part of the fast suite. Run explicitly with AO_WORLD_AUDIT=1:
///   dotnet test --filter FullyQualifiedName~WorldMapAudit
/// The report is appended to %TEMP%/ao-world-audit-&lt;timestamp&gt;.txt.
/// </summary>
[Trait("Category", "Audit")]
public sealed class WorldMapAuditTests
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("AO_WORLD_AUDIT") == "1";
    private static readonly string ReportPath = Path.Combine(Path.GetTempPath(),
        $"ao-world-audit-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

    private readonly ITestOutputHelper _output;
    public WorldMapAuditTests(ITestOutputHelper output) => _output = output;

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

    private static Dictionary<short, string> MapFiles(string mapsDir) =>
        Directory.EnumerateFiles(mapsDir, "*.csm")
            .Select(path => (Path: path, Id: short.TryParse(
                new string(Path.GetFileNameWithoutExtension(path).Where(char.IsDigit).ToArray()), out var id) ? id : (short)0))
            .Where(item => item.Id > 0 && new FileInfo(item.Path).Length > 0)
            .GroupBy(item => item.Id)
            .Select(group => group.First())
            .ToDictionary(item => item.Id, item => item.Path);

    private void Write(string title, IEnumerable<string> lines)
    {
        var report = lines.ToArray();
        foreach (var line in report) _output.WriteLine(line);
        var block = new List<string> { "", $"== {title} ==", $"date={DateTime.UtcNow:O}" };
        block.AddRange(report);
        File.AppendAllLines(ReportPath, block);
    }

    [Fact]
    public void Dataset_and_exit_geometry()
    {
        if (!Enabled) return;
        var root = ResourceRoot();
        var mapsDir = Path.Combine(root, "Mapas");
        if (!Directory.Exists(mapsDir)) { Write("dataset", new[] { "NO RESOURCES" }); return; }

        var files = MapFiles(mapsDir);
        var layouts = new MapsWorldDataService().Load(Path.Combine(root, "init", "mapsworlddata.dat"));
        var lines = new List<string> { $"csm files: {files.Count}", $"layouts: {layouts.Count}" };

        foreach (var layout in layouts)
        {
            var ids = layout.SectorIds.Where(id => id > 0).ToArray();
            var dups = ids.GroupBy(id => id).Where(group => group.Count() > 1)
                .OrderByDescending(group => group.Count())
                .Select(group => $"{group.Key}x{group.Count()}").Take(5);
            var missing = ids.Where(id => !files.ContainsKey(id)).Distinct().Count();
            lines.Add($"WORLDMAP{layout.WorldId}: {layout.Width}x{layout.Height} cells={layout.SectorIds.Count} " +
                $"identity={ids.Length} distinct={ids.Distinct().Count()} missing={missing} " +
                $"dups=[{string.Join(",", dups)}]");
        }

        var loader = new SectorCsmFileService();
        var exits = new Dictionary<short, List<(int X, int Y, short To, int TX, int TY)>>();
        var paintedFull = 0;
        var loaded = 0;
        var failed = new List<string>();
        var sw = Stopwatch.StartNew();
        foreach (var (mapId, path) in files)
        {
            WorldSector map;
            try { map = loader.Load(path); }
            catch (Exception error) { failed.Add($"{mapId}:{error.GetType().Name}"); continue; }
            loaded++;
            var painted = 0;
            for (var x = 1; x <= WorldSector.Width; x++)
            for (var y = 1; y <= WorldSector.Height; y++)
            {
                var block = map.Blocks[x, y];
                if (block.TileExit.SectorId > 0)
                {
                    if (!exits.TryGetValue(mapId, out var list)) exits[mapId] = list = [];
                    list.Add((x, y, block.TileExit.SectorId, block.TileExit.X, block.TileExit.Y));
                }
                for (var layer = 0; layer < block.Graphics.Length; layer++)
                    if (block.Graphics[layer].GraphicIndex > 0) { painted++; break; }
            }
            if (painted == WorldSector.Width * WorldSector.Height) paintedFull++;
        }
        lines.Add($"loaded={loaded} failed={failed.Count} [{string.Join(",", failed.Take(10))}] " +
            $"elapsed={sw.ElapsedMilliseconds}ms fullyPainted100x100={paintedFull}");

        static string Classify(int sx, int sy, int tx, int ty) => (sx, sy, tx, ty) switch
        {
            (13, _, 87, _) => "W 13->87",
            (88, _, 14, _) => "E 88->14",
            (_, 10, _, 90) => "N 10->90",
            (_, 91, _, 11) => "S 91->11",
            _ => "other"
        };
        var cross = exits.SelectMany(pair => pair.Value)
            .Where(exit => files.ContainsKey(exit.To)).ToArray();
        lines.Add($"cross-map exits: {cross.Length}");
        foreach (var group in cross.GroupBy(exit => Classify(exit.X, exit.Y, exit.TX, exit.TY))
                     .OrderByDescending(group => group.Count()))
            lines.Add($"  class {group.Key}: {group.Count()}");

        // NEXUS deltas against the official grid (WORLDMAP1)
        var layout1 = layouts.FirstOrDefault(layout => layout.WorldId == 1);
        if (layout1 is not null)
        {
            var cellOf = new Dictionary<short, (int X, int Y)>();
            for (var index = 0; index < layout1.SectorIds.Count; index++)
                if (layout1.SectorIds[index] > 0) cellOf[layout1.SectorIds[index]] = (index % layout1.Width, index / layout1.Width);
            var deltas = new Dictionary<string, int>();
            foreach (var (mapId, list) in exits)
            {
                if (!cellOf.TryGetValue(mapId, out var cell)) continue;
                foreach (var exit in list)
                {
                    if (!cellOf.TryGetValue(exit.To, out var target)) continue;
                    var side = exit.X == 13 ? "W" : exit.X == 88 ? "E" : exit.Y == 10 ? "N" : exit.Y == 91 ? "S" : null;
                    if (side is null) continue;
                    var key = $"{side} d=({target.X - cell.X},{target.Y - cell.Y})";
                    deltas[key] = deltas.GetValueOrDefault(key) + 1;
                }
            }
            foreach (var delta in deltas.OrderByDescending(pair => pair.Value).Take(12))
                lines.Add($"  nexus {delta.Key}: {delta.Value}");
        }
        Write("dataset and exits", lines);
    }

    [Fact]
    public void Memory_preload_and_snapshot()
    {
        if (!Enabled) return;
        var root = ResourceRoot();
        var mapsDir = Path.Combine(root, "Mapas");
        if (!Directory.Exists(mapsDir)) return;
        var files = MapFiles(mapsDir);
        var loader = new SectorCsmFileService();
        var lines = new List<string>();

        var baseline = GC.GetTotalMemory(true);
        var maps = new List<WorldSector>();
        var sw = Stopwatch.StartNew();
        foreach (var (_, path) in files.OrderBy(item => item.Key))
        {
            try { maps.Add(loader.Load(path)); } catch { }
            if (maps.Count % 200 == 0)
                lines.Add($"{maps.Count} maps: {(GC.GetTotalMemory(false) - baseline) / 1024d / 1024d:F1} MB, " +
                    $"elapsed={sw.ElapsedMilliseconds}ms");
        }
        var mapBytes = GC.GetTotalMemory(false) - baseline;
        lines.Add($"TOTAL {maps.Count} maps: {mapBytes / 1024d / 1024d:F1} MB, " +
            $"{mapBytes / 1024d / 1024d / Math.Max(1, maps.Count):F2} MB/map, elapsed={sw.ElapsedMilliseconds}ms");
        maps.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var sessionBaseline = GC.GetTotalMemory(true);
        var session = new WorldEditorSession(root);
        sw.Restart();
        var world = session.LoadWorld(mapsDir);
        var openBytes = GC.GetTotalMemory(false) - sessionBaseline;
        lines.Add($"LoadWorld (lazy): loaded={world.LoadedMapCount} placements={world.Assembly.Placements.Count} " +
            $"elapsed={sw.ElapsedMilliseconds}ms memory={openBytes / 1024d / 1024d:F1} MB");

        var visibleSample = world.Assembly.Placements.Select(item => item.SectorId).Distinct().Take(9).ToArray();
        foreach (var mapId in visibleSample)
            try { _ = world.Maps[mapId]; } catch { }
        var visibleBytes = GC.GetTotalMemory(false) - sessionBaseline;
        lines.Add($"Lazy sample {visibleSample.Length} maps: loaded={world.LoadedMapCount} " +
            $"memory={visibleBytes / 1024d / 1024d:F1} MB");
        world.TrimLoadedMaps(Array.Empty<short>());
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var trimmedBytes = GC.GetTotalMemory(false) - sessionBaseline;
        lines.Add($"After Trim: loaded={world.LoadedMapCount} memory={trimmedBytes / 1024d / 1024d:F1} MB");

        var undoBaseline = GC.GetTotalMemory(true);
        var singleMapSnapshot = GlobalWorldSnapshot.Capture(world, new[] { world.RootMap });
        lines.Add($"Global undo snapshot 1 map: {singleMapSnapshot.MapCount} maps, " +
            $"{(GC.GetTotalMemory(false) - undoBaseline) / 1024d / 1024d:F1} MB");
        GC.KeepAlive(singleMapSnapshot);

        sw.Restart();
        session.PreloadWorld();
        lines.Add($"PreloadWorld: loaded={world.LoadedMapCount} errors={world.LoadErrors.Count} " +
            $"elapsed={sw.ElapsedMilliseconds}ms memory={(GC.GetTotalMemory(false) - sessionBaseline) / 1024d / 1024d:F1} MB");
        world = null!;
        session = null!;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Snapshot cost on a bounded sample; the full world scales linearly.
        var sample = new Dictionary<short, WorldSector>();
        foreach (var (mapId, path) in files.OrderBy(item => item.Key).Take(100))
            try { sample[mapId] = loader.Load(path); } catch { }
        var snapshotBaseline = GC.GetTotalMemory(true);
        var snapshots = new List<WorldSectorSnapshot>();
        sw.Restart();
        foreach (var map in sample.Values) snapshots.Add(WorldSectorSnapshot.Capture(map));
        var snapshotBytes = GC.GetTotalMemory(false) - snapshotBaseline;
        lines.Add($"snapshot {sample.Count} maps: {snapshotBytes / 1024d / 1024d:F1} MB, " +
            $"{snapshotBytes / 1024d / 1024d / Math.Max(1, sample.Count):F2} MB/map, elapsed={sw.ElapsedMilliseconds}ms; " +
            $"extrapolated {files.Count} maps = {snapshotBytes / 1024d / 1024d / Math.Max(1, sample.Count) * files.Count:F0} MB");
        snapshots.Clear();
        GC.KeepAlive(sample);
        Write("memory, preload and snapshot", lines);
    }

    [Fact]
    public void Wrap_assembler_and_validator()
    {
        if (!Enabled) return;
        var root = ResourceRoot();
        var mapsDir = Path.Combine(root, "Mapas");
        if (!Directory.Exists(mapsDir)) return;
        var files = MapFiles(mapsDir);
        var layouts = new MapsWorldDataService().Load(Path.Combine(root, "init", "mapsworlddata.dat"));
        var loader = new SectorCsmFileService();
        var lines = new List<string>();

        foreach (var layout in layouts)
        {
            var rootSectorId = layout.SectorIds.FirstOrDefault(id => id > 0 && files.ContainsKey(id));
            if (rootSectorId <= 0) { lines.Add($"WORLDMAP{layout.WorldId}: no root"); continue; }
            var maps = new Dictionary<short, WorldSector>();
            foreach (var mapId in layout.SectorIds.Where(id => id > 0 && files.ContainsKey(id)).Distinct())
                try { maps[mapId] = loader.Load(files[mapId]); } catch { }

            var assembly = MapsWorldDataService.BuildAssembly(layouts, files.Keys.ToArray(), rootSectorId)!;
            var validatorIssues = new GlobalWorldLayoutValidator().Validate(assembly, maps, rootSectorId);
            var discovered = new GlobalWorldAssembler().Assemble(maps, rootSectorId);
            var officialCell = new Dictionary<short, (int X, int Y)>();
            for (var index = 0; index < layout.SectorIds.Count; index++)
                if (layout.SectorIds[index] > 0) officialCell[layout.SectorIds[index]] = (index % layout.Width, index / layout.Width);
            var identityCount = layout.SectorIds.Count(id => id > 0);
            var exact = 0;
            var modulo = 0;
            if (officialCell.Count == identityCount)
            {
                foreach (var placement in discovered.Placements)
                {
                    if (!officialCell.TryGetValue(placement.SectorId, out var official)) continue;
                    if (official == (placement.Chunk.X, placement.Chunk.Y)) exact++;
                    var normalizedX = ((placement.Chunk.X % layout.Width) + layout.Width) % layout.Width;
                    if (official == (normalizedX, placement.Chunk.Y)) modulo++;
                }
            }
            lines.Add($"WORLDMAP{layout.WorldId}: maps={maps.Count} placements={assembly.Placements.Count} " +
                $"validatorIssues={validatorIssues.Count} bfsPlacements={discovered.Placements.Count} " +
                (officialCell.Count == identityCount
                    ? $"bfsExact={exact} bfsModuloX={modulo} "
                    : "bfsExact=skip(filler) bfsModuloX=skip(filler) ") +
                $"conflicts={discovered.Conflicts.Count} " +
                $"top=[{string.Join(" | ", validatorIssues.GroupBy(issue => issue.Message)
                    .OrderByDescending(group => group.Count()).Take(3).Select(group => $"{group.Count()}x {group.Key}"))}]");
            maps.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Write("wrap, assembler and validator", lines);
    }

    [Fact]
    public void Overlay_cost_and_roundtrip()
    {
        if (!Enabled) return;
        var root = ResourceRoot();
        var mapsDir = Path.Combine(root, "Mapas");
        var lines = new List<string>();
        if (Directory.Exists(mapsDir))
        {
            var files = MapFiles(mapsDir);
            var layouts = new MapsWorldDataService().Load(Path.Combine(root, "init", "mapsworlddata.dat"));
            var layout = layouts.FirstOrDefault(item => item.WorldId == 1);
            if (layout is not null)
            {
                var loader = new SectorCsmFileService();
                var maps = new Dictionary<short, WorldSector>();
                foreach (var mapId in layout.SectorIds.Where(id => id > 0 && files.ContainsKey(id)).Distinct())
                    try { maps[mapId] = loader.Load(files[mapId]); } catch { }

                // Whole-world sweep (old behaviour) vs a 9-map window that
                // approximates what the viewport-limited path builds per frame.
                var window = layout.SectorIds.Where(id => id > 0 && maps.ContainsKey(id)).Take(9).ToArray();
                foreach (var (label, subset) in new[]
                {
                    ("all", maps.Keys.ToArray()),
                    ("window9", window)
                })
                foreach (var options in new[]
                {
                    new WorldRenderOptions(),
                    new WorldRenderOptions { ShowGrid = true },
                    new WorldRenderOptions { ShowBlocked = true, ShowTriggers = true, ShowTransfers = true }
                })
                {
                    var sw = Stopwatch.StartNew();
                    var count = 0;
                    for (var pass = 0; pass < 3; pass++)
                    {
                        count = 0;
                        foreach (var mapId in subset)
                            if (maps.TryGetValue(mapId, out var map))
                                count += MapOverlayGeometry.Build(map, options).Count;
                    }
                    lines.Add($"overlays {label} grid={options.ShowGrid} markers={options.ShowBlocked}: " +
                        $"count={count} perPass={sw.ElapsedMilliseconds / 3d:F1}ms");
                }
                maps.Clear();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        var assembly = new GlobalWorldAssembly
        {
            Placements =
            [
                new GlobalSectorPlacement(1, new GlobalChunkCoordinate(0, 0), "surface"),
                new GlobalSectorPlacement(2, new GlobalChunkCoordinate(1, 0), "surface")
            ]
        };
        var document = new GlobalWorldDocument(assembly,
            new Dictionary<short, WorldSector> { [1] = new(), [2] = new() },
            new Dictionary<short, string> { [1] = "1.csm", [2] = "2.csm" }, 1);
        foreach (var (mapId, x, y) in new (short SectorId, int X, int Y)[]
                 {
                     (2, 14, 50), (2, 1, 50), (2, 13, 50), (2, 88, 50),
                     (2, 100, 50), (1, 1, 50), (1, 13, 50), (1, 88, 50), (1, 100, 50)
                 })
        {
            if (!document.TryGetGlobalTile(mapId, x, y, out var global))
            {
                lines.Add($"roundtrip map{mapId}({x},{y}) -> rejected (hidden border)");
                continue;
            }
            document.TryResolve(global.X, global.Y, out var location);
            lines.Add($"roundtrip map{mapId}({x},{y})->global({global.X},{global.Y})->" +
                $"map{location.SectorId}({location.LocalX},{location.LocalY})");
        }
        Write("overlay cost and roundtrip", lines);
    }
}
