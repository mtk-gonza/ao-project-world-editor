using System.Text.RegularExpressions;

namespace AoProject.WorldEditor.Infrastructure;

internal sealed record MapsToSectorsMigrationResult(
    int Migrated,
    int Skipped,
    IReadOnlyList<string> Errors);

/// <summary>Converts the legacy flat /Mapas layout into /sectors/sector-N.</summary>
internal sealed class MapsToSectorsMigrationService
{
    private static readonly Regex MapName = new(@"^mapa(?<id>\d+)\.csm$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public MapsToSectorsMigrationResult Migrate(string sourceDirectory, string destinationDirectory,
        bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException(sourceDirectory);

        var errors = new List<string>();
        var migrated = 0;
        var skipped = 0;
        foreach (var source in Directory.EnumerateFiles(sourceDirectory, "*.csm", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var match = MapName.Match(Path.GetFileName(source));
            if (!match.Success || !short.TryParse(match.Groups["id"].Value, out var sectorId))
            {
                skipped++;
                continue;
            }

            try
            {
                var sectorDirectory = Path.Combine(destinationDirectory, $"sector-{sectorId}");
                Directory.CreateDirectory(sectorDirectory);
                Copy(source, Path.Combine(sectorDirectory, "sector.csm"), overwrite);

                foreach (var extension in new[] { ".dat", ".inf" })
                {
                    var sidecar = Path.Combine(sourceDirectory, $"mapa{sectorId}{extension}");
                    if (File.Exists(sidecar))
                        Copy(sidecar, Path.Combine(sectorDirectory, "sector" + extension), overwrite);
                }
                migrated++;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(source)}: {error.Message}");
            }
        }
        return new MapsToSectorsMigrationResult(migrated, skipped, errors);
    }

    private static void Copy(string source, string destination, bool overwrite)
    {
        if (!overwrite && File.Exists(destination)) return;
        File.Copy(source, destination, overwrite);
    }
}
