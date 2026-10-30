using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Domain.Rendering;
using AoProject.WorldEditor.Engine.Rendering;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapOverlayTests
{
    [Fact]
    public void Overlay_geometry_reports_enabled_tile_annotations()
    {
        var map = new WorldMap();
        map.Blocks[4, 5].Blocked = 0xF;
        map.Blocks[4, 5].Trigger = 2;
        map.Blocks[4, 5].Light.Range = 10;
        var options = new WorldRenderOptions { ShowBlocked = true, ShowTriggers = true, ShowLights = true };
        var overlays = MapOverlayGeometry.Build(map, options, (4, 5));

        Assert.Contains(overlays, overlay => overlay.Kind == OverlayKind.Blocked);
        Assert.Contains(overlays, overlay => overlay.Kind == OverlayKind.Trigger);
        Assert.Contains(overlays, overlay => overlay.Kind == OverlayKind.Light);
        Assert.Contains(overlays, overlay => overlay.Kind == OverlayKind.Selection);
        Assert.Contains(overlays, overlay => overlay.Kind == OverlayKind.Blocked && overlay.GraphicIndex == MapOverlayGeometry.BlockedBaseGraphic + 1);
        Assert.Contains(overlays, overlay => overlay.Kind == OverlayKind.Trigger && overlay.GraphicIndex >= MapOverlayGeometry.TriggerDigitBase);
    }

    [Fact]
    public void Grid_overlay_can_be_clipped_to_the_visible_rect()
    {
        var options = new WorldRenderOptions { ShowGrid = true };
        var overlays = MapOverlayGeometry.Build(new WorldMap(), options, (5, 5), (MinX: 4, MaxX: 6, MinY: 10, MaxY: 12));

        var grid = overlays.Where(overlay => overlay.Kind == OverlayKind.Grid).ToArray();
        Assert.Equal(9, grid.Length);
        Assert.All(grid, overlay => Assert.InRange(overlay.X, 4, 6));
        Assert.All(grid, overlay => Assert.InRange(overlay.Y, 10, 12));
        // The selection is always emitted, even outside the clip.
        Assert.Contains(overlays, overlay => overlay.Kind == OverlayKind.Selection && overlay.X == 5);
    }

    [Fact]
    public void Clip_outside_the_map_produces_no_tile_overlays()
    {
        var options = new WorldRenderOptions { ShowGrid = true, ShowBlocked = true };
        var overlays = MapOverlayGeometry.Build(new WorldMap(), options, null,
            (MinX: 500, MaxX: 600, MinY: 500, MaxY: 600));

        Assert.DoesNotContain(overlays, overlay => overlay.Kind == OverlayKind.Grid);
        Assert.DoesNotContain(overlays, overlay => overlay.Kind == OverlayKind.Blocked);
    }

    [Fact]
    public void Grid_overlay_is_one_annotation_per_map_tile()
    {
        var overlays = MapOverlayGeometry.Build(new WorldMap(), new WorldRenderOptions { ShowGrid = true });

        Assert.Equal(WorldMap.Width * WorldMap.Height, overlays.Count(overlay => overlay.Kind == OverlayKind.Grid));
        Assert.All(overlays.Where(overlay => overlay.Kind == OverlayKind.Grid), overlay =>
        {
            Assert.Equal(1, overlay.Width);
            Assert.Equal(1, overlay.Height);
            Assert.Equal(MapOverlayGeometry.GridGraphic, overlay.GraphicIndex);
        });
    }

    [Fact]
    public void Marker_graphics_include_the_legacy_grid_and_editor_markers()
    {
        var markers = MapOverlayGeometry.MarkerGraphics().ToArray();

        Assert.Equal(MapOverlayGeometry.GridGraphic, markers[0]);
        Assert.Contains(MapOverlayGeometry.TransferGraphic, markers);
        Assert.Contains(MapOverlayGeometry.BlockedBaseGraphic + 1, markers);
        Assert.Contains(MapOverlayGeometry.TriggerDigitBase + 9, markers);
    }

    [Fact]
    public void Selection_overlay_geometry_uses_one_tile_box()
    {
        var map = new WorldMap();
        var overlays = MapOverlayGeometry.Build(map, new WorldRenderOptions(), (4, 5));

        var selection = Assert.Single(overlays.Where(overlay => overlay.Kind == OverlayKind.Selection));
        Assert.Equal(4, selection.X);
        Assert.Equal(5, selection.Y);
        Assert.Equal(1, selection.Width);
        Assert.Equal(1, selection.Height);
    }

    [Fact]
    public void Overlay_geometry_respects_the_effective_csm_domain()
    {
        var map = new WorldMap { CsmMapSize = new CsmMapSize(3, 2, 4, 2) };
        map.Blocks[10, 10].Blocked = 0xF;
        var overlays = MapOverlayGeometry.Build(map, new WorldRenderOptions { ShowGrid = true, ShowBlocked = true });

        Assert.Equal(6, overlays.Count(overlay => overlay.Kind == OverlayKind.Grid));
        Assert.DoesNotContain(overlays, overlay => overlay.X == 10 || overlay.Y == 10);
    }
}
