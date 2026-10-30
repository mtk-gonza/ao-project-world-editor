using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AdjacentTransferTests
{
    [Fact]
    public void Adjacent_transfers_skip_fully_blocked_tiles_and_set_reference_destinations()
    {
        var map = new WorldSector();
        map.Blocks[20, 10].Blocked = 0xF;
        var transfer = new AdjacentTransfer(true, 42, 20, 1, true, 10, 19, 21);

        var changed = new AdjacentTransferService().Apply(map, [transfer, default, default, default]);

        Assert.Equal(2, changed);
        Assert.Equal((short)42, map.Blocks[19, 10].TileExit.Map);
        Assert.Equal((short)19, map.Blocks[19, 10].TileExit.X);
        Assert.Equal((short)1, map.Blocks[19, 10].TileExit.Y);
        Assert.Equal((short)0, map.Blocks[20, 10].TileExit.Map);
        Assert.True(map.Info.Changed);
    }
}
