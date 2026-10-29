using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class WorldMiniMapProjectionTests
{
    [Fact]
    public void Maps_a_local_tile_to_its_pixel_inside_the_world_image()
    {
        var projection = new WorldMiniMapProjection(19, 22, wrapX: true);

        Assert.Equal(1900, projection.Width);
        Assert.Equal(2200, projection.Height);
        Assert.Equal(new WorldMiniMapPoint(0, 0), projection.ToPixel(new GlobalChunkCoordinate(0, 0), 1, 1));
        Assert.Equal(new WorldMiniMapPoint(200, 300), projection.ToPixel(new GlobalChunkCoordinate(2, 3), 1, 1));
        Assert.Equal(new WorldMiniMapPoint(299, 399), projection.ToPixel(new GlobalChunkCoordinate(2, 3), 100, 100));
    }

    [Fact]
    public void Rejects_tiles_outside_the_map()
    {
        var projection = new WorldMiniMapProjection(1, 1, wrapX: false);
        Assert.Throws<ArgumentOutOfRangeException>(() => projection.ToPixel(new GlobalChunkCoordinate(0, 0), 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => projection.ToPixel(new GlobalChunkCoordinate(0, 0), 1, 101));
    }

    [Fact]
    public void Clamps_the_viewport_at_the_horizontal_and_vertical_borders()
    {
        var projection = new WorldMiniMapProjection(19, 22, wrapX: false);

        var origin = projection.CenterOn(new WorldMiniMapPoint(0, 0), 200, 216);
        Assert.Equal(0, origin.Left);
        Assert.Equal(0, origin.Top);

        var end = projection.CenterOn(new WorldMiniMapPoint(1899, 2199), 200, 216);
        Assert.Equal(1700, end.Left);
        Assert.Equal(1984, end.Top);
        Assert.False(end.WrapsX);
    }

    [Fact]
    public void Wraps_the_viewport_around_the_seam_of_the_cylinder()
    {
        var projection = new WorldMiniMapProjection(19, 22, wrapX: true);

        var viewport = projection.CenterOn(new WorldMiniMapPoint(5, 500), 200, 216);
        Assert.True(viewport.WrapsX);
        Assert.Equal(1805, viewport.Left);
        Assert.Equal(392, viewport.Top);

        var parts = projection.Split(viewport);
        Assert.Equal(2, parts.Count);
        Assert.Equal(95, parts[0].Source.Width);
        Assert.Equal(0, parts[0].DestinationX);
        Assert.Equal(105, parts[1].Source.Width);
        Assert.Equal(95, parts[1].DestinationX);
    }

    [Fact]
    public void Does_not_split_a_viewport_that_fits()
    {
        var projection = new WorldMiniMapProjection(19, 22, wrapX: true);
        var viewport = projection.CenterOn(new WorldMiniMapPoint(900, 1100), 200, 216);
        var parts = projection.Split(viewport);
        Assert.Single(parts);
        Assert.Equal(viewport, parts[0].Source);
    }
}
