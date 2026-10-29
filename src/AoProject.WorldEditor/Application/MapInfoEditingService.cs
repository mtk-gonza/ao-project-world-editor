using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct MapInfoValues(
    string Name,
    string Music,
    short MapVersion,
    bool PlayerKilling,
    byte MagicDisabled,
    byte InvisibilityDisabled,
    byte ResurrectionDisabled,
    byte NoEncryptMap,
    string Terrain,
    string Zone,
    string Restriction,
    byte Backup,
    string Light);

internal sealed class MapInfoEditingService
{
    public void Apply(WorldMap map, MapInfoValues values)
    {
        ArgumentNullException.ThrowIfNull(map);
        map.Info.Name = values.Name.Trim();
        map.Info.Music = values.Music.Trim();
        map.Info.MapVersion = values.MapVersion;
        map.Info.PlayerKilling = values.PlayerKilling;
        map.Info.MagicDisabled = values.MagicDisabled;
        map.Info.InvisibilityDisabled = values.InvisibilityDisabled;
        map.Info.ResurrectionDisabled = values.ResurrectionDisabled;
        map.Info.NoEncryptMap = values.NoEncryptMap;
        map.Info.Terrain = values.Terrain.Trim();
        map.Info.Zone = values.Zone.Trim();
        map.Info.Restriction = values.Restriction.Trim();
        map.Info.Backup = values.Backup;
        map.Info.Light = values.Light.Trim();
        map.Info.Changed = true;
    }
}
