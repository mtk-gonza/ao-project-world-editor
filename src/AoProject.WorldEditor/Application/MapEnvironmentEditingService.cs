using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed class MapEnvironmentEditingService
{
    public void Apply(WorldMap map, byte rain, byte snow, byte fog, int baseLight)
    {
        ArgumentNullException.ThrowIfNull(map);
        map.Environment.Rain = rain; map.Environment.Snow = snow; map.Environment.Fog = fog; map.Environment.BaseLight = baseLight;
        map.Info.Changed = true;
    }
}
