using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapInfoEditingTests
{
    [Fact]
    public void Map_info_updates_all_reference_fields_and_marks_map_changed()
    {
        var map = new WorldSector();
        var values = new SectorInfoValues("  Nuevo mapa ", "12", 7, true, 1, 0, 1, 1, "bosque", "zona", "nivel", 1, "&H010203");
        new MapInfoEditingService().Apply(map, values);

        Assert.Equal("Nuevo mapa", map.Info.Name);
        Assert.Equal((short)7, map.Info.MapVersion);
        Assert.True(map.Info.PlayerKilling);
        Assert.Equal((byte)1, map.Info.MagicDisabled);
        Assert.Equal("&H010203", map.Info.Light);
        Assert.True(map.Info.Changed);
    }
}
