using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AudioSelectionTests
{
    [Fact]
    public void Music_selection_is_stored_without_extension()
    {
        var map = new WorldMap();
        new MapInfoEditingService().Apply(map, new MapInfoValues("", "theme", 0, false, 0, 0, 0, 0, "", "", "", 0, ""));
        Assert.Equal("theme", map.Info.Music);
    }
}
