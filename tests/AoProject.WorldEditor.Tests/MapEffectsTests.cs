using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapEffectsTests
{
    [Fact]
    public void Lights_and_particles_are_mutable_and_clearable()
    {
        var map = new WorldSector();
        var effects = new MapEffectsEditingService();
        effects.SetLight(map, 4, 5, 20, 0x112233);
        effects.SetParticle(map, 6, 7, 1234);
        Assert.Equal(1, effects.ClearLights(map));
        Assert.Equal(1, effects.ClearParticles(map));
        Assert.Equal(0, map.Blocks[4, 5].Light.Range);
        Assert.Equal(0, map.Blocks[6, 7].ParticleIndex);
    }
}
