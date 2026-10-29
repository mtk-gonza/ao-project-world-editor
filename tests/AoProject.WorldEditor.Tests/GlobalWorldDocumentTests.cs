using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GlobalWorldDocumentTests
{
    [Fact]
    public void Resolves_global_tile_to_sector_and_local_coordinates()
    {
        var map = new WorldMap();
        var assembly = new GlobalWorldAssembly
        {
            Placements = [new GlobalMapPlacement(7, new GlobalChunkCoordinate(2, -1), "surface")]
        };
        var document = new GlobalWorldDocument(assembly,
            new Dictionary<short, WorldMap> { [7] = map },
            new Dictionary<short, string> { [7] = "mapa7.csm" }, 7);

        Assert.True(document.TryResolve(162, -72, out var location));
        Assert.Equal(new GlobalTileLocation(7, 13, 10), location);
    }

    [Fact]
    public void Resolves_shared_border_to_the_playable_neighbor_without_overlap()
    {
        var assembly = new GlobalWorldAssembly
        {
            Placements =
            [
                new GlobalMapPlacement(1, new GlobalChunkCoordinate(0, 0), "surface"),
                new GlobalMapPlacement(2, new GlobalChunkCoordinate(1, 0), "surface")
            ]
        };
        var maps = new Dictionary<short, WorldMap> { [1] = new(), [2] = new() };
        var document = new GlobalWorldDocument(assembly, maps,
            new Dictionary<short, string>(), 1);

        Assert.True(document.TryResolve(87, 50, out var west));
        Assert.Equal(new GlobalTileLocation(1, 88, 51), west);
        Assert.True(document.TryResolve(88, 50, out var east));
        Assert.Equal(new GlobalTileLocation(2, 14, 51), east);
    }

    [Fact]
    public void Collects_only_the_maps_touched_by_a_global_selection()
    {
        var assembly = new GlobalWorldAssembly
        {
            Placements =
            [
                new GlobalMapPlacement(1, new GlobalChunkCoordinate(0, 0), "surface"),
                new GlobalMapPlacement(2, new GlobalChunkCoordinate(1, 0), "surface")
            ]
        };
        var document = new GlobalWorldDocument(assembly,
            new Dictionary<short, WorldMap> { [1] = new(), [2] = new() },
            new Dictionary<short, string>(), 1);

        var west = document.CollectMapIds(new GlobalTileSelection(20, 20, 30, 30));
        var both = document.CollectMapIds(new GlobalTileSelection(80, 20, 100, 30));

        Assert.Equal(new short[] { 1 }, west);
        Assert.Equal(new short[] { 1, 2 }, both.OrderBy(id => id));
    }

    [Fact]
    public void TryGetGlobalTile_rejects_tiles_hidden_by_the_core()
    {
        var assembly = new GlobalWorldAssembly
        {
            Placements =
            [
                new GlobalMapPlacement(1, new GlobalChunkCoordinate(0, 0), "surface"),
                new GlobalMapPlacement(2, new GlobalChunkCoordinate(1, 0), "surface")
            ]
        };
        var document = new GlobalWorldDocument(assembly,
            new Dictionary<short, WorldMap> { [1] = new(), [2] = new() },
            new Dictionary<short, string>(), 1);

        Assert.True(document.TryGetGlobalTile(1, 88, 50, out var westEdge));
        Assert.Equal((87, 49), westEdge);
        Assert.True(document.TryGetGlobalTile(2, 14, 50, out var eastEdge));
        Assert.Equal((88, 49), eastEdge);

        // Hidden west border of map 2 belongs to map 1; hidden east border of
        // map 1 belongs to map 2 / the clipped area.
        Assert.False(document.TryGetGlobalTile(2, 13, 50, out _));
        Assert.False(document.TryGetGlobalTile(1, 100, 50, out _));
        Assert.False(document.TryResolve(0, 50, out _));
        // The clipped east border of map 1 is covered by map 2's west core.
        Assert.True(document.TryResolve(99, 50, out var overlap));
        Assert.Equal(new GlobalTileLocation(2, 25, 51), overlap);
    }
}
