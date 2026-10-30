using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class SelectedEffectsRemovalTests
{
    [Fact]
    public void Selected_effect_removal_clears_payloads_without_touching_neighbor()
    {
        var map = new WorldSector(); map.Blocks[3, 4].Light.Range = 9; map.Blocks[3, 4].ParticleIndex = 12; map.Blocks[4, 4].Light.Range = 8;
        var effects = new MapEffectsEditingService();
        effects.SetLight(map, 3, 4, 0, 0); effects.ClearParticles(map);
        Assert.Equal((short)0, map.Blocks[3, 4].Light.Range); Assert.Equal(0, map.Blocks[3, 4].ParticleIndex); Assert.Equal((short)8, map.Blocks[4, 4].Light.Range);
    }
}
