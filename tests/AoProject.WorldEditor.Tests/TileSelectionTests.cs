using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class TileSelectionTests
{
    [Fact]
    public void Selection_normalization_supports_reverse_shift_drag()
    {
        var selection = new TileSelection(20, 30, 10, 12).Normalize();
        Assert.Equal(10, selection.StartX); Assert.Equal(12, selection.StartY);
        Assert.Equal(20, selection.EndX); Assert.Equal(30, selection.EndY);
        Assert.Equal(11, selection.Width); Assert.Equal(19, selection.Height);
    }
}
