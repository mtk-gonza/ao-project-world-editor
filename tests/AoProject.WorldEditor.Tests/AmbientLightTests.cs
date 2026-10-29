using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AmbientLightTests
{
    [Fact]
    public void Ambient_table_matches_the_reference_hours()
    {
        var service = new AmbientLightService();
        Assert.Equal(25, service.Table.Count);
        Assert.Equal(new AmbientColor(87, 61, 43), service.Ambient(0));
        Assert.Equal(new AmbientColor(212, 205, 207), service.Ambient(7));
        Assert.Equal(new AmbientColor(255, 255, 255), service.Ambient(12));
        Assert.Equal(new AmbientColor(156, 142, 83), service.Ambient(19));
        Assert.Equal(new AmbientColor(87, 61, 43), service.Ambient(24));
    }

    [Fact]
    public void Change_day_effect_steps_channels_towards_the_target()
    {
        var service = new AmbientLightService();
        var result = service.ChangeDayEffect(100, 100, 100, 200, 200, 200);
        Assert.Equal(101, result.Red);
        Assert.Equal(101, result.Green);
        Assert.Equal(101, result.Blue);

        var down = service.ChangeDayEffect(100, 100, 100, 50, 50, 50);
        Assert.Equal(99, down.Red);
    }

    [Fact]
    public void Negative_channels_are_clamped_to_one()
    {
        var service = new AmbientLightService();
        var result = service.ChangeDayEffect(0, 0, 0, -5, -5, -5);
        Assert.Equal(1, result.Red);
        Assert.Equal(1, result.Green);
        Assert.Equal(1, result.Blue);
    }
}

public sealed class ParticleTypeLookupTests
{
    [Fact]
    public void Particle_type_lookup_finds_the_stream_that_contains_a_grh()
    {
        var particles = new List<ParticleData>
        {
            new() { Name = "A", Grhs = [10, 11] },
            new() { Name = "B", Grhs = [20] }
        };
        var service = new ParticleCatalogService();
        Assert.Equal(1, service.TypeForGrh(particles, 11));
        Assert.Equal(2, service.TypeForGrh(particles, 20));
        Assert.Equal(0, service.TypeForGrh(particles, 99));
    }
}
