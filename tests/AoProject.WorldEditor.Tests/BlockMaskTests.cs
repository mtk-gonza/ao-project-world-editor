using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class BlockMaskTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(0xF)]
    [InlineData(0)]
    public void Block_mask_is_applied_to_every_tile_of_the_selection(byte mask)
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-mask-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "mapa1.csm"); new Infrastructure.CsmMapFileService().Save(new WorldMap(), path);
            var session = new WorldEditorSession(root); session.LoadMap(path);
            var changed = session.ApplyBlockMask(new TileSelection(4, 5, 6, 7), mask);
            Assert.Equal(9, changed);
            for (var x = 4; x <= 6; x++)
            for (var y = 5; y <= 7; y++)
                Assert.Equal(mask, session.CurrentMap!.Blocks[x, y].Blocked);
            Assert.Equal((byte)0, session.CurrentMap!.Blocks[7, 5].Blocked);
            Assert.True(session.Undo());
            Assert.Equal((byte)0, session.CurrentMap.Blocks[4, 5].Blocked);
        }
        finally { Directory.Delete(root, true); }
    }
}
