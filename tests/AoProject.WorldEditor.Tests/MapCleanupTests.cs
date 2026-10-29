using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapCleanupTests
{
    [Fact]
    public void Cleanup_removes_each_supported_entity_type()
    {
        var map = new WorldMap();
        map.Blocks[1, 1].NpcIndex = 4;
        map.Blocks[2, 2].Object.Index = 5;
        map.Blocks[3, 3].Trigger = 7;
        map.Blocks[4, 4].TileExit.Map = 9;
        var cleanup = new MapCleanupService();

        Assert.Equal(1, cleanup.RemoveNpcs(map));
        Assert.Equal(1, cleanup.RemoveObjects(map));
        Assert.Equal(1, cleanup.RemoveTriggers(map));
        Assert.Equal(1, cleanup.RemoveExits(map));
        Assert.Equal(0, map.Blocks[1, 1].NpcIndex);
        Assert.True(map.Info.Changed);
    }

    [Fact]
    public void Clear_map_restores_base_graphic_and_removes_all_editable_payloads()
    {
        var map = new WorldMap();
        var block = map.Blocks[5, 6];
        block.Graphics[0].GraphicIndex = 99; block.Graphics[1].GraphicIndex = 98; block.Blocked = 0xF;
        block.NpcIndex = 2; block.Object.Index = 3; block.TileExit.Map = 4; block.Trigger = 5;
        block.Light.Range = 8; block.ParticleIndex = 9;

        new MapCleanupService().ClearMap(map);

        Assert.Equal(1, block.Graphics[0].GraphicIndex);
        Assert.All(block.Graphics.Skip(1).Take(4), graphic => Assert.Equal(0, graphic.GraphicIndex));
        Assert.Equal((short)0, block.NpcIndex); Assert.Equal((short)0, block.Object.Index);
        Assert.Equal((short)0, block.TileExit.Map); Assert.Equal((short)0, block.Trigger);
        Assert.Equal((short)0, block.Light.Range); Assert.Equal(0, block.ParticleIndex);
    }
}
