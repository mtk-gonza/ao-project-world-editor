using System.Text;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using AoProject.WorldEditor.Infrastructure.Runtime;

namespace AoProject.WorldEditor.Application;

internal sealed record AuditProgress(int Current, int Total, string Phase, string File);

internal sealed record FullAuditResult(
    string Directory,
    int Maps,
    int MapErrors,
    int MapWarnings,
    int MapInfos,
    int Worlds,
    int WorldErrors,
    int WorldWarnings,
    int WorldInfos,
    int IndexFiles = 0,
    int IndexErrors = 0,
    int IndexWarnings = 0,
    int IndexInfos = 0);

/// <summary>
/// Runs the complete audit: every CSM file (format, references, round-trip)
/// and every official WORLDMAP (coordinates, coverage, exits, CsmMapSize).
/// Writes TSV/TXT evidence under artifacts/audit/&lt;timestamp&gt;.
/// </summary>
internal sealed class WorldAuditService
{
    public FullAuditResult Run(
        string mapsDirectory,
        string resourceRoot,
        WorldEditorResourceCatalog? catalog = null,
        IProgress<AuditProgress>? progress = null,
        bool roundTrip = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceRoot);
        catalog ??= WorldEditorResourceCatalog.Load(resourceRoot);
        var files = Directory.EnumerateFiles(mapsDirectory, "*.csm", SearchOption.TopDirectoryOnly)
            .Select(path => (Path: path, Id: ParseMapId(path)))
            .Where(item => item.Id > 0)
            .GroupBy(item => item.Id).Select(group => group.First())
            .OrderBy(item => item.Id)
            .ToArray();
        var available = files.Select(item => item.Id).ToHashSet();
        var mapAudit = new MapDataAuditService(catalog);
        var summaries = new List<MapAuditSummary>();
        var coordinates = new Dictionary<short, MapCoordinateInfo>();
        for (var index = 0; index < files.Length; index++)
        {
            var (path, mapId) = files[index];
            progress?.Report(new AuditProgress(index + 1, files.Length, "mapas", Path.GetFileName(path)));
            var summary = mapAudit.Audit(mapId, path, available, roundTrip);
            summaries.Add(summary);
            if (!summary.Issues.Any(issue => issue.Code == "CSM-READ" && issue.Severity == AuditSeverity.Error))
                coordinates[mapId] = new MapCoordinateInfo(mapId, summary.XMin, summary.XMax,
                    summary.YMin, summary.YMax, summary.BorderExits);
        }

        var layouts = new MapsWorldDataService().Load(Path.Combine(resourceRoot, "init", "mapsworlddata.dat"));
        progress?.Report(new AuditProgress(files.Length, files.Length, "coordenadas", "WORLDMAP"));
        var worldSummaries = new WorldCoordinateAuditService().Audit(layouts, available, coordinates);

        progress?.Report(new AuditProgress(files.Length, files.Length, "indices", "graficos.ind"));
        var indexAudit = new IndexAuditService().Audit(resourceRoot, mapsDirectory, catalog);

        var directory = Path.Combine(EditorPaths.AuditDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(directory);
        WriteMapReports(directory, summaries);
        WriteWorldReports(directory, worldSummaries);
        WriteIndexReports(directory, indexAudit);
        var result = new FullAuditResult(
            directory,
            summaries.Count,
            summaries.Sum(summary => summary.Errors),
            summaries.Sum(summary => summary.Warnings),
            summaries.Sum(summary => summary.Infos),
            worldSummaries.Count,
            worldSummaries.Sum(summary => summary.Errors),
            worldSummaries.Sum(summary => summary.Warnings),
            worldSummaries.Sum(summary => summary.Infos),
            indexAudit.Files,
            indexAudit.Errors,
            indexAudit.Warnings,
            indexAudit.Infos);
        File.WriteAllText(Path.Combine(directory, "audit-summary.txt"),
            $"AO WorldEditor - auditoria completa{Environment.NewLine}" +
            $"Fecha UTC: {DateTime.UtcNow:O}{Environment.NewLine}" +
            $"Mapas: {result.Maps} (errores={result.MapErrors}, warnings={result.MapWarnings}, info={result.MapInfos}){Environment.NewLine}" +
            $"Mundos: {result.Worlds} (errores={result.WorldErrors}, warnings={result.WorldWarnings}, info={result.WorldInfos}){Environment.NewLine}" +
            $"Indices: {indexAudit.Files} archivos (errores={indexAudit.Errors}, warnings={indexAudit.Warnings}, " +
            $"info={indexAudit.Infos}){Environment.NewLine}",
            Encoding.UTF8);
        return result;
    }

    private static void WriteIndexReports(string directory, IndexAuditResult audit)
    {
        var issues = new StringBuilder("File\tCode\tSeverity\tMessage\r\n");
        foreach (var issue in audit.Issues)
            issues.Append(Escape(issue.File)).Append('\t')
                .Append(issue.Code).Append('\t')
                .Append(issue.Severity).Append('\t')
                .Append(Escape(issue.Message)).Append("\r\n");
        File.WriteAllText(Path.Combine(directory, "index-audit.tsv"), issues.ToString(), Encoding.UTF8);

        var stats = new StringBuilder("Metric\tValue\r\n");
        foreach (var pair in audit.Stats.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            stats.Append(Escape(pair.Key)).Append('\t').Append(Escape(pair.Value)).Append("\r\n");
        File.WriteAllText(Path.Combine(directory, "index-stats.tsv"), stats.ToString(), Encoding.UTF8);
    }

    private static void WriteMapReports(string directory, IReadOnlyList<MapAuditSummary> summaries)
    {
        var issues = new StringBuilder("MapId\tFile\tCode\tSeverity\tX\tY\tLayer\tMessage\r\n");
        foreach (var summary in summaries)
        foreach (var issue in summary.Issues)
            issues.Append(summary.MapId).Append('\t')
                .Append(Escape(summary.File)).Append('\t')
                .Append(issue.Code).Append('\t')
                .Append(issue.Severity).Append('\t')
                .Append(issue.X).Append('\t')
                .Append(issue.Y).Append('\t')
                .Append(issue.Layer).Append('\t')
                .Append(Escape(issue.Message)).Append("\r\n");
        File.WriteAllText(Path.Combine(directory, "map-data-issues.tsv"), issues.ToString(), Encoding.UTF8);

        var summaryText = new StringBuilder(
            "MapId\tFile\tSize\tName\tErrors\tWarnings\tInfos\tXMin\tXMax\tYMin\tYMax\tBlocked\tTriggers\tLights\tParticles\tNpcs\tObjects\tExits\r\n");
        foreach (var summary in summaries)
            summaryText.Append(summary.MapId).Append('\t')
                .Append(Escape(summary.File)).Append('\t')
                .Append(summary.FileSize).Append('\t')
                .Append(Escape(summary.Name)).Append('\t')
                .Append(summary.Errors).Append('\t')
                .Append(summary.Warnings).Append('\t')
                .Append(summary.Infos).Append('\t')
                .Append(summary.XMin).Append('\t').Append(summary.XMax).Append('\t')
                .Append(summary.YMin).Append('\t').Append(summary.YMax).Append('\t')
                .Append(summary.Blocked).Append('\t').Append(summary.Triggers).Append('\t')
                .Append(summary.Lights).Append('\t').Append(summary.Particles).Append('\t')
                .Append(summary.Npcs).Append('\t').Append(summary.Objects).Append('\t')
                .Append(summary.Exits).Append("\r\n");
        File.WriteAllText(Path.Combine(directory, "map-data-summary.tsv"), summaryText.ToString(), Encoding.UTF8);
    }

    private static void WriteWorldReports(string directory, IReadOnlyList<WorldAuditSummary> summaries)
    {
        var issues = new StringBuilder("WorldId\tMapId\tCode\tSeverity\tChunkX\tChunkY\tGlobalX\tGlobalY\tMessage\r\n");
        foreach (var summary in summaries)
        foreach (var issue in summary.Issues)
            issues.Append(issue.WorldId).Append('\t')
                .Append(issue.MapId).Append('\t')
                .Append(issue.Code).Append('\t')
                .Append(issue.Severity).Append('\t')
                .Append(issue.ChunkX).Append('\t')
                .Append(issue.ChunkY).Append('\t')
                .Append(issue.GlobalX).Append('\t')
                .Append(issue.GlobalY).Append('\t')
                .Append(Escape(issue.Message)).Append("\r\n");
        File.WriteAllText(Path.Combine(directory, "world-coordinate-issues.tsv"), issues.ToString(), Encoding.UTF8);

        var summaryText = new StringBuilder("WorldId\tWidth\tHeight\tPlacements\tErrors\tWarnings\tInfos\r\n");
        foreach (var summary in summaries)
            summaryText.Append(summary.WorldId).Append('\t')
                .Append(summary.Width).Append('\t').Append(summary.Height).Append('\t')
                .Append(summary.Placements).Append('\t')
                .Append(summary.Errors).Append('\t').Append(summary.Warnings).Append('\t')
                .Append(summary.Infos).Append("\r\n");
        File.WriteAllText(Path.Combine(directory, "world-coordinate-summary.tsv"), summaryText.ToString(), Encoding.UTF8);
    }

    private static short ParseMapId(string path)
    {
        var digits = new string(Path.GetFileNameWithoutExtension(path).Where(char.IsDigit).ToArray());
        return short.TryParse(digits, out var id) ? id : (short)0;
    }

    private static string Escape(string value) =>
        (value ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
