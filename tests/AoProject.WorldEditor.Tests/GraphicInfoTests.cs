using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GraphicInfoTests
{
    [Fact]
    public void Graphic_info_reports_dimensions_frames_and_animation_state()
    {
        var graphics = new GraphicData[2];
        graphics[1] = new GraphicData { FileNumber = 8, PixelWidth = 32, PixelHeight = 32, Frames = [1, 2], Speed = 6 };
        var info = new GraphicInfoService().Get(graphics, 1);
        Assert.Equal(8, info.Bmp); Assert.Equal((short)32, info.Width); Assert.Equal(2, info.FrameCount); Assert.True(info.Animated);
    }
}
