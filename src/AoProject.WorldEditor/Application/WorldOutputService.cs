using System.Text;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using AoProject.WorldEditor.Infrastructure.Runtime;

namespace AoProject.WorldEditor.Application;

internal sealed record WorldOutputProgress(int Current, int Total, string File);

internal sealed record WorldOutputResult(
    string Directory,
    int Maps,
    int Minimaps,
    int Errors,
    IReadOnlyList<string> ErrorMessages,
    int ClientMinimaps = 0,
    int WorldMinimaps = 0);

/// <summary>
/// Generates the ancillary files the VB6 editor kept next to the maps
/// (NameMapa.dat, MiniMap/translados.ini, minimapas/*.png and the 16bpp
/// Minimapas/*.bmp the client loads) into the workspace artifacts folder.
/// The resource repository stays read-only.
/// </summary>
internal sealed class WorldOutputService
{
    private readonly SectorCsmFileService _maps = new();

    public WorldOutputResult Generate(
        string mapsDirectory,
        WorldEditorResourceCatalog? catalog = null,
        string? outputRoot = null,
        bool includeMinimaps = true,
        IProgress<WorldOutputProgress>? progress = null,
        string? referenceRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapsDirectory);
        if (includeMinimaps && catalog is null)
            throw new ArgumentNullException(nameof(catalog), "Se necesita el catalogo para generar minimapas.");
        var directory = Path.Combine(
            outputRoot ?? EditorPaths.WorldOutputDirectory,
            DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "minimapas"));
        Directory.CreateDirectory(Path.Combine(directory, "minimapas-bmp"));

        var files = Directory.EnumerateFiles(mapsDirectory, "*.csm", SearchOption.TopDirectoryOnly)
            .Select(path => (Path: path, Id: ParseSectorId(path)))
            .Where(item => item.Id > 0 && new FileInfo(item.Path).Length > 0)
            .GroupBy(item => item.Id).Select(group => group.First())
            .OrderBy(item => item.Id)
            .ToArray();
        // Start from the legacy files when available so unknown keys (the
        // mapa<id>Desc descriptions) and their order survive, exactly like
        // WritePrivateProfileString did in the VB6 editor.
        var namesPath = FindLegacyFile(referenceRoot, "init", "namemapa.dat");
        var transfersPath = FindLegacyFile(referenceRoot, "MiniMap", "translados.ini");
        var names = namesPath is null ? IniEditor.Create() : IniEditor.Load(namesPath, Encoding.Latin1);
        var transfers = transfersPath is null ? IniEditor.Create() : IniEditor.Load(transfersPath, Encoding.Latin1);
        var namesAdded = 0;
        var transfersAdded = 0;
        var minimaps = 0;
        var clientMinimaps = 0;
        var errors = new List<string>();
        var minimapService = new MapMiniMapService();
        var options = MapMiniMapOptions.Vb6Default;
        var layouts = LoadLayouts(referenceRoot);
        using var worldComposer = includeMinimaps && layouts.Count > 0
            ? new WorldMiniMapComposer(layouts, options, catalog?.Root)
            : null;
        for (var index = 0; index < files.Length; index++)
        {
            var (path, mapId) = files[index];
            progress?.Report(new WorldOutputProgress(index + 1, files.Length, Path.GetFileName(path)));
            try
            {
                var map = _maps.Load(path);
                // VB6 keys and section names are the map tab caption ("mapaN"):
                // WritePrivateProfileString("NameMapa", "mapaN", name, ...)
                // (ModCargaIAO.bas:949) and WritePrivateProfileString("mapaN", "Abajo", ...)
                // (frmMain.frm:7174-7178).
                if (names.Get("NameMapa", $"mapa{mapId}").Length == 0) namesAdded++;
                names.Set("NameMapa", $"mapa{mapId}", Sanitize(map.Info.Name));
                if (transfers.Get($"mapa{mapId}", "Abajo").Length == 0) transfersAdded++;
                transfers.Set($"mapa{mapId}", "Abajo", map.Blocks[50, 91].TileExit.SectorId.ToString());
                transfers.Set($"mapa{mapId}", "Arriba", map.Blocks[50, 10].TileExit.SectorId.ToString());
                transfers.Set($"mapa{mapId}", "Izquierda", map.Blocks[13, 50].TileExit.SectorId.ToString());
                transfers.Set($"mapa{mapId}", "Derecha", map.Blocks[88, 50].TileExit.SectorId.ToString());
                if (includeMinimaps)
                {
                    minimapService.SavePng(map, catalog!.Graphics, options, catalog.Objects,
                        Path.Combine(directory, "minimapas", $"mapa{mapId}.png"));
                    minimapService.SaveBmp(map, catalog.Graphics, options, catalog.Objects,
                        Path.Combine(directory, "minimapas-bmp", $"mapa{mapId}.bmp"));
                    minimaps++;
                    clientMinimaps++;
                    worldComposer?.Add(mapId, map, catalog.Graphics, catalog.Objects);
                }
            }
            catch (Exception error)
            {
                errors.Add($"{Path.GetFileName(path)}: {error.Message}");
            }
        }
        var worldMinimaps = 0;
        if (worldComposer is not null)
        {
            var filesWritten = worldComposer.Save(Path.Combine(directory, "worldmap"));
            worldMinimaps = worldComposer.Worlds;
            File.WriteAllLines(Path.Combine(directory, "worldmap", "worldmap-summary.txt"),
                filesWritten.Select(Path.GetFileName)!);
        }
        names.Save(Path.Combine(directory, "NameMapa.dat"), Encoding.Latin1);
        transfers.Save(Path.Combine(directory, "translados.ini"), Encoding.Latin1);
        File.WriteAllText(Path.Combine(directory, "output-summary.txt"),
            $"Mapas: {files.Length}{Environment.NewLine}" +
            $"Minimapas PNG: {minimaps}{Environment.NewLine}" +
            $"Minimapas BMP (cliente): {clientMinimaps}{Environment.NewLine}" +
            $"Minimapas de mundo: {worldMinimaps}{Environment.NewLine}" +
            $"Nombres nuevos: {namesAdded}{Environment.NewLine}" +
            $"Secciones nuevas: {transfersAdded}{Environment.NewLine}" +
            $"Errores: {errors.Count}{Environment.NewLine}" +
            $"Base legacy: {(referenceRoot is null ? "no" : "si")}{Environment.NewLine}" +
            string.Join(Environment.NewLine, errors.Select(error => $"  {error}")) +
            Environment.NewLine, Encoding.UTF8);
        return new WorldOutputResult(directory, files.Length, minimaps, errors.Count, errors,
            clientMinimaps, worldMinimaps);
    }

    private static IReadOnlyList<MapsWorldDataService.WorldLayout> LoadLayouts(string? referenceRoot)
    {
        if (string.IsNullOrWhiteSpace(referenceRoot)) return [];
        var path = FindLegacyFile(referenceRoot, "init", "mapsworlddata.dat");
        return path is null ? [] : new MapsWorldDataService().Load(path);
    }

    /// <summary>Case-insensitive lookup of a shipped legacy output file.</summary>
    private static string? FindLegacyFile(string? referenceRoot, string folder, string fileName)
    {
        if (string.IsNullOrWhiteSpace(referenceRoot)) return null;
        var directory = Path.Combine(referenceRoot, folder);
        if (!Directory.Exists(directory)) return null;
        return Directory.EnumerateFiles(directory)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName,
                StringComparison.OrdinalIgnoreCase));
    }

    private static short ParseSectorId(string path)
    {
        var digits = new string(Path.GetFileNameWithoutExtension(path).Where(char.IsDigit).ToArray());
        return short.TryParse(digits, out var id) ? id : (short)0;
    }

    private static string Sanitize(string value) =>
        (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
}
