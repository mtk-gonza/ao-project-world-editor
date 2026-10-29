using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

internal readonly record struct GraphicSource(
    string Path,
    int SourceX,
    int SourceY,
    int Width,
    int Height);

/// <summary>Resolves GRH metadata to existing PNG files without modifying them.</summary>
internal sealed class GraphicSourceResolver
{
    private readonly string _graphicsDirectory;
    private readonly Dictionary<int, string> _files;

    public GraphicSourceResolver(string resourceRoot)
    {
        var directory = Directory.EnumerateDirectories(resourceRoot, "Graficos", SearchOption.AllDirectories)
            .FirstOrDefault() ?? throw new DirectoryNotFoundException("No existe la carpeta Graficos.");
        _graphicsDirectory = directory;
        _files = Directory.EnumerateFiles(directory)
            .Where(path => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            .Select(path => (Path: path, Name: Path.GetFileNameWithoutExtension(path)))
            .Where(item => int.TryParse(item.Name, out _))
            .GroupBy(item => int.Parse(item.Name))
            .ToDictionary(group => group.Key, group => group.First().Path);
    }

    public GraphicSource Resolve(GraphicData graphic)
    {
        ArgumentNullException.ThrowIfNull(graphic);
        if (!_files.TryGetValue(graphic.FileNumber, out var path))
            throw new FileNotFoundException($"No existe el grafico {graphic.FileNumber}.", Path.Combine(_graphicsDirectory, graphic.FileNumber + ".png"));
        return new GraphicSource(path, graphic.SourceX, graphic.SourceY, graphic.PixelWidth, graphic.PixelHeight);
    }
}
