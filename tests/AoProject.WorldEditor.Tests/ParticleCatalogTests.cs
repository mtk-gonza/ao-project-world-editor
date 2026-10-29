using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class ParticleCatalogTests
{
    [Fact]
    public void Loader_reads_every_reference_particle()
    {
        var path = Path.Combine(ResourceRoot(), "init", "particles.ind");
        if (!File.Exists(path)) return;
        var particles = new Infrastructure.ParticleCatalogLoader().Load(path);
        Assert.Equal(325, particles.Count);
        Assert.Equal("Fountain", particles[0].Name);
        Assert.Equal(20, particles[0].NumOfParticles);
        Assert.Equal(4, particles[0].NumGrhs);
        Assert.Equal(4, particles[0].Grhs.Length);
        Assert.All(particles, particle => Assert.False(string.IsNullOrWhiteSpace(particle.Name)));
        Assert.All(particles, particle => Assert.Equal(particle.NumGrhs, particle.Grhs.Length));
        Assert.Equal("Fuego Juracik", particles[^1].Name);
        Assert.Contains(particles, particle => particle.Grhs.Length > 10);
    }

    private static string ResourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "ao-project-resources");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "ao-project-resources");
    }
}
