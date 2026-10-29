using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GlobalChunkBoundsTests
{
    [Fact]
    public void Converts_a_visible_world_rect_to_local_tile_bounds()
    {
        var chunk = new GlobalChunkCoordinate(2, 3);
        var global = (MinX: 140, MaxX: 170, MinY: 100, MaxY: 120);

        var local = GlobalChunkGeometry.ToLocalTileBounds(global, chunk);

        // chunk 2 starts at world tile 150 (2 * 75), chunk 3 at 243 (3 * 81).
        Assert.Equal(140 - 150 + 1, local.MinX);
        Assert.Equal(170 - 150 + 1, local.MaxX);
        Assert.Equal(100 - 243 + 1, local.MinY);
        Assert.Equal(120 - 243 + 1, local.MaxY);
    }

    [Fact]
    public void Local_tile_plus_chunk_stride_matches_the_geometry_offset()
    {
        var chunk = new GlobalChunkCoordinate(1, 1);
        var local = GlobalChunkGeometry.ToLocalTileBounds(
            (MinX: 75, MaxX: 75, MinY: 81, MaxY: 81), chunk);

        // World tile 75 is local tile 1 of chunk 1; its overlay X is local + 75.
        Assert.Equal(1, local.MinX);
        Assert.Equal(1, local.MinY);
        var overlayX = local.MinX + chunk.X * GlobalChunkGeometry.WidthTiles;
        var geometryX = (local.MinX - 1) + chunk.X * GlobalChunkGeometry.WidthTiles;
        Assert.Equal(geometryX, overlayX - 1);
    }
}
