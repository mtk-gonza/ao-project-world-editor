using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapEnvironmentTests
{
    [Fact]
    public void Environment_editing_updates_climate_and_base_light()
    {
        var map = new WorldMap();
        new MapEnvironmentEditingService().Apply(map, 1, 2, 3, 400);
        Assert.Equal((byte)1, map.Environment.Rain); Assert.Equal((byte)2, map.Environment.Snow); Assert.Equal((byte)3, map.Environment.Fog);
        Assert.Equal(400, map.Environment.BaseLight); Assert.True(map.Info.Changed);
    }
}
