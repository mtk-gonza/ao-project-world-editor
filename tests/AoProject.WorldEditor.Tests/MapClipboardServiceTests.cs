using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapClipboardServiceTests
{
    [Fact]
    public void Clipboard_pastes_single_selected_tile_graphics()
    {
        var map = new WorldSector();
        map.Blocks[4, 5].Graphics[0].GraphicIndex = 10; map.Blocks[4, 5].Graphics[3].GraphicIndex = 40;
        map.Blocks[4, 5].Blocked = 0xF; map.Blocks[4, 5].Object.Index = 2; map.Blocks[4, 5].Trigger = 8; map.Blocks[4, 5].TileExit.SectorId = 7; map.Blocks[4, 5].NpcIndex = 3;
        var clipboard = new MapClipboardService();
        clipboard.Copy(map, new TileSelection(4, 5, 4, 5));
        clipboard.Paste(map, 20, 21);
        Assert.Equal(10, map.Blocks[20, 21].Graphics[0].GraphicIndex);
        Assert.Equal(40, map.Blocks[20, 21].Graphics[3].GraphicIndex);
        Assert.Equal((byte)0xF, map.Blocks[20, 21].Blocked); Assert.Equal((short)2, map.Blocks[20, 21].Object.Index);
        Assert.Equal((short)8, map.Blocks[20, 21].Trigger); Assert.Equal((short)7, map.Blocks[20, 21].TileExit.SectorId); Assert.Equal((short)0, map.Blocks[20, 21].NpcIndex);
    }

    [Fact]
    public void Session_paste_is_undoable_as_one_operation()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-paste-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "mapa1.csm"); new Infrastructure.SectorCsmFileService().Save(new WorldSector(), path);
            var session = new WorldEditorSession(root); session.LoadMap(path); session.CurrentMap!.Blocks[2, 2].Graphics[0].GraphicIndex = 77;
            var clipboard = new MapClipboardService(); clipboard.Copy(session.CurrentMap, new TileSelection(2, 2, 2, 2));
            session.PasteClipboard(clipboard, 8, 9);
            Assert.Equal(77, session.CurrentMap.Blocks[8, 9].Graphics[0].GraphicIndex); Assert.True(session.Undo()); Assert.Equal(0, session.CurrentMap.Blocks[8, 9].Graphics[0].GraphicIndex);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Cut_replaces_selected_blocks_with_empty_blocks_and_is_undoable()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-cut-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "mapa1.csm"); var map = new WorldSector();
            map.Blocks[2, 3].Graphics[0].GraphicIndex = 44; map.Blocks[2, 3].Object.Index = 5; map.Blocks[2, 3].NpcIndex = 6;
            new Infrastructure.SectorCsmFileService().Save(map, path); var session = new WorldEditorSession(root); session.LoadMap(path);
            var clipboard = new MapClipboardService(); session.CutClipboard(clipboard, new TileSelection(2, 3, 2, 3));
            Assert.Equal(0, session.CurrentMap!.Blocks[2, 3].Graphics[0].GraphicIndex); Assert.Equal((short)0, session.CurrentMap.Blocks[2, 3].Object.Index);
            Assert.True(session.Undo()); Assert.Equal(44, session.CurrentMap.Blocks[2, 3].Graphics[0].GraphicIndex); Assert.Equal((short)5, session.CurrentMap.Blocks[2, 3].Object.Index);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Paste_does_not_modify_tiles_outside_csm_domain()
    {
        var map = new WorldSector { CsmSectorSize = new CsmSectorSize(3, 2, 3, 2) };
        map.Blocks[2, 2].Graphics[0].GraphicIndex = 17;
        map.Blocks[1, 1].Graphics[0].GraphicIndex = 99;
        var clipboard = new MapClipboardService();
        clipboard.Copy(map, new TileSelection(2, 2, 2, 2));

        clipboard.Paste(map, 1, 1);

        Assert.Equal(99, map.Blocks[1, 1].Graphics[0].GraphicIndex);
        Assert.Equal(17, map.Blocks[2, 2].Graphics[0].GraphicIndex);
    }
}
