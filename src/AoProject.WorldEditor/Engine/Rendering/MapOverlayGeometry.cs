using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Domain.Rendering;

namespace AoProject.WorldEditor.Engine.Rendering;

internal enum OverlayKind { Grid, Blocked, Trigger, Transfer, Particle, Light, Selection }

internal readonly record struct MapOverlay(
    OverlayKind Kind, int X, int Y, int Width, int Height,
    int GraphicIndex = 0, int OffsetX = 0, int OffsetY = 0);

/// <summary>
/// Port of the marker block of clsDX8Engine.RenderScreen: blocked sides use
/// GRH 1150+i, transfers use GRH 3 and triggers render their digits with
/// GRH 21452+digit, offset -6/-12 and 8 px apart.
/// </summary>
internal static class MapOverlayGeometry
{
    public const int GridGraphic = 2;
    public const int BlockedBaseGraphic = 1150;
    public const int TransferGraphic = 3;
    public const int TriggerDigitBase = 21452;

    public static IReadOnlyList<MapOverlay> Build(WorldMap map, WorldRenderOptions options, (int X, int Y)? selected = null,
        (int MinX, int MaxX, int MinY, int MaxY)? bounds = null)
    {
        var overlays = new List<MapOverlay>();
        if (AffectsTiles(options))
        {
            var minX = Math.Clamp((int)map.CsmMapSize.XMin, 1, WorldMap.Width);
            var maxX = Math.Clamp((int)map.CsmMapSize.XMax, 1, WorldMap.Width);
            var minY = Math.Clamp((int)map.CsmMapSize.YMin, 1, WorldMap.Height);
            var maxY = Math.Clamp((int)map.CsmMapSize.YMax, 1, WorldMap.Height);
            // Only the visible tile rect matters: building the whole map per
            // frame costs ~16 ms in the world view and produces overlays that
            // the renderer then truncates.
            if (bounds is { } visible)
            {
                minX = Math.Max(minX, visible.MinX);
                maxX = Math.Min(maxX, visible.MaxX);
                minY = Math.Max(minY, visible.MinY);
                maxY = Math.Min(maxY, visible.MaxY);
            }
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var block = map.Blocks[x, y];
                if (options.ShowGrid) overlays.Add(new(OverlayKind.Grid, x, y, 1, 1, GridGraphic));
                if (options.ShowBlocked && block.Blocked != 0)
                {
                    for (var side = 0; side < 4; side++)
                        if ((block.Blocked & (1 << side)) != 0)
                            overlays.Add(new(OverlayKind.Blocked, x, y, 1, 1, BlockedBaseGraphic + side + 1));
                }
                if (options.ShowTriggers && block.Trigger != 0)
                {
                    var position = 0;
                    foreach (var character in block.Trigger.ToString())
                    {
                        if (!char.IsDigit(character)) continue;
                        var digit = character - '0';
                        overlays.Add(new(OverlayKind.Trigger, x, y, 1, 1, TriggerDigitBase + digit, -6 + position, -12));
                        position += 8;
                    }
                }
                if (options.ShowTransfers && block.TileExit.Map != 0)
                    overlays.Add(new(OverlayKind.Transfer, x, y, 1, 1, TransferGraphic));
                if (options.ShowParticles && block.ParticleIndex != 0) overlays.Add(new(OverlayKind.Particle, x, y, 1, 1));
                if (options.ShowLights && block.Light.Range != 0) overlays.Add(new(OverlayKind.Light, x, y, 1, 1));
            }
        }
        if (selected is { } tile) overlays.Add(new(OverlayKind.Selection, tile.X, tile.Y, 1, 1));
        return overlays;
    }

    /// <summary>
    /// True when any option can emit a per-tile overlay. Used to skip full-map
    /// sweeps (single map and global world view) when nothing is enabled.
    /// </summary>
    public static bool AffectsTiles(WorldRenderOptions options) =>
        options.ShowGrid || options.ShowBlocked || options.ShowTriggers ||
        options.ShowTransfers || options.ShowParticles || options.ShowLights;

    public static IEnumerable<int> MarkerGraphics()
    {
        yield return GridGraphic;
        yield return TransferGraphic;
        for (var side = 0; side < 4; side++) yield return BlockedBaseGraphic + side + 1;
        for (var digit = 0; digit <= 9; digit++) yield return TriggerDigitBase + digit;
    }
}
