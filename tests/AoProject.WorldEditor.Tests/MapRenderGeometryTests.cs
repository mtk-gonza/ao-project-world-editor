using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Domain.Rendering;
using AoProject.WorldEditor.Engine.Rendering;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapRenderGeometryTests
{
    [Fact]
    public void Roof_tiles_use_the_nearby_roof_zone_trigger()
    {
        var map = new WorldMap();
        map.Blocks[5, 5].Trigger = 60;
        map.Blocks[6, 6].Graphics[3].GraphicIndex = 123;

        var geometry = MapRenderGeometry.Build(map);
        var roof = Assert.Single(geometry.Tiles.Where(tile => tile.Layer == 3));

        Assert.Equal(60, roof.RoofTrigger);
    }

    [Fact]
    public void Geometry_ignores_tiles_outside_csm_map_size()
    {
        var map = new WorldMap { CsmMapSize = new CsmMapSize(3, 2, 4, 3) };
        map.Blocks[1, 1].Graphics[0].GraphicIndex = 10;
        map.Blocks[2, 3].Graphics[0].GraphicIndex = 11;
        map.Blocks[4, 4].Graphics[0].GraphicIndex = 12;

        var geometry = MapRenderGeometry.Build(map);

        Assert.DoesNotContain(geometry.Tiles, tile => tile.GraphicIndex == 10);
        Assert.Contains(geometry.Tiles, tile => tile.GraphicIndex == 11);
        Assert.DoesNotContain(geometry.Tiles, tile => tile.GraphicIndex == 12);
    }

    [Fact]
    public void Global_geometry_uses_only_each_sector_playable_core()
    {
        var first = new WorldMap();
        var second = new WorldMap();
        first.Blocks[12, 10].Graphics[0].GraphicIndex = 1;
        first.Blocks[13, 10].Graphics[0].GraphicIndex = 2;
        second.Blocks[14, 10].Graphics[0].GraphicIndex = 3;

        var geometry = MapRenderGeometry.Combine([
            (first, new GlobalChunkCoordinate(0, 0)),
            (second, new GlobalChunkCoordinate(1, 0))
        ], new WorldRenderOptions());

        Assert.DoesNotContain(geometry.Tiles, tile => tile.GraphicIndex == 1);
        Assert.Contains(geometry.Tiles, tile => tile.GraphicIndex == 2 && tile.X == 13);
        Assert.Contains(geometry.Tiles, tile => tile.GraphicIndex == 3 && tile.X == 89);
    }

    [Fact]
    public void Global_geometry_drops_the_shared_north_and_west_border_rows()
    {
        var first = new WorldMap();
        var second = new WorldMap();
        var third = new WorldMap();
        first.Blocks[88, 91].Graphics[0].GraphicIndex = 1;
        second.Blocks[13, 10].Graphics[0].GraphicIndex = 2;
        second.Blocks[14, 11].Graphics[0].GraphicIndex = 3;
        third.Blocks[13, 10].Graphics[0].GraphicIndex = 4;

        var geometry = MapRenderGeometry.Combine([
            (MapRenderGeometry.Build(first), new GlobalChunkCoordinate(0, 0)),
            (MapRenderGeometry.Build(second), new GlobalChunkCoordinate(1, 0)),
            (MapRenderGeometry.Build(third), new GlobalChunkCoordinate(0, 1))
        ]);

        Assert.Contains(geometry.Tiles, tile => tile.GraphicIndex == 1);
        Assert.DoesNotContain(geometry.Tiles, tile => tile.GraphicIndex == 2);
        Assert.Contains(geometry.Tiles, tile => tile.GraphicIndex == 3);
        Assert.DoesNotContain(geometry.Tiles, tile => tile.GraphicIndex == 4);
    }

    [Fact]
    public void Global_tiles_are_not_clipped_at_the_map_border()
    {
        // VB6 draws tall GRHs complete even when they cross the map border: the
        // shared border *tiles* are dropped by IsCoreTile, but the graphics that
        // extend from inside must not be cut (that made a roof look like a piece
        // one tile away).
        var map = new WorldMap();
        map.Blocks[20, 11].Graphics[0].GraphicIndex = 1;

        var geometry = MapRenderGeometry.Combine([
            (MapRenderGeometry.Build(map), new GlobalChunkCoordinate(0, 0))
        ]);

        var tile = Assert.Single(geometry.Tiles);
        Assert.True(tile.ClipLeft < 0, $"clipLeft={tile.ClipLeft}");
        Assert.True(tile.ClipTop < 0, $"clipTop={tile.ClipTop}");
    }

    [Fact]
    public void Duplicated_border_junction_collapses_to_the_same_row()
    {
        var upper = new WorldMap();
        var lower = new WorldMap();
        for (var x = 1; x <= WorldMap.Width; x++)
        {
            upper.Blocks[x, 91].Graphics[0].GraphicIndex = 10;
            lower.Blocks[x, 11].Graphics[0].GraphicIndex = 10;   // duplicated design
        }
        var placements = new[]
        {
            new GlobalMapPlacement(1, new GlobalChunkCoordinate(0, 0), "w"),
            new GlobalMapPlacement(2, new GlobalChunkCoordinate(0, 1), "w")
        };
        var maps = new Dictionary<short, WorldMap> { [1] = upper, [2] = lower };
        var stitcher = new WorldBorderStitcher(placements,
            id => maps.TryGetValue(id, out var map) ? WorldBorderStitcher.Sample(map) : null);
        var stitch = stitcher.ForAll(placements.Select(item => item.Chunk));

        Assert.Equal(1, stitch.Rows[new GlobalChunkCoordinate(0, 1)]);
        var geometry = MapRenderGeometry.Combine([
            (MapRenderGeometry.Build(upper), new GlobalChunkCoordinate(0, 0)),
            (MapRenderGeometry.Build(lower), new GlobalChunkCoordinate(0, 1))
        ], stitch.Columns, stitch.Rows);

        // Both border tiles land on the same global row: collapsed, not a gap.
        var rows = geometry.Tiles.Where(tile => tile.GraphicIndex == 10).Select(tile => tile.Y).Distinct().ToArray();
        Assert.Single(rows);
    }

    [Fact]
    public void Different_border_junction_keeps_both_rows()
    {
        var upper = new WorldMap();
        var lower = new WorldMap();
        for (var x = 1; x <= WorldMap.Width; x++)
        {
            upper.Blocks[x, 91].Graphics[0].GraphicIndex = 10;
            lower.Blocks[x, 11].Graphics[0].GraphicIndex = 11;   // different design
        }
        var placements = new[]
        {
            new GlobalMapPlacement(1, new GlobalChunkCoordinate(0, 0), "w"),
            new GlobalMapPlacement(2, new GlobalChunkCoordinate(0, 1), "w")
        };
        var maps = new Dictionary<short, WorldMap> { [1] = upper, [2] = lower };
        var stitcher = new WorldBorderStitcher(placements,
            id => maps.TryGetValue(id, out var map) ? WorldBorderStitcher.Sample(map) : null);
        var stitch = stitcher.ForAll(placements.Select(item => item.Chunk));

        Assert.Equal(0, stitch.Rows[new GlobalChunkCoordinate(0, 1)]);
        var geometry = MapRenderGeometry.Combine([
            (MapRenderGeometry.Build(upper), new GlobalChunkCoordinate(0, 0)),
            (MapRenderGeometry.Build(lower), new GlobalChunkCoordinate(0, 1))
        ], stitch.Columns, stitch.Rows);

        Assert.Equal(2, geometry.Tiles.Select(tile => tile.Y).Distinct().Count());
    }

    [Fact]
    public void Shared_border_rows_are_still_dropped_without_clipping()
    {
        var first = new WorldMap();
        var second = new WorldMap();
        first.Blocks[20, 91].Graphics[0].GraphicIndex = 1;
        second.Blocks[20, 10].Graphics[0].GraphicIndex = 2;
        second.Blocks[20, 11].Graphics[0].GraphicIndex = 3;

        var geometry = MapRenderGeometry.Combine([
            (MapRenderGeometry.Build(first), new GlobalChunkCoordinate(0, 0)),
            (MapRenderGeometry.Build(second), new GlobalChunkCoordinate(0, 1))
        ]);

        Assert.Contains(geometry.Tiles, tile => tile.GraphicIndex == 1);
        Assert.DoesNotContain(geometry.Tiles, tile => tile.GraphicIndex == 2);
        Assert.Contains(geometry.Tiles, tile => tile.GraphicIndex == 3);
    }
}
