using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

internal readonly record struct MapConversionResult(string SourcePath, string DestinationPath, bool Converted);

/// <summary>Port of frmMain.cmdCovertitMap_Click: legacy .map to .csm batch conversion.</summary>
internal sealed class MapConversionService
{
    public IReadOnlyList<MapConversionResult> ConvertDirectory(string sourceDirectory, string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);
        Directory.CreateDirectory(destinationDirectory);

        var sources = Directory.EnumerateFiles(sourceDirectory, "*.map", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var legacy = new MapFileService();
        var csm = new CsmMapFileService();
        var results = new List<MapConversionResult>(sources.Length);
        foreach (var path in sources)
        {
            var destination = Path.Combine(destinationDirectory, Path.GetFileNameWithoutExtension(path) + ".csm");
            try
            {
                WorldMap map = legacy.LoadV3(path);
                csm.Save(map, destination);
                results.Add(new MapConversionResult(path, destination, true));
            }
            catch (Exception)
            {
                results.Add(new MapConversionResult(path, destination, false));
            }
        }
        return results;
    }
}
