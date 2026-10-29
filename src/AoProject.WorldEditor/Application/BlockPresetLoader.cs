using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

internal sealed class BlockPresetType
{
    public int Number { get; init; }
    public string Name { get; init; } = string.Empty;
    public int TilesX { get; init; }
    public int TilesY { get; init; }
    public int Layer { get; init; }
    public IReadOnlyList<BlockPreset> Items { get; init; } = [];
}

internal sealed class BlockPreset
{
    public string Name { get; init; } = string.Empty;
    public int GraphicIndex { get; init; }
}

/// <summary>Read-only loader for the reference block database.</summary>
internal sealed class BlockPresetLoader
{
    public IReadOnlyList<BlockPresetType> Load(string path)
    {
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
                items.Add(new BlockPreset { Name = document.Get(section, "NOMBRE" + item), GraphicIndex = document.GetInt(section, "GRH" + item) });
            result.Add(new BlockPresetType
            {
                Number = document.GetInt("CABECERA", "NRO" + index),
                Name = name,
                TilesX = document.GetInt("CABECERA", "TILESX" + index),
                TilesY = document.GetInt("CABECERA", "TILESY" + index),
                Layer = document.GetInt("CABECERA", "LAYER" + index),
                Items = items
            });
        }
        return result;
    }
}
