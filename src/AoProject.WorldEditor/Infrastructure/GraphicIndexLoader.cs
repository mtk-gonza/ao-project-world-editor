using System.Text;
using System.Runtime.InteropServices;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>
/// Read-only loader for Recursos/init/graficos.ind and minimap.bin.
/// The resource repository is never written by this component.
/// </summary>
internal sealed class GraphicIndexLoader
{
    private const int TilePixels = 32;

    public IReadOnlyList<GraphicData> Load(string resourceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceRoot);
        var init = Path.Combine(resourceRoot, "init");
        var indexPath = FindCaseInsensitive(init, "graficos.ind");
        var minimapPath = FindCaseInsensitive(init, "minimap.bin");

        using var stream = File.OpenRead(indexPath);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadInt32(); // fileVersion, as in CargarIndicesDeGraficosIND
        var count = reader.ReadInt32();
        if (count <= 0 || count > 2_000_000)
            throw new InvalidDataException($"Cantidad de GRH invalida: {count}.");

        var graphics = Enumerable.Range(0, count + 1)
            .Select(_ => new GraphicData())
            .ToArray();
        var read = 0;
        var seen = new HashSet<int>();
        while (stream.Position < stream.Length && !seen.Contains(count))
        {
            var index = reader.ReadInt32();
            if (index < 1 || index > count)
                throw new InvalidDataException($"Indice GRH fuera de rango: {index}.");
            if (!seen.Add(index))
                throw new InvalidDataException($"Indice GRH duplicado: {index}.");

            var data = graphics[index];
            data.Active = true;
            var frameCount = reader.ReadInt16();
            if (frameCount <= 0)
                throw new InvalidDataException($"GRH {index} tiene una cantidad de frames invalida.");
            data.Frames = new int[frameCount];

            if (frameCount > 1)
            {
                for (var frame = 0; frame < frameCount; frame++)
                {
                    var frameIndex = reader.ReadInt32();
                    if (frameIndex < 1 || frameIndex > count)
                        throw new InvalidDataException($"Frame GRH fuera de rango: {frameIndex}.");
                    data.Frames[frame] = frameIndex;
                }
                data.Speed = reader.ReadSingle();
                if (data.Speed <= 0)
                    throw new InvalidDataException($"Velocidad invalida en GRH {index}.");
            }
            else
            {
                data.FileNumber = reader.ReadInt32();
                data.SourceX = reader.ReadInt16();
                data.SourceY = reader.ReadInt16();
                data.PixelWidth = reader.ReadInt16();
                data.PixelHeight = reader.ReadInt16();
                if (data.FileNumber <= 0 || data.SourceX < 0 || data.SourceY < 0 ||
                    data.PixelWidth <= 0 || data.PixelHeight <= 0)
                    throw new InvalidDataException($"Datos invalidos en GRH {index}.");
                data.Frames[0] = index;
                data.TileWidth = (float)data.PixelWidth / TilePixels;
                data.TileHeight = (float)data.PixelHeight / TilePixels;
            }
            read++;
        }

        if (!seen.Contains(count))
            throw new InvalidDataException("graficos.ind termina antes del GRH final.");

        foreach (var data in graphics.Skip(1).Where(item => item.Frames.Length > 1))
        {
            var first = graphics[data.Frames[0]];
            data.PixelWidth = first.PixelWidth;
            data.PixelHeight = first.PixelHeight;
            data.TileWidth = first.TileWidth;
            data.TileHeight = first.TileHeight;
        }

        LoadMinimapColors(graphics, minimapPath);
        // Some shipped map sets reference GRHs that are present in the
        // companion text catalog but are absent from the legacy binary index
        // (the current resources contain references such as 85483 while the
        // binary header stops at 61947). Keep the binary catalog authoritative
        // for entries it contains, and use the text catalog only to complete
        // missing IDs. This preserves the legacy data while preventing valid
        // map layers from disappearing from the atlas.
        var textCatalog = ReadTextCatalog(Path.Combine(init, "graficos.ini"));
        MergeMissingTextCatalog(textCatalog, ref graphics);
        // The shipped graficos.ind is not always in sync with the art: many GRHs
        // point to file numbers whose PNG does not exist (for example 15952 ->
        // 7219) while graficos.ini has the same GRH pointing to a file that does
        // (5131). Those tiles rendered black; prefer the catalog entry whenever
        // the binary one cannot be resolved.
        RepairMissingFileNumbers(graphics, resourceRoot, textCatalog);
        FillMiniMapColorsFromPng(resourceRoot, graphics);
        return graphics;
    }

    private static void LoadMinimapColors(IReadOnlyList<GraphicData> graphics, string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        for (var index = 1; index < graphics.Count; index++)
        {
            if (graphics[index].Active && reader.BaseStream.Position + sizeof(int) <= reader.BaseStream.Length)
                graphics[index].MiniMapColor = reader.ReadInt32();
        }
    }

    private static void FillMiniMapColorsFromPng(string resourceRoot, GraphicData[] graphics)
    {
        var directory = Directory.EnumerateDirectories(resourceRoot, "Graficos", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (directory is null) return;
        FillMissingMiniMapColorsFromPng(graphics, directory);
    }

    /// <summary>
    /// Completes the palette only for GRHs without a legacy minimap.bin color.
    /// </summary>
    internal static void FillMissingMiniMapColorsFromPng(GraphicData[] graphics, string graphicsDirectory)
    {
        // minimap.bin is the legacy palette (modIndices.bas:227-234) and wins.
        // The PNG average is only a fallback for GRHs that are not in that file
        // (for example entries completed from graficos.ini).
        var missing = graphics.Select((data, index) => (data, index))
            .Where(item => item.index > 0 && item.data.Active && item.data.FileNumber > 0 &&
                item.data.MiniMapColor == 0)
            .GroupBy(item => item.data.FileNumber);
        foreach (var group in missing)
        {
            var path = Path.Combine(graphicsDirectory, group.Key + ".png");
            if (!File.Exists(path)) continue;
            try
            {
                using var bitmap = new Bitmap(path);
                foreach (var (data, _) in group)
                {
                    var totalR = 0L; var totalG = 0L; var totalB = 0L; var samples = 0;
                    var stepX = Math.Max(1, data.PixelWidth / 8);
                    var stepY = Math.Max(1, data.PixelHeight / 8);
                    for (var py = (int)data.SourceY; py < data.SourceY + data.PixelHeight; py += stepY)
                    for (var px = (int)data.SourceX; px < data.SourceX + data.PixelWidth; px += stepX)
                    {
                        var pixel = bitmap.GetPixel(Math.Clamp(px, 0, bitmap.Width - 1),
                            Math.Clamp(py, 0, bitmap.Height - 1));
                        if (pixel.A == 0) continue;
                        totalR += pixel.R; totalG += pixel.G; totalB += pixel.B; samples++;
                    }
                    if (samples > 0)
                        data.MiniMapColor = (int)((totalR / samples) << 16 |
                            (totalG / samples) << 8 | totalB / samples);
                }
            }
            catch (ArgumentException) { }
            catch (ExternalException) { }
        }

        // Animations do not have a source rectangle of their own. Reuse the
        // representative color of their first resolved frame.
        foreach (var data in graphics.Where(item => item.Active && item.MiniMapColor == 0 && item.Frames.Length > 0))
        {
            var frame = data.Frames[0];
            if (frame > 0 && frame < graphics.Length)
                data.MiniMapColor = graphics[frame].MiniMapColor;
        }
    }

    /// <summary>Parses <c>init/graficos.ini</c>: index -> <c>frames-file-srcX-srcY-w-h</c>.</summary>
    private static Dictionary<int, string> ReadTextCatalog(string path)
    {
        var entries = new Dictionary<int, string>();
        if (!File.Exists(path)) return entries;
        var inGraphicsSection = false;
        foreach (var raw in File.ReadLines(path, Encoding.Default))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';') continue;
            if (line[0] == '[')
            {
                inGraphicsSection = line.Equals("[Graphics]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inGraphicsSection || !line.StartsWith("Grh", StringComparison.OrdinalIgnoreCase)) continue;
            var separator = line.IndexOf('=');
            if (separator <= 3 || !int.TryParse(line[3..separator], out var index) || index <= 0) continue;
            entries[index] = line[(separator + 1)..];
        }
        return entries;
    }

    /// <summary>
    /// Replaces file numbers that do not exist in <c>Graficos/</c> with the ones
    /// from <c>graficos.ini</c> when that file does exist. Keeps the binary index
    /// authoritative otherwise.
    /// </summary>
    private static void RepairMissingFileNumbers(GraphicData[] graphics, string resourceRoot,
        IReadOnlyDictionary<int, string> textCatalog)
    {
        if (textCatalog.Count == 0) return;
        var directory = Directory.EnumerateDirectories(resourceRoot, "Graficos", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (directory is null) return;
        var files = Directory.EnumerateFiles(directory)
            .Where(path => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => int.TryParse(name, out _))
            .Select(int.Parse)
            .ToHashSet();
        if (files.Count == 0) return;
        var repaired = 0;
        foreach (var data in graphics.Skip(1))
        {
            if (!data.Active || data.FileNumber <= 0 || files.Contains(data.FileNumber)) continue;
            if (data.Frames.Length != 1) continue;
            if (!textCatalog.TryGetValue(data.Frames[0], out var raw)) continue;
            var fields = raw.Split('-', StringSplitOptions.TrimEntries);
            if (fields.Length < 6) continue;
            if (!int.TryParse(fields[1], out var file) || !files.Contains(file)) continue;
            if (!int.TryParse(fields[2], out var sourceX) || !int.TryParse(fields[3], out var sourceY) ||
                !int.TryParse(fields[4], out var width) || !int.TryParse(fields[5], out var height) ||
                width <= 0 || height <= 0) continue;
            data.FileNumber = file;
            data.SourceX = checked((short)sourceX);
            data.SourceY = checked((short)sourceY);
            data.PixelWidth = checked((short)width);
            data.PixelHeight = checked((short)height);
            data.TileWidth = (float)width / TilePixels;
            data.TileHeight = (float)height / TilePixels;
            repaired++;
        }
        if (repaired > 0)
            System.Diagnostics.Trace.TraceInformation(
                $"WorldEditor: {repaired} GRH tomaron el archivo del catalogo de texto (graficos.ind desincronizado).");
    }

    private static void MergeMissingTextCatalog(string path, ref GraphicData[] graphics)
        => MergeMissingTextCatalog(ReadTextCatalog(path), ref graphics);

    private static void MergeMissingTextCatalog(IReadOnlyDictionary<int, string> entries, ref GraphicData[] graphics)
    {
        if (entries.Count == 0) return;
        var maxIndex = entries.Keys.Max();
        if (maxIndex >= graphics.Length)
        {
            var oldLength = graphics.Length;
            Array.Resize(ref graphics, maxIndex + 1);
            for (var index = oldLength; index < graphics.Length; index++)
                graphics[index] = new GraphicData();
        }

        // Static records first: animation records can refer to frames that
        // appear later in the text catalog.
        foreach (var entry in entries)
        {
            if (entry.Key < graphics.Length && graphics[entry.Key].Active) continue;
            var fields = entry.Value.Split('-', StringSplitOptions.TrimEntries);
            if (fields.Length < 2 || !int.TryParse(fields[0], out var frameCount) || frameCount != 1 ||
                fields.Length < 6 || !int.TryParse(fields[1], out var file) ||
                !int.TryParse(fields[2], out var sourceX) || !int.TryParse(fields[3], out var sourceY) ||
                !int.TryParse(fields[4], out var width) || !int.TryParse(fields[5], out var height) ||
                file <= 0 || width <= 0 || height <= 0) continue;
            graphics[entry.Key].Active = true;
            graphics[entry.Key].FileNumber = file;
            graphics[entry.Key].SourceX = checked((short)sourceX);
            graphics[entry.Key].SourceY = checked((short)sourceY);
            graphics[entry.Key].PixelWidth = checked((short)width);
            graphics[entry.Key].PixelHeight = checked((short)height);
            graphics[entry.Key].TileWidth = (float)width / TilePixels;
            graphics[entry.Key].TileHeight = (float)height / TilePixels;
            graphics[entry.Key].Frames = [entry.Key];
            graphics[entry.Key].Speed = 0;
        }

        foreach (var entry in entries)
        {
            if (graphics[entry.Key].Active) continue;
            var fields = entry.Value.Split('-', StringSplitOptions.TrimEntries);
            if (fields.Length < 2 || !int.TryParse(fields[0], out var frameCount) || frameCount <= 0) continue;
            if (frameCount > 1 && fields.Length >= frameCount + 2 &&
                      float.TryParse(fields[^1], System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out var speed))
            {
                var frames = new int[frameCount];
                var valid = speed > 0;
                for (var frame = 0; frame < frameCount && valid; frame++)
                    valid = int.TryParse(fields[frame + 1], out frames[frame]) &&
                        frames[frame] > 0 && frames[frame] < graphics.Length && graphics[frames[frame]].Active;
                if (!valid) continue;
                var data = graphics[entry.Key];
                data.Active = true;
                data.FileNumber = 0;
                data.Frames = frames;
                data.Speed = speed;
                var first = graphics[frames[0]];
                data.PixelWidth = first.PixelWidth;
                data.PixelHeight = first.PixelHeight;
                data.TileWidth = first.TileWidth;
                data.TileHeight = first.TileHeight;
            }
        }
    }

    private static string FindCaseInsensitive(string directory, string fileName)
    {
        var exact = Path.Combine(directory, fileName);
        if (File.Exists(exact))
            return exact;
        var match = Directory.EnumerateFiles(directory)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new FileNotFoundException($"No existe el recurso {fileName}.", exact);
    }
}
