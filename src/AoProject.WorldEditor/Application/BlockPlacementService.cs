using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

/// <summary>Places a rectangular block using the same sequential GRH rule as PonerGrh.</summary>
internal sealed class BlockPlacementService
{
    public int Place(WorldMap map, BlockPresetType preset, BlockPreset item, int startX, int startY)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(item);
        if (preset.Layer is < 1 or > 4 || preset.TilesX <= 0 || preset.TilesY <= 0)
            throw new InvalidDataException("Preset de bloque invalido.");

        var placed = 0;
        for (var y = 0; y < preset.TilesY; y++)
        for (var x = 0; x < preset.TilesX; x++)
        {
            var targetX = startX + x;
            var targetY = startY + y;
            if (targetX is < 1 or > WorldMap.Width || targetY is < 1 or > WorldMap.Height)
                continue;
            map.Blocks[targetX, targetY].Graphics[preset.Layer - 1].GraphicIndex = item.GraphicIndex + placed;
            placed++;
        }
        if (placed > 0) map.Info.Changed = true;
        return placed;
    }
}
