using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class SelectionBlockingTests
{
    [Fact]
    public void Selection_blocking_toggles_every_tile_in_the_rectangle()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-block-sel-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "mapa1.csm"); var map = new WorldMap(); map.Blocks[2, 2].Blocked = 0xF;
            new Infrastructure.CsmMapFileService().Save(map, path);
            var session = new WorldEditorSession(root); session.LoadMap(path);
            var changed = session.ToggleSelectionBlocked(new TileSelection(2, 2, 3, 3));
            Assert.Equal(4, changed);
            Assert.Equal((byte)0, session.CurrentMap!.Blocks[2, 2].Blocked);
            Assert.Equal((byte)0xF, session.CurrentMap.Blocks[3, 3].Blocked);
            session.ToggleSelectionBlocked(new TileSelection(2, 2, 3, 3));
            Assert.Equal((byte)0xF, session.CurrentMap.Blocks[2, 2].Blocked);
            Assert.Equal((byte)0, session.CurrentMap.Blocks[3, 3].Blocked);
        }
        finally { Directory.Delete(root, true); }
    }
}
