using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class WorldCoordinateAuditTests
{
    [Fact]
    public void A_two_cell_world_with_canonical_exits_has_no_issues()
    {
        var layouts = new[]
        {
            new MapsWorldDataService.WorldLayout(1, 3, 1, new short[] { 1, 2, 0 })
        };
        var info = new Dictionary<short, MapCoordinateInfo>
        {
            [1] = Info(1, new MapBorderExit(GlobalWorldSide.East, 2, 88, 50, 14, 50)),
            [2] = Info(2, new MapBorderExit(GlobalWorldSide.West, 1, 13, 50, 87, 50))
        };

        var result = new WorldCoordinateAuditService().Audit(layouts, new short[] { 1, 2 }, info);

        var world = Assert.Single(result);
        Assert.Equal(2, world.Placements);
        Assert.Equal(0, world.Errors);
    }

    [Fact]
    public void Reports_a_missing_canonical_exit()
    {
        var layouts = new[]
        {
            new MapsWorldDataService.WorldLayout(1, 3, 1, new short[] { 1, 2, 0 })
        };
        var info = new Dictionary<short, MapCoordinateInfo>
        {
            [1] = Info(1, new MapBorderExit(GlobalWorldSide.East, 2, 88, 50, 14, 50)),
            [2] = Info(2)
        };

        var result = new WorldCoordinateAuditService().Audit(layouts, new short[] { 1, 2 }, info);

        Assert.Contains(result.Single().Issues, issue => issue.Code == "WORLD-EXIT" && issue.Severity == AuditSeverity.Error);
    }

    [Fact]
    public void Repeated_filler_maps_are_reported_as_info_not_errors()
    {
        var layouts = new[]
        {
            new MapsWorldDataService.WorldLayout(1, 2, 1, new short[] { 1, 1 })
        };
        var info = new Dictionary<short, MapCoordinateInfo> { [1] = Info(1) };

        var result = new WorldCoordinateAuditService().Audit(layouts, new short[] { 1 }, info);

        var world = Assert.Single(result);
        Assert.Equal(0, world.Errors);
        Assert.Contains(world.Issues, issue => issue.Code == "WORLD-FILLER" && issue.Severity == AuditSeverity.Info);
    }

    [Fact]
    public void Reports_a_core_outside_the_csm_size()
    {
        var layouts = new[]
        {
            new MapsWorldDataService.WorldLayout(1, 1, 1, new short[] { 1 })
        };
        var info = new Dictionary<short, MapCoordinateInfo>
        {
            [1] = new MapCoordinateInfo(1, 1, 10, 1, 10, [])
        };

        var result = new WorldCoordinateAuditService().Audit(layouts, new short[] { 1 }, info);

        Assert.Contains(result.Single().Issues, issue => issue.Code == "WORLD-CSM-EMPTY");
    }

    private static MapCoordinateInfo Info(short id, params MapBorderExit[] exits) =>
        new(id, 1, 100, 1, 100, exits);
}
