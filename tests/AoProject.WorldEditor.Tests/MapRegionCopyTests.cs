using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapRegionCopyTests
{
    [Fact]
    public void Region_copy_preserves_payload_but_drops_npc()
    {
        var source = new WorldSector(); var destination = new WorldSector();
        var block = source.Blocks[1, 10]; block.Graphics[0].GraphicIndex = 42; block.Blocked = 15; block.Trigger = 9;
        block.Object.Index = 3; block.Object.Amount = 2; block.NpcIndex = 8; block.TileExit.Map = 7; block.TileExit.X = 20; block.TileExit.Y = 21;
        Assert.Equal(1, new MapRegionCopyService().Copy(source, new MapRegion(1, 10, 1, 10), destination, 50, 90));
        var copied = destination.Blocks[50, 90];
        Assert.Equal(42, copied.Graphics[0].GraphicIndex); Assert.Equal((byte)15, copied.Blocked); Assert.Equal((short)9, copied.Trigger);
        Assert.Equal((short)3, copied.Object.Index); Assert.Equal((short)7, copied.TileExit.Map); Assert.Equal((short)0, copied.NpcIndex);
    }

    [Fact]
    public void Region_copy_skips_destination_tiles_outside_csm_domain()
    {
        var source = new WorldSector();
        source.Blocks[1, 1].Graphics[0].GraphicIndex = 42;
        var destination = new WorldSector { CsmSectorSize = new CsmSectorSize(3, 2, 3, 2) };

        var copied = new MapRegionCopyService().Copy(source, new MapRegion(1, 1, 1, 1), destination, 1, 1);

        Assert.Equal(0, copied);
        Assert.Equal(0, destination.Blocks[1, 1].Graphics[0].GraphicIndex);
    }
}
