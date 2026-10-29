using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

/// <summary>Port of clsDX8Engine.Particle_Type_Get.</summary>
internal sealed class ParticleCatalogService
{
    public int TypeForGrh(IReadOnlyList<ParticleData> particles, int grhIndex)
    {
        ArgumentNullException.ThrowIfNull(particles);
        if (grhIndex <= 0) return 0;
        for (var index = 0; index < particles.Count; index++)
            if (Array.IndexOf(particles[index].Grhs, grhIndex) >= 0) return index + 1;
        return 0;
    }
}
