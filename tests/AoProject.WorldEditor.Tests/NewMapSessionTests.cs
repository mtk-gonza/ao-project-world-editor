using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class NewMapSessionTests
{
    [Fact]
    public void New_map_resets_path_and_undo_history()
    {
        var session = new WorldEditorSession(Path.GetTempPath());
        session.NewMap();
        Assert.NotNull(session.CurrentMap); Assert.Null(session.CurrentMapPath); Assert.False(session.CanUndo);
        Assert.Equal(1, session.CurrentMap!.Blocks[50, 50].Graphics[0].GraphicIndex);
        Assert.Equal("Nuevo Mapa", session.CurrentMap.Info.Name);
        Assert.Equal("BOSQUE", session.CurrentMap.Info.Terrain);
        Assert.True(session.CurrentMap.Info.PlayerKilling);
    }
}
