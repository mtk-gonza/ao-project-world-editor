using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapEffectsSessionTests
{
    [Fact]
    public void Effects_are_stored_in_map_blocks_and_can_be_undone_as_bulk_operations()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-effects-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "mapa1.csm"); new Infrastructure.CsmMapFileService().Save(new WorldMap(), path);
            var session = new WorldEditorSession(root); session.LoadMap(path);
            session.SetLight(4, 5, 10, 0x112233); session.SetParticle(6, 7, 12);
            Assert.Equal((short)10, session.CurrentMap!.Blocks[4, 5].Light.Range); Assert.Equal(12, session.CurrentMap.Blocks[6, 7].ParticleIndex);
            Assert.True(session.Undo()); Assert.Equal(0, session.CurrentMap.Blocks[6, 7].ParticleIndex);
        }
        finally { Directory.Delete(root, true); }
    }
}
