using AoProject.WorldEditor.Domain.Rendering;
using AoProject.WorldEditor.Engine.Rendering;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class WorldCameraTests
{
    [Fact]
    public void Camera_centering_places_destination_at_view_center()
    {
        var camera = new WorldCamera();
        camera.CenterOnTile(50, 50, 800, 600);
        Assert.Equal(48 * MapRenderGeometry.TilePixels - 400, camera.OffsetX);
        Assert.Equal(49 * MapRenderGeometry.TilePixels - 300, camera.OffsetY);
        Assert.Equal((49, 50), camera.ScreenToTile(400, 300));
    }

    [Fact]
    public void Global_camera_centering_uses_zero_based_world_coordinates()
    {
        var camera = new WorldCamera();
        camera.CenterOnGlobalTile(50, 50, 800, 600);

        Assert.Equal((50, 50), camera.ScreenToGlobalTile(400, 300));
    }
}
