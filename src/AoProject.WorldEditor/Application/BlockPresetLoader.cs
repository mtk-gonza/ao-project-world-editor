using AoProject.WorldEditor.Infrastructure;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed class BlockPresetType
{
    public int Number { get; init; }
    public string Name { get; init; } = string.Empty;
    public int TilesX { get; init; }
    public int TilesY { get; init; }
    public int Layer { get; init; }
    public string Category { get; init; } = string.Empty;
    public IReadOnlyList<BlockPreset> Items { get; init; } = [];
}

internal sealed class BlockPreset
{
    public string Name { get; init; } = string.Empty;
    public int GraphicIndex { get; init; }
    public byte DefaultBlockedMask { get; init; }
}

/// <summary>Read-only loader for the reference block database.</summary>
internal sealed class BlockPresetLoader
{
    public IReadOnlyList<BlockPresetType> Load(string path, BlockPresetLoaderOptions? options = null)
    {
        options ??= new BlockPresetLoaderOptions();
        if (!BlockedMask.IsValid(options.DefaultBlockedMask))
            throw new ArgumentOutOfRangeException(nameof(options), "La máscara por defecto debe usar sólo los bits 1,2,4,8.");
        var document = IniDocument.Load(path);
        var count = document.GetInt("CABECERA", "NTIPOS");
        var result = new List<BlockPresetType>(count);
        for (var index = 1; index <= count; index++)
        {
            var name = document.Get("CABECERA", "TIPO" + index);
            var section = string.IsNullOrWhiteSpace(name) ? string.Empty : name;
            var itemCount = document.GetInt(section, "NITEMS");
            var items = new List<BlockPreset>(itemCount);
            for (var item = 1; item <= itemCount; item++)
            {
                var maskText = document.Get(section, "BLOQUEO" + item);
                if (maskText.Length == 0) maskText = document.Get(section, "BLOCKED" + item);
                var mask = byte.TryParse(maskText, out var parsed) && BlockedMask.IsValid(parsed)
                    ? parsed : options.DefaultBlockedMask;
                items.Add(new BlockPreset { Name = document.Get(section, "NOMBRE" + item), GraphicIndex = document.GetInt(section, "GRH" + item), DefaultBlockedMask = mask });
            }
            result.Add(new BlockPresetType
            {
                Number = document.GetInt("CABECERA", "NRO" + index),
                Name = name,
                TilesX = document.GetInt("CABECERA", "TILESX" + index),
                TilesY = document.GetInt("CABECERA", "TILESY" + index),
                Layer = document.GetInt("CABECERA", "LAYER" + index),
                Category = document.Get("CABECERA", "CATEGORIA" + index),
                Items = items
            });
        }
        return result;
    }
}

/// <summary>Explicit compatibility policy for old bloq_database.txt files.</summary>
internal sealed record BlockPresetLoaderOptions(byte DefaultBlockedMask = 0);
