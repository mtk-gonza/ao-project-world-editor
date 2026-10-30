using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GlobalRuntimeCoordinateTests
{
    [Fact]
    public void Runtime_resolver_returns_global_coordinate_without_map_id()
    {
        var map = new WorldMap();
        var world = new GlobalWorldDocument(
            new GlobalWorldAssembly
            {
                Width = 1,
                Height = 1,
                Placements = [new GlobalMapPlacement(842, new GlobalChunkCoordinate(0, 0), "world")]
            },
            new Dictionary<short, WorldMap> { [842] = map },
            new Dictionary<short, string> { [842] = "mapa842.csm" }, 842);

        Assert.True(world.TryResolveGlobal(12, 9, out var coordinate));
        Assert.Equal(new GlobalTileCoordinate(12, 9), coordinate);
    }

    [Fact]
    public void Legacy_exit_is_converted_to_global_target_only_at_boundary()
    {
        var world = new GlobalWorldDocument(
            new GlobalWorldAssembly
            {
                Width = 2,
                Height = 1,
                Placements =
                [
                    new GlobalMapPlacement(1, new GlobalChunkCoordinate(0, 0), "world"),
                    new GlobalMapPlacement(2, new GlobalChunkCoordinate(1, 0), "world")
                ]
            },
            new Dictionary<short, WorldMap> { [1] = new(), [2] = new() },
            new Dictionary<short, string> { [1] = "mapa1.csm", [2] = "mapa2.csm" }, 1);

        Assert.True(world.TryResolveLegacyExit(2, 14, 50, out var target));
        Assert.Equal(new GlobalExitTarget(88, 49), target);
    }

    [Fact]
    public void Compacted_stitch_audit_does_not_mutate_source_maps()
    {
        var source = new WorldMap();
        source.Blocks[13, 10].Graphics[0].GraphicIndex = 123;
        var world = new GlobalWorldDocument(
            new GlobalWorldAssembly
            {
                Width = 1,
                Height = 1,
                Placements = [new GlobalMapPlacement(7, new GlobalChunkCoordinate(0, 0), "world")]
            },
            new Dictionary<short, WorldMap> { [7] = source },
            new Dictionary<short, string> { [7] = "mapa7.csm" }, 7);

        var result = new GlobalStitchAuditService().AuditCompactedMaterialization(world);

        var entry = Assert.Single(result);
        Assert.Equal(0, entry.ShiftX);
        Assert.Equal(0, entry.ShiftY);
        Assert.Equal(123, source.Blocks[13, 10].Graphics[0].GraphicIndex);
    }
}
