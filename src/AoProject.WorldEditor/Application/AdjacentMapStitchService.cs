using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal enum StitchSide { North, South, East, West }

internal readonly record struct StitchResult(StitchSide Side, short Map, int Copied, bool Found);

/// <summary>Port of Form3.HacerTranslados region propagation.</summary>
internal sealed class AdjacentMapStitchService
{
    public short DetectNeighborMap(WorldSector map, StitchSide side)
    {
        ArgumentNullException.ThrowIfNull(map);
        var (fixedCoordinate, start, end, horizontal, fallbackX, fallbackY) = side switch
        {
            StitchSide.North => (10, 14, 87, true, 49, 10),
            StitchSide.South => (91, 14, 87, true, 49, 91),
            StitchSide.East => (88, 11, 90, false, 88, 49),
            _ => (13, 11, 90, false, 12, 49)
        };
        for (var coordinate = start; coordinate <= end; coordinate++)
        {
            var block = horizontal ? map.Blocks[coordinate, fixedCoordinate] : map.Blocks[fixedCoordinate, coordinate];
            if (block.TileExit.Map != 0) return block.TileExit.Map;
        }
        return map.Blocks[fallbackX, fallbackY].TileExit.Map;
    }

    public int CopySide(WorldSector source, WorldSector destination, StitchSide side)
    {
        var (region, targetX, targetY) = side switch
        {
            StitchSide.North => (new MapRegion(1, 10, 100, 21), 1, 90),
            StitchSide.South => (new MapRegion(1, 81, 100, 89), 1, 1),
            StitchSide.East => (new MapRegion(76, 1, 87, 100), 1, 1),
            _ => (new MapRegion(13, 1, 25, 100), 88, 1)
        };
        return new MapRegionCopyService().Copy(source, region, destination, targetX, targetY);
    }
}
