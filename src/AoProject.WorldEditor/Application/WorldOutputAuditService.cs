using System.Text;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

/// <summary>
/// Compares the generated ancillary files (NameMapa.dat, translados.ini and
/// minimapas/*.png) against the current CSM files. Never touches resources.
/// </summary>
internal sealed class WorldOutputAuditService
{
    private readonly SectorCsmFileService _maps = new();

    public IReadOnlyList<SectorAuditIssue> Audit(
        string mapsDirectory,
        string outputDirectory,
        bool checkMinimaps = true,
        string? resourceRoot = null)
    {
        var issues = new List<SectorAuditIssue>();
        var namePath = Path.Combine(outputDirectory, "NameMapa.dat");
        var transferPath = Path.Combine(outputDirectory, "translados.ini");
        if (!File.Exists(namePath))
            issues.Add(new SectorAuditIssue(0, "OUT-MISSING", AuditSeverity.Error, "Falta NameMapa.dat."));
        if (!File.Exists(transferPath))
            issues.Add(new SectorAuditIssue(0, "OUT-MISSING", AuditSeverity.Error, "Falta translados.ini."));
        if (!File.Exists(namePath) || !File.Exists(transferPath)) return issues;

        var names = IniDocument.Load(namePath, Encoding.Latin1);
        var transfers = IniDocument.Load(transferPath, Encoding.Latin1);
        var files = Directory.EnumerateFiles(mapsDirectory, "*.csm", SearchOption.TopDirectoryOnly)
            .Select(path => (Path: path, Id: ParseSectorId(path)))
            .Where(item => item.Id > 0 && new FileInfo(item.Path).Length > 0)
            .GroupBy(item => item.Id).Select(group => group.First())
            .OrderBy(item => item.Id);
        foreach (var (path, mapId) in files)
        {
            WorldSector map;
            try { map = _maps.Load(path); }
            catch (Exception error)
            {
                issues.Add(new SectorAuditIssue(mapId, "OUT-READ", AuditSeverity.Error,
                    $"No se pudo cargar el mapa: {error.Message}"));
                continue;
            }
            var expectedName = (map.Info.Name ?? string.Empty).Trim();
            var actualName = names.Get("NameMapa", $"mapa{mapId}").Trim();
            if (!string.Equals(expectedName, actualName, StringComparison.Ordinal))
                issues.Add(new SectorAuditIssue(mapId, "OUT-NAME", AuditSeverity.Warning,
                    $"NameMapa 'mapa{mapId}={actualName}' != nombre del CSM '{expectedName}'."));

            var section = $"mapa{mapId}";
            CheckTransfer(issues, mapId, transfers, section, "Abajo", map.Blocks[50, 91].TileExit.SectorId);
            CheckTransfer(issues, mapId, transfers, section, "Arriba", map.Blocks[50, 10].TileExit.SectorId);
            CheckTransfer(issues, mapId, transfers, section, "Izquierda", map.Blocks[13, 50].TileExit.SectorId);
            CheckTransfer(issues, mapId, transfers, section, "Derecha", map.Blocks[88, 50].TileExit.SectorId);

            if (!checkMinimaps) continue;
            var minimap = Path.Combine(outputDirectory, "minimapas", $"mapa{mapId}.png");
            if (!File.Exists(minimap) || new FileInfo(minimap).Length == 0)
                issues.Add(new SectorAuditIssue(mapId, "OUT-MINIMAP", AuditSeverity.Warning,
                    "Falta el minimapa PNG."));
            var clientMinimap = Path.Combine(outputDirectory, "minimapas-bmp", $"mapa{mapId}.bmp");
            if (!File.Exists(clientMinimap))
                issues.Add(new SectorAuditIssue(mapId, "OUT-MINIMAP-BMP", AuditSeverity.Warning,
                    "Falta el minimapa BMP del cliente."));
            else if (!IsClientMinimap(clientMinimap))
                issues.Add(new SectorAuditIssue(mapId, "OUT-MINIMAP-BMP", AuditSeverity.Warning,
                    "El minimapa BMP no es 100x100 16bpp BI_RGB."));
        }

        if (checkMinimaps && !string.IsNullOrWhiteSpace(resourceRoot))
            CheckWorldMinimaps(issues, outputDirectory, resourceRoot);
        return issues;
    }

    /// <summary>Each WORLDMAP must have its composed minimap at grid scale.</summary>
    private static void CheckWorldMinimaps(List<SectorAuditIssue> issues, string outputDirectory,
        string resourceRoot)
    {
        var init = Path.Combine(resourceRoot, "init");
        if (!Directory.Exists(init)) return;
        var path = Directory.EnumerateFiles(init)
            .FirstOrDefault(file => string.Equals(Path.GetFileName(file), "mapsworlddata.dat",
                StringComparison.OrdinalIgnoreCase));
        if (path is null) return;
        foreach (var layout in new MapsWorldDataService().Load(path))
        {
            var file = Path.Combine(outputDirectory, "worldmap", $"worldmap{layout.WorldId}.png");
            if (!File.Exists(file))
            {
                issues.Add(new SectorAuditIssue(0, "OUT-WORLDMAP-MINIMAP", AuditSeverity.Warning,
                    $"Falta el minimapa del WORLDMAP{layout.WorldId}."));
                continue;
            }
            var expectedWidth = layout.Width * Domain.WorldSector.Width;
            var expectedHeight = layout.Height * Domain.WorldSector.Height;
            try
            {
                using var image = System.Drawing.Image.FromFile(file);
                if (image.Width != expectedWidth || image.Height != expectedHeight)
                    issues.Add(new SectorAuditIssue(0, "OUT-WORLDMAP-SIZE", AuditSeverity.Warning,
                        $"WORLDMAP{layout.WorldId}: {image.Width}x{image.Height} != " +
                        $"{expectedWidth}x{expectedHeight}."));
            }
            catch (Exception error) when (error is IOException or OutOfMemoryException)
            {
                issues.Add(new SectorAuditIssue(0, "OUT-WORLDMAP-SIZE", AuditSeverity.Warning,
                    $"WORLDMAP{layout.WorldId}: no se pudo leer el PNG ({error.Message})."));
            }
            if (!File.Exists(Path.Combine(outputDirectory, "worldmap",
                    $"worldmap{layout.WorldId}-preview.png")))
                issues.Add(new SectorAuditIssue(0, "OUT-WORLDMAP-MINIMAP", AuditSeverity.Warning,
                    $"Falta el preview del WORLDMAP{layout.WorldId}."));
        }
    }

    /// <summary>Validates the header the client expects (16bpp, uncompressed).</summary>
    internal static bool IsClientMinimap(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (reader.ReadByte() != 'B' || reader.ReadByte() != 'M') return false;
            reader.ReadInt32();
            reader.ReadInt32();
            var offset = reader.ReadInt32();
            var headerSize = reader.ReadInt32();
            var width = reader.ReadInt32();
            var height = reader.ReadInt32();
            var planes = reader.ReadInt16();
            var bits = reader.ReadInt16();
            var compression = reader.ReadInt32();
            return offset == 54 && headerSize == 40 && width == WorldSector.Width &&
                height == WorldSector.Height && planes == 1 && bits == 16 && compression == 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the findings next to the generated files as output-audit.tsv and
    /// returns the report path.
    /// </summary>
    public string WriteReport(IReadOnlyList<SectorAuditIssue> issues, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "output-audit.tsv");
        var text = new StringBuilder("SectorId\tCode\tSeverity\tX\tY\tLayer\tMessage\r\n");
        foreach (var issue in issues)
            text.Append(issue.SectorId).Append('\t')
                .Append(issue.Code).Append('\t')
                .Append(issue.Severity).Append('\t')
                .Append(issue.X).Append('\t')
                .Append(issue.Y).Append('\t')
                .Append(issue.Layer).Append('\t')
                .Append(Escape(issue.Message)).Append("\r\n");
        File.WriteAllText(path, text.ToString(), Encoding.UTF8);
        return path;
    }

    private static void CheckTransfer(List<SectorAuditIssue> issues, short mapId, IniDocument transfers,
        string section, string key, short expected)
    {
        var value = transfers.Get(section, key);
        if (!short.TryParse(value, out var actual))
        {
            issues.Add(new SectorAuditIssue(mapId, "OUT-TRANSFER", AuditSeverity.Warning,
                $"{key}: valor ausente o invalido '{value}'."));
            return;
        }
        if (actual != expected)
            issues.Add(new SectorAuditIssue(mapId, "OUT-TRANSFER", AuditSeverity.Warning,
                $"{key}: {actual} != {expected} (TileExit del mapa)."));
    }

    private static short ParseSectorId(string path)
    {
        var digits = new string(Path.GetFileNameWithoutExtension(path).Where(char.IsDigit).ToArray());
        return short.TryParse(digits, out var id) ? id : (short)0;
    }

    private static string Escape(string value) =>
        (value ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
