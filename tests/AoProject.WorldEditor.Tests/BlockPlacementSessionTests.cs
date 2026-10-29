using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class BlockPlacementSessionTests
{
    [Fact]
    public void Block_placement_is_one_undoable_session_operation()
    {
        var session = new WorldEditorSession(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "ao-project-resources")));
        var path = Path.Combine(Path.GetTempPath(), $"world-editor-{Guid.NewGuid():N}.csm");
        try
        {
            new Infrastructure.CsmMapFileService().Save(new WorldMap(), path);
            session.LoadMap(path);
            var type = new BlockPresetType { Layer = 1, TilesX = 2, TilesY = 1 };
            var item = new BlockPreset { GraphicIndex = 700 };
            session.PlaceBlock(type, item, 3, 4);
            Assert.Equal(700, session.CurrentMap!.Blocks[3, 4].Graphics[0].GraphicIndex);
            Assert.True(session.Undo());
            Assert.Equal(0, session.CurrentMap.Blocks[3, 4].Graphics[0].GraphicIndex);
        }
        finally { File.Delete(path); }
    }
}
