using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapLightingTests
{
    [Fact]
    public void Unlit_tiles_fall_back_to_the_ambient_colour()
    {
        var map = new WorldMap();
        var lights = new MapLightingService().Compute(map, MapLightingService.DayLight);
        for (var corner = 0; corner < 4; corner++)
            Assert.Equal(unchecked((int)0xFFFFFFFF), lights[50, 50, corner]);
    }

    [Fact]
    public void Night_ambient_tints_every_corner()
    {
        var map = new WorldMap();
        var lights = new MapLightingService().Compute(map, MapLightingService.NightLight);
        Assert.Equal(unchecked((int)0xFF8080AA), lights[10, 10, 0]);
    }

    [Fact]
    public void Flat_light_fills_the_whole_square()
    {
        var map = new WorldMap();
        map.Blocks[50, 50].Light.Range = 2;
        map.Blocks[50, 50].Light.Color = 0xFF0000; // CSM stores 0xRRGGBB
        var lights = new MapLightingService().Compute(map, MapLightingService.DayLight, includeFlatLights: true);
        Assert.Equal(unchecked((int)0xFFFF0000), lights[48, 50, 2]);
        Assert.Equal(unchecked((int)0xFFFFFFFF), lights[48, 50, 0]);
        Assert.Equal(unchecked((int)0xFFFF0000), lights[50, 48, 0]);
        Assert.Equal(unchecked((int)0xFFFF0000), lights[50, 50, 0]);
        Assert.Equal(unchecked((int)0xFFFF0000), lights[52, 50, 0]);
        Assert.Equal(unchecked((int)0xFFFFFFFF), lights[53, 50, 0]);
    }

    [Fact]
    public void Flat_light_keeps_raw_border_corner_ownership_at_map_edge()
    {
        var map = new WorldMap();
        map.Blocks[1, 1].Light.Range = 2;
        map.Blocks[1, 1].Light.Color = 0xFF0000;

        var lights = new MapLightingService().Compute(map, MapLightingService.DayLight, includeFlatLights: true);

        Assert.Equal(unchecked((int)0xFFFF0000), lights[1, 3, 1]);
        Assert.Equal(unchecked((int)0xFFFFFFFF), lights[1, 3, 0]);
    }

    [Fact]
    public void Round_light_fades_towards_the_ambient_colour()
    {
        var map = new WorldMap();
        map.Blocks[50, 50].Light.Range = 105; // effective range 6
        map.Blocks[50, 50].Light.Color = 0xFF0000; // CSM stores 0xRRGGBB
        var lights = new MapLightingService().Compute(map, MapLightingService.DayLight);

        var center = lights[50, 50, 1];
        var edge = lights[53, 50, 1];
        Assert.True(Red(center) > Green(center), $"centro=0x{center:X8}");
        Assert.True(Green(edge) > Green(center), $"centro=0x{center:X8} borde=0x{edge:X8}");
        Assert.NotEqual(center, edge);
    }

    [Fact]
    public void Round_light_applies_the_same_distance_to_symmetric_corners()
    {
        var map = new WorldMap();
        map.Blocks[50, 50].Light.Range = 103; // effective range 4
        map.Blocks[50, 50].Light.Color = 0xFF0000;
        var lights = new MapLightingService().Compute(map, MapLightingService.DayLight);

        Assert.Equal(lights[49, 49, 2], lights[50, 49, 2]);
        Assert.Equal(lights[49, 51, 0], lights[51, 51, 2]);
    }

    [Fact]
    public void Round_light_at_map_edge_does_not_write_outside_the_map()
    {
        var map = new WorldMap();
        map.Blocks[1, 1].Light.Range = 104;
        map.Blocks[1, 1].Light.Color = 0xFF0000;
        var lights = new MapLightingService().Compute(map, MapLightingService.DayLight);

        Assert.NotEqual(0, lights[1, 1, 1]);
        Assert.Equal(0, lights[0, 1, 1]);
        Assert.Equal(0, lights[1, 0, 1]);
    }

    [Fact]
    public void Round_light_corner_table_is_initialized_inside_and_outside_the_radius()
    {
        var map = new WorldMap();
        map.Blocks[50, 50].Light.Range = 104; // effective range 5
        map.Blocks[50, 50].Light.Color = 0xFF0000;
        var lights = new MapLightingService().Compute(map, MapLightingService.DayLight);

        for (var y = 40; y <= 60; y++)
        for (var x = 40; x <= 60; x++)
        for (var corner = 0; corner < 4; corner++)
        {
            var value = lights[x, y, corner];
            Assert.NotEqual(0, value);
            if (x == 40 && y == 40) Assert.Equal(MapLightingService.DayLight, value);
        }
    }

    [Fact]
    public void Flat_lights_are_skipped_on_load_like_the_reference()
    {
        var map = new WorldMap();
        map.Blocks[50, 50].Light.Range = 2;
        map.Blocks[50, 50].Light.Color = 0xFF0000;
        var lights = new MapLightingService().Compute(map, MapLightingService.DayLight);
        Assert.Equal(unchecked((int)0xFFFFFFFF), lights[50, 50, 0]);
    }

    [Fact]
    public void Zero_range_lights_are_ignored()
    {
        var map = new WorldMap();
        map.Blocks[50, 50].Light.Range = 0;
        map.Blocks[50, 50].Light.Color = 0xFF0000; // CSM stores 0xRRGGBB
        var lights = new MapLightingService().Compute(map, MapLightingService.DayLight);
        Assert.Equal(unchecked((int)0xFFFFFFFF), lights[50, 50, 0]);
    }

    [Fact]
    public void Base_light_COLORREF_is_normalized_to_argb()
    {
        Assert.Equal(unchecked((int)0xFFFF0000), MapLightingService.AmbientFromColor(0x000000FF));
        Assert.Equal(unchecked((int)0xFF8080AA), MapLightingService.AmbientFromColor(MapLightingService.NightLight));
    }

    private static int Red(int argb) => (argb >> 16) & 0xFF;
    private static int Green(int argb) => (argb >> 8) & 0xFF;
}


