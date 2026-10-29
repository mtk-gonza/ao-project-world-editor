using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class UndoToggleTests
{
    [Fact]
    public void Disabling_undo_applies_edits_without_retaining_history()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-undo-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "mapa1.csm"); new Infrastructure.CsmMapFileService().Save(new WorldMap(), path);
            var session = new WorldEditorSession(root); session.LoadMap(path); session.ConfigureUndo(false); session.SetGraphic(3, 4, 0, 55);
            Assert.Equal(55, session.CurrentMap!.Blocks[3, 4].Graphics[0].GraphicIndex); Assert.False(session.CanUndo); Assert.False(session.Undo());
        }
        finally { Directory.Delete(root, true); }
    }
}
