using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class TileBlockingTests
{
    [Fact]
    public void Tile_blocking_updates_only_the_requested_tile()
    {
        var map = new WorldMap();
        var session = new WorldEditorSession(Path.GetTempPath());
        var path = Path.Combine(Path.GetTempPath(), "ao-block-" + Guid.NewGuid().ToString("N") + ".csm");
        try
        {
            new Infrastructure.CsmMapFileService().Save(map, path); session.LoadMap(path);
            session.SetTileBlocked(8, 9, 0xF);
            Assert.Equal((byte)0xF, session.CurrentMap!.Blocks[8, 9].Blocked); Assert.Equal((byte)0, session.CurrentMap.Blocks[8, 10].Blocked);
            Assert.True(session.Undo()); Assert.Equal((byte)0, session.CurrentMap.Blocks[8, 9].Blocked);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
