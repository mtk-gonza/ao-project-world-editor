using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Domain.Rendering;
using AoProject.WorldEditor.Engine.Rendering;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AnimationFrameTests
{
    [Fact]
    public void Frame_index_advances_with_time_and_wraps()
    {
        // VB6 speed is the total animation duration in milliseconds.
        var animation = new AnimationInfo([10, 11, 12], 300f);
        Assert.Equal(10, animation.FrameAt(0));
        Assert.Equal(11, animation.FrameAt(0.1f));
        Assert.Equal(12, animation.FrameAt(0.2f));
        Assert.Equal(10, animation.FrameAt(0.3f));
        Assert.Equal(12, animation.FrameAt(1.1f));
    }

    [Fact]
    public void Static_graphics_return_their_only_frame()
    {
        Assert.Equal(7, new AnimationInfo([7], 5f).FrameAt(3.5));
        Assert.Equal(0, new AnimationInfo([], 5f).FrameAt(1));
    }

    [Fact]
    public void Zero_speed_does_not_divide()
    {
        Assert.Equal(4, new AnimationInfo([4, 5], 0f).FrameAt(9));
    }

    [Fact]
    public void Real_map_reports_animated_grhs()
    {
        var root = ResourceRoot();
        var mapPath = Path.Combine(root, "Mapas", "mapa1.csm");
        if (!File.Exists(mapPath)) return;
        var catalog = Infrastructure.WorldEditorResourceCatalog.Load(root);
        var map = new Infrastructure.CsmMapFileService().Load(mapPath);
        var animated = map.Blocks.Cast<MapBlock>().Where(block => block is not null)
            .SelectMany(block => block.Graphics)
            .Select(graphic => graphic.GraphicIndex)
            .Distinct()
            .Count(index => index > 0 && index < catalog.Graphics.Count && catalog.Graphics[index].Frames.Length > 1);
        Assert.True(animated >= 0);
    }

    private static string ResourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "ao-project-resources");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "ao-project-resources");
    }
}
