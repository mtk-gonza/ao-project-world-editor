using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AdjacentTransferPresetTests
{
    [Fact]
    public void Reference_presets_expose_distinct_border_coordinates()
    {
        Assert.Equal((short)91, AdjacentTransferService.BasicBounds.BottomY);
        Assert.Equal((short)94, AdjacentTransferService.UllathorpeBounds.BottomY);
        Assert.Equal((short)92, AdjacentTransferService.UllathorpeBounds.RightX);
        Assert.NotEqual(AdjacentTransferService.BasicBounds, AdjacentTransferService.UllathorpeBounds);
    }
}
