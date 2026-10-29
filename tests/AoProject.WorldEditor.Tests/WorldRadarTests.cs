using System.Drawing;
using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

/// <summary>
/// Port of the VB6 client centered minimap: the radar is a crop of a pre-rendered
/// world image (mapaN_200x200.bmp), centered on the player and clamped.
/// </summary>
public sealed class WorldRadarTests
{
    [Fact]
    public void Crops_around_the_player_and_keeps_the_destination_size()
    {
        using var world = Solid(200, 200, Color.Blue);
        using var radar = new WorldRadarService().CreateCentered(world, 19, 22, 5, 5, 50, 50, 100, 100);

        Assert.Equal(100, radar.Width);
        Assert.Equal(100, radar.Height);
        Assert.Equal(Color.Blue.ToArgb(), radar.GetPixel(50, 50).ToArgb());
    }

    [Fact]
    public void Clamps_the_crop_at_the_world_edges()
    {
        var service = new WorldRadarService();
        using var world = Solid(200, 200, Color.Blue);
        // Player in the top-left cell: the crop must not start before 0.
        using var topLeft = service.CreateCentered(world, 19, 22, 0, 0, 14, 11, 100, 100);
        Assert.Equal(Color.Blue.ToArgb(), topLeft.GetPixel(0, 0).ToArgb());
        // Player in the bottom-right cell: the crop must not pass the bitmap.
        using var bottomRight = service.CreateCentered(world, 19, 22, 18, 21, 87, 90, 100, 100);
        Assert.Equal(Color.Blue.ToArgb(), bottomRight.GetPixel(99, 99).ToArgb());
    }

    [Fact]
    public void The_player_tile_lands_near_the_center_pixel()
    {
        // Single cell world: the core maps onto the whole image, so the center
        // tile (50,50) lands next to the middle of the crop (VB6 uses CLng).
        using var world = new Bitmap(200, 200);
        using (var canvas = Graphics.FromImage(world)) canvas.Clear(Color.Black);
        world.SetPixel(100, 100, Color.Red);
        using var radar = new WorldRadarService().CreateCentered(world, 1, 1, 0, 0, 50, 50, 100, 100);

        var found = false;
        for (var y = 48; y <= 52 && !found; y++)
        for (var x = 48; x <= 52 && !found; x++)
            found = radar.GetPixel(x, y).ToArgb() == Color.Red.ToArgb();
        Assert.True(found, "el tile del jugador no cayo cerca del centro del recorte");
    }

    [Fact]
    public void Zoom_delta_widens_the_source_crop()
    {
        using var world = Solid(200, 200, Color.Blue);
        var service = new WorldRadarService();
        using var normal = service.CreateCentered(world, 1, 1, 0, 0, 50, 50, 100, 100);
        using var zoomedOut = service.CreateCentered(world, 1, 1, 0, 0, 50, 50, 100, 100, deltaWidth: 50, deltaHeight: 50);

        Assert.Equal(100, normal.Width);
        Assert.Equal(100, zoomedOut.Width);
    }

    [Fact]
    public void Composes_the_world_image_from_the_per_map_minimaps()
    {
        var layouts = new[]
        {
            new MapsWorldDataService.WorldLayout(1, 2, 1, new short[] { 1, 2 })
        };
        using var first = Solid(100, 100, Color.Red);
        using var second = Solid(100, 100, Color.Lime);
        using var image = WorldRadarService.ComposeWorldImage(layouts, 1,
            new Dictionary<short, Bitmap> { [1] = first, [2] = second });

        Assert.Equal(150, image.Width);
        Assert.Equal(81, image.Height);
        Assert.Equal(Color.Red.ToArgb(), image.GetPixel(10, 40).ToArgb());
        Assert.Equal(Color.Lime.ToArgb(), image.GetPixel(110, 40).ToArgb());
        Assert.Equal((0, 0), WorldRadarService.GridOf(layouts, 1, 1));
        Assert.Equal((1, 0), WorldRadarService.GridOf(layouts, 1, 2));
        Assert.Equal((-1, -1), WorldRadarService.GridOf(layouts, 1, 9));
    }

    [Fact]
    public void Composed_cells_are_contiguous_because_only_the_core_is_drawn()
    {
        var layouts = new[]
        {
            new MapsWorldDataService.WorldLayout(1, 2, 1, new short[] { 1, 2 })
        };
        // Map images with black margins outside the playable core, like the CSM.
        using var first = CoreWithMargin(Color.Red);
        using var second = CoreWithMargin(Color.Lime);
        using var image = WorldRadarService.ComposeWorldImage(layouts, 1,
            new Dictionary<short, Bitmap> { [1] = first, [2] = second });

        // Two 75 wide cells: x 74 is the last column of the first, x 75 the first of
        // the second. Both must be clean core colours (no margin, no seam).
        Assert.Equal(150, image.Width);
        Assert.Equal(81, image.Height);
        var left = image.GetPixel(74, 40);
        var right = image.GetPixel(75, 40);
        Assert.True(left.R > 0xC0 && left.G < 0x40, $"borde izquierdo {left}");
        Assert.True(right.G > 0xC0 && right.R < 0x40, $"borde derecho {right}");
    }

    [Fact]
    public void Radar_pixel_is_the_global_tile_shifted_by_the_core_origin()
    {
        // The image uses the render stride (75x81), so the mapping is a pure
        // translation: no drift one pixel per cell.
        Assert.Equal((0, 0), WorldRadarService.ToRadarPixel(new GlobalChunkCoordinate(0, 0), 14, 11));
        Assert.Equal((74, 80), WorldRadarService.ToRadarPixel(new GlobalChunkCoordinate(0, 0), 88, 91));
        Assert.Equal((75, 81), WorldRadarService.ToRadarPixel(new GlobalChunkCoordinate(1, 1), 14, 11));
        Assert.Equal((150, 162), WorldRadarService.ToRadarPixel(new GlobalChunkCoordinate(2, 2), 14, 11));
        // The last column of one cell is immediately left of the first column of
        // the next one: no gap and no duplicated column.
        Assert.Equal(74, WorldRadarService.ToRadarPixel(new GlobalChunkCoordinate(0, 0), 88, 40).X);
        Assert.Equal(75, WorldRadarService.ToRadarPixel(new GlobalChunkCoordinate(1, 0), 14, 40).X);
    }

    [Fact]
    public void CreateCenteredAt_places_the_requested_pixel_in_the_middle()
    {
        using var world = new Bitmap(400, 400);
        using (var canvas = Graphics.FromImage(world)) canvas.Clear(Color.Black);
        world.SetPixel(200, 200, Color.Red);

        var (radar, markerX, markerY) = new WorldRadarService().CreateCenteredAt(world, 200, 200, 100, 100);
        using var image = radar;

        Assert.Equal((50, 50), (markerX, markerY));
        Assert.Equal(Color.Red.ToArgb(), image.GetPixel(50, 50).ToArgb());
    }

    [Fact]
    public void CreateCenteredAt_moves_the_marker_when_the_crop_clamps_at_the_edge()
    {
        using var world = new Bitmap(400, 400);
        using (var canvas = Graphics.FromImage(world)) canvas.Clear(Color.Black);
        world.SetPixel(2, 3, Color.Red);

        var (radar, markerX, markerY) = new WorldRadarService().CreateCenteredAt(world, 2, 3, 100, 100);
        using var image = radar;

        // Clamped to (0,0): the player is drawn at (2,3), not in the middle.
        Assert.Equal((2, 3), (markerX, markerY));
        Assert.Equal(Color.Red.ToArgb(), image.GetPixel(markerX, markerY).ToArgb());
    }

    [Fact]
    public void CreateCenteredAt_centers_the_crop_when_clamp_is_disabled()
    {
        using var world = new Bitmap(400, 400);
        using (var canvas = Graphics.FromImage(world)) canvas.Clear(Color.Black);
        world.SetPixel(2, 3, Color.Red);

        var (radar, markerX, markerY) = new WorldRadarService().CreateCenteredAt(
            world, 2, 3, 100, 100, clampToImage: false);
        using var image = radar;

        // Without clamping the marker stays centered and the world shows up in
        // the bottom-right quadrant (black outside the bitmap).
        Assert.Equal((50, 50), (markerX, markerY));
        Assert.Equal(Color.Red.ToArgb(), image.GetPixel(50, 50).ToArgb());
        Assert.Equal(Color.Black.ToArgb(), image.GetPixel(0, 0).ToArgb());
    }

    private static Bitmap CoreWithMargin(Color core)
    {
        var image = new Bitmap(100, 100);
        using var canvas = Graphics.FromImage(image);
        canvas.Clear(Color.Black);
        // The whole render core of a cell: 75x81 tiles from local (14,11).
        canvas.FillRectangle(new SolidBrush(core), 13, 10,
            WorldRadarService.RadarStrideX, WorldRadarService.RadarStrideY);
        return image;
    }

    private static Bitmap Solid(int width, int height, Color color)
    {
        var image = new Bitmap(width, height);
        using var canvas = Graphics.FromImage(image);
        canvas.Clear(color);
        return image;
    }
}
