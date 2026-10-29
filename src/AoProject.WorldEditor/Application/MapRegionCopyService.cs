using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct MapRegion(int StartX, int StartY, int EndX, int EndY)
{
    public MapRegion Normalize() => new(Math.Min(StartX, EndX), Math.Min(StartY, EndY), Math.Max(StartX, EndX), Math.Max(StartY, EndY));
    public int Width => Math.Abs(EndX - StartX) + 1;
    public int Height => Math.Abs(EndY - StartY) + 1;
}

/// <summary>
/// Copies the complete MapBlock payload used by Form2/Form3 border stitching.
/// NPCs are intentionally omitted exactly as modDirectDraw.PegarSeleccion does.
/// </summary>
internal sealed class MapRegionCopyService
{
    public int Copy(WorldMap source, MapRegion region, WorldMap destination, int destinationX, int destinationY)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        var normalized = region.Normalize();
        Validate(source, normalized);
        if (destinationX < 1 || destinationY < 1) throw new ArgumentOutOfRangeException(nameof(destinationX));

        var copied = 0;
        for (var dx = 0; dx < normalized.Width; dx++)
        for (var dy = 0; dy < normalized.Height; dy++)
        {
            var x = destinationX + dx;
            var y = destinationY + dy;
            if (!InDomain(destination, x, y)) continue;
            CopyBlock(source.Blocks[normalized.StartX + dx, normalized.StartY + dy], destination.Blocks[x, y]);
            destination.Blocks[x, y].NpcIndex = 0;
            destination.Blocks[x, y].CharacterIndex = 0;
            copied++;
        }
        destination.Info.Changed = true;
        return copied;
    }

    private static void CopyBlock(MapBlock source, MapBlock destination)
    {
        Array.Copy(source.Graphics, destination.Graphics, source.Graphics.Length);
        destination.ObjectGraphic = source.ObjectGraphic;
        Array.Copy(source.LightValues, destination.LightValues, source.LightValues.Length);
        Array.Copy(source.Colors, destination.Colors, source.Colors.Length);
        destination.Light.Range = source.Light.Range; destination.Light.Color = source.Light.Color;
        destination.ParticleGroup = source.ParticleGroup; destination.ParticleIndex = source.ParticleIndex;
        destination.Marked = source.Marked; destination.Blocked = source.Blocked;
        destination.Trigger = source.Trigger; destination.Red = source.Red; destination.Green = source.Green; destination.Blue = source.Blue;
        destination.Object.Index = source.Object.Index; destination.Object.Amount = source.Object.Amount;
        destination.TileExit.Map = source.TileExit.Map; destination.TileExit.X = source.TileExit.X; destination.TileExit.Y = source.TileExit.Y;
    }

    private static void Validate(WorldMap map, MapRegion region)
    {
        if (!InDomain(map, region.StartX, region.StartY) || !InDomain(map, region.EndX, region.EndY))
            throw new ArgumentOutOfRangeException(nameof(region));
    }

    private static bool InDomain(WorldMap map, int x, int y) =>
        x >= map.CsmMapSize.XMin && x <= map.CsmMapSize.XMax &&
        y >= map.CsmMapSize.YMin && y <= map.CsmMapSize.YMax &&
        x is >= 1 and <= WorldMap.Width && y is >= 1 and <= WorldMap.Height;
}
