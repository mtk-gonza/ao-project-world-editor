using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class PasteHouseTests
{
    [Fact]
    public void House_region_is_copied_from_the_source_map_without_npcs()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-house-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var house = new WorldSector();
            house.Blocks[65, 23].Graphics[0].GraphicIndex = 501;
            house.Blocks[66, 24].Object.Index = 3;
            house.Blocks[65, 23].NpcIndex = 9;
            var housePath = Path.Combine(root, "mapa169.csm"); new SectorCsmFileService().Save(house, housePath);

            var targetPath = Path.Combine(root, "mapa1.csm"); new SectorCsmFileService().Save(new WorldSector(), targetPath);
            var session = new WorldEditorSession(root); session.LoadMap(targetPath);

            var copied = session.PasteHouse(housePath, 20, 40);
            Assert.Equal(80, copied);
            Assert.Equal(501, session.CurrentMap!.Blocks[20, 40].Graphics[0].GraphicIndex);
            Assert.Equal((short)3, session.CurrentMap.Blocks[21, 41].Object.Index);
            Assert.Equal((short)0, session.CurrentMap.Blocks[20, 40].NpcIndex);
            Assert.True(session.Undo());
            Assert.Equal(0, session.CurrentMap.Blocks[20, 40].Graphics[0].GraphicIndex);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
