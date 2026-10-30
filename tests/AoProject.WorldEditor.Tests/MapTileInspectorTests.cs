using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapTileInspectorTests
{
    [Fact]
    public void Inspector_describes_reference_tile_payloads()
    {
        var map = new WorldSector(); var block = map.Blocks[4, 5];
        block.Blocked = 1; block.TileExit.Map = 8; block.TileExit.X = 20; block.TileExit.Y = 21; block.NpcIndex = 500; block.Object.Index = 1; block.Object.Amount = 2; block.Trigger = 3;
        block.Graphics[0].GraphicIndex = 10;
        var objects = new ObjectData[2]; objects[1] = new ObjectData { Name = "Objeto" };
        var npcs = new NpcData[501]; npcs[500] = new NpcData { Name = "Hostil" };
        var text = new MapTileInspector().Describe(map, 4, 5, objects, npcs);
        Assert.Contains("BLOQ", text); Assert.Contains("Hostil", text); Assert.Contains("Objeto", text); Assert.Contains("Capa1: 10", text);
    }
}
