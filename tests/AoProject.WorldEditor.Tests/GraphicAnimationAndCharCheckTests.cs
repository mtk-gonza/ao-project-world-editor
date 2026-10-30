using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Domain.Rendering;
using AoProject.WorldEditor.Engine.Rendering;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GraphicAnimationAndCharCheckTests
{
    [Fact]
    public void Animation_advances_only_after_the_frame_duration()
    {
        var graphic = new GraphicData { Frames = [1, 2], Speed = 10f };
        var animation = new GraphicAnimation { GraphicIndex = 1 };
        Assert.False(GraphicAnimationService.ShowNextFrame(graphic, ref animation, 0.05f));
        Assert.True(GraphicAnimationService.ShowNextFrame(graphic, ref animation, 0.06f));
        Assert.True(animation.FrameCounter < 0.1f);
    }

    [Fact]
    public void Single_frame_graphics_never_advance()
    {
        var graphic = new GraphicData { Frames = [1], Speed = 10f };
        var animation = new GraphicAnimation { GraphicIndex = 1 };
        Assert.False(GraphicAnimationService.ShowNextFrame(graphic, ref animation, 1f));
    }

    [Fact]
    public void Animation_info_starts_at_the_first_frame_and_wraps()
    {
        var animation = new AnimationInfo([10, 11, 12], 900f);

        Assert.Equal(10, animation.FrameAt(0d));
        Assert.Equal(11, animation.FrameAt(0.31d));
        Assert.Equal(10, animation.FrameAt(0.91d));
    }

    [Fact]
    public void Character_check_reports_active_slots()
    {
        var list = new CharacterListService();
        list.Make(1, 1, 2, 3, 10, 10);
        Assert.True(list.Check(1)); Assert.False(list.Check(2));
        list.Erase(1);
        Assert.False(list.Check(1));
    }
}
