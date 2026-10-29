using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

/// <summary>
/// The VB6 editor decodes the ambient and the per-tile light colour differently
/// (clsLight.LightRender vs ModCargaIAO.bas:353-358). Both conventions are
/// pinned here because getting them backwards swaps red and blue.
/// </summary>
public sealed class MapLightingColorTests
{
    [Fact]
    public void Map_light_colour_reads_the_high_byte_as_red()
    {
        // Mapa319 has a light stored as 0xFF0000: pure red in the VB6 loader.
        Assert.Equal(unchecked((int)0xFFFF0000), MapLightingService.LightColorFromMap(0xFF0000));
        Assert.Equal(unchecked((int)0xFF00FF00), MapLightingService.LightColorFromMap(0x00FF00));
        Assert.Equal(unchecked((int)0xFF0000FF), MapLightingService.LightColorFromMap(0x0000FF));
        Assert.Equal(unchecked((int)0xFF102030), MapLightingService.LightColorFromMap(0x102030));
    }

    [Fact]
    public void Ambient_keeps_the_legacy_red_blue_swap()
    {
        // clsLight.LightRender: r = BaseLight And 255, so 0x00RRGGBB flips R and B.
        Assert.Equal(unchecked((int)0xFF0000FF), MapLightingService.AmbientFromColor(0xFF0000));
        Assert.Equal(unchecked((int)0xFF102030), MapLightingService.AmbientFromColor(0x302010));
    }

    [Fact]
    public void Ambient_zero_is_daylight_and_argb_values_pass_through()
    {
        Assert.Equal(MapLightingService.DayLight, MapLightingService.AmbientFromColor(0));
        Assert.Equal(unchecked((int)0xFF8080AA), MapLightingService.AmbientFromColor(unchecked((int)0xFF8080AA)));
    }

    [Fact]
    public void A_round_light_stays_red_dominant_on_its_own_tile()
    {
        var map = new WorldMap();
        map.Blocks[50, 50].Light.Range = 100; // stored >= 100 -> effective radius 1
        map.Blocks[50, 50].Light.Color = 0xFF0000;

        var lights = new MapLightingService().Compute(map, MapLightingService.DayLight);

        // At the tile corners the light is lerped toward the white ambient, so
        // red must stay the strongest channel. With the old decode it was blue.
        for (var corner = 0; corner < 4; corner++)
        {
            var color = lights[50, 50, corner];
            var red = (color >> 16) & 0xFF;
            var green = (color >> 8) & 0xFF;
            var blue = color & 0xFF;
            Assert.True(red > green, $"corner {corner}: R={red} G={green} B={blue}");
            Assert.True(red > blue, $"corner {corner}: R={red} G={green} B={blue}");
        }
    }

    [Fact]
    public void Tiles_outside_every_light_use_the_ambient()
    {
        var map = new WorldMap();
        map.Blocks[50, 50].Light.Range = 100;
        map.Blocks[50, 50].Light.Color = 0xFF0000;
        var ambient = unchecked((int)0xFF203040);

        var lights = new MapLightingService().Compute(map, ambient);

        Assert.Equal(ambient, lights[1, 1, 0]);
        Assert.Equal(ambient, lights[100, 100, 3]);
    }

    [Fact]
    public void Flat_lights_stay_off_by_default()
    {
        var map = new WorldMap();
        map.Blocks[50, 50].Light.Range = 97;
        map.Blocks[50, 50].Light.Color = 0x00FF00;

        var off = new MapLightingService().Compute(map, MapLightingService.DayLight);
        var on = new MapLightingService().Compute(map, MapLightingService.DayLight, includeFlatLights: true);

        Assert.Equal(MapLightingService.DayLight, off[50, 50, 0]);
        Assert.NotEqual(MapLightingService.DayLight, on[50, 50, 0]);
    }
}
