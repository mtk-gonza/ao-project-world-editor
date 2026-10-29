using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Domain.Rendering;
using AoProject.WorldEditor.Engine.Rendering;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class WorldMapRenderGuardTests
{
    [Fact]
    public void Overlay_builder_skips_the_tile_sweep_when_no_overlay_is_enabled()
    {
        var map = new WorldMap();
        map.Blocks[10, 10].Trigger = 5;
        map.Blocks[10, 10].Blocked = 0xF;
        map.Blocks[10, 10].TileExit.Map = 2;
        map.Blocks[10, 10].ParticleIndex = 1;
        map.Blocks[10, 10].Light.Range = 10;

        var overlays = MapOverlayGeometry.Build(map, new WorldRenderOptions());

        Assert.Empty(overlays);
        Assert.False(MapOverlayGeometry.AffectsTiles(new WorldRenderOptions()));
    }

    [Fact]
    public void Overlay_builder_keeps_the_selection_without_other_toggles()
    {
        var overlays = MapOverlayGeometry.Build(new WorldMap(), new WorldRenderOptions(), (12, 34));

        var selection = Assert.Single(overlays);
        Assert.Equal(OverlayKind.Selection, selection.Kind);
        Assert.Equal(12, selection.X);
        Assert.Equal(34, selection.Y);
    }

    [Fact]
    public void Overlay_affects_tiles_flags_each_editor_toggle()
    {
        Assert.True(MapOverlayGeometry.AffectsTiles(new WorldRenderOptions { ShowGrid = true }));
        Assert.True(MapOverlayGeometry.AffectsTiles(new WorldRenderOptions { ShowBlocked = true }));
        Assert.True(MapOverlayGeometry.AffectsTiles(new WorldRenderOptions { ShowTriggers = true }));
        Assert.True(MapOverlayGeometry.AffectsTiles(new WorldRenderOptions { ShowTransfers = true }));
        Assert.True(MapOverlayGeometry.AffectsTiles(new WorldRenderOptions { ShowParticles = true }));
        Assert.True(MapOverlayGeometry.AffectsTiles(new WorldRenderOptions { ShowLights = true }));
    }

    [Fact]
    public void Grid_overlay_covers_the_full_map()
    {
        var overlays = MapOverlayGeometry.Build(new WorldMap(), new WorldRenderOptions { ShowGrid = true });

        Assert.Equal(WorldMap.Width * WorldMap.Height, overlays.Count);
    }

    [Theory]
    [InlineData(0, 0, false, false, false)]     // tile 1 is outside the core
    [InlineData(12 * 32, 9 * 32, false, false, true)]   // tile 13,10: west/north edge
    [InlineData(12 * 32, 9 * 32, true, true, false)]    // tile 13,10 hidden with neighbours
    [InlineData(13 * 32, 10 * 32, true, true, true)]    // tile 14,11: first interior tile
    [InlineData(87 * 32, 90 * 32, false, false, true)]  // tile 88,91: east/south edge
    [InlineData(-1, 50 * 32, false, false, false)]      // negative anchor floors to tile 0
    public void Core_pixel_filter_matches_the_tile_core(
        int pixelX, int pixelY, bool hasWest, bool hasNorth, bool expected)
    {
        Assert.Equal(expected, GlobalChunkGeometry.IsCorePixel(
            pixelX, pixelY, MapRenderGeometry.TilePixels, hasWest, hasNorth));
    }

    [Fact]
    public void Light_lookup_outside_the_map_returns_white_instead_of_throwing()
    {
        var lights = new int[WorldMap.Width + 1, WorldMap.Height + 1, 4];
        lights[50, 50, 0] = unchecked((int)0xFF102030);

        Assert.Equal(unchecked((int)0xFF102030), D3D11WorldRenderer.LightColorAt(lights, 50, 50, 0));
        Assert.Equal(unchecked((int)0xFFFFFFFF), D3D11WorldRenderer.LightColorAt(lights, 500, 500, 0));
        Assert.Equal(unchecked((int)0xFFFFFFFF), D3D11WorldRenderer.LightColorAt(lights, -1, 0, 0));
        Assert.Equal(unchecked((int)0xFFFFFFFF), D3D11WorldRenderer.LightColorAt(null, 0, 0, 0));
    }
}
