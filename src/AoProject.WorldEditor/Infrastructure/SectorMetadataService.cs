using System.Globalization;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>Port of MapInfo_Cargar/MapInfo_Guardar and clsIniReader semantics.</summary>
internal sealed class SectorMetadataService
{
    public void Load(WorldSector world, string path)
    {
        if (!File.Exists(path))
            return;

        var section = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
        var values = Parse(path, section);
        var info = world.Info;
        info.Name = Get(values, "Name");
        info.Music = Get(values, "MusicNum");
        info.MapVersion = ShortValue(values, "MapVersion");
        info.MagicDisabled = ByteValue(values, "MagiaSinEfecto");
        info.InvisibilityDisabled = ByteValue(values, "InviSinEfecto");
        info.ResurrectionDisabled = ByteValue(values, "ResuSinEfecto");
        info.NoEncryptMap = ByteValue(values, "NoEncriptarMP");
        info.Light = Get(values, "Light");
        info.Terrain = Get(values, "Terreno");
        info.Zone = Get(values, "Zona");
        info.Restriction = Get(values, "Restringir");
        info.Backup = ByteValue(values, "BACKUP");
        info.PlayerKilling = IntValue(values, "Pk") == 0;
    }

    public void Save(WorldSector world, string path)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var section = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
        var info = world.Info;
        var lines = new List<string>
        {
            $"[{section}]",
            $"Name={info.Name}",
            $"MusicNum={info.Music}",
            $"MapVersion={info.MapVersion}",
            $"MagiaSinefecto={info.MagicDisabled}",
            $"InviSinEfecto={info.InvisibilityDisabled}",
            $"ResuSinEfecto={info.ResurrectionDisabled}",
            $"NoEncriptarMP={info.NoEncryptMap}",
            $"Light={info.Light}",
            $"Terreno={info.Terrain}",
            $"Zona={info.Zone}",
            $"Restringir={info.Restriction}",
            $"BackUp={info.Backup}",
            $"Pk={(info.PlayerKilling ? 0 : 1)}"
        };
        File.WriteAllLines(path, lines);
    }

    private static Dictionary<string, string> Parse(string path, string section)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var active = false;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                active = string.Equals(line[1..^1].Trim(), section, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!active || line.Length == 0 || line.StartsWith(';'))
                continue;
            var separator = line.IndexOf('=');
            if (separator > 0)
                result[line[..separator].Trim()] = line[(separator + 1)..];
        }
        return result;
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : string.Empty;

    private static byte ByteValue(IReadOnlyDictionary<string, string> values, string key) =>
        byte.TryParse(Get(values, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : (byte)0;

    private static int IntValue(IReadOnlyDictionary<string, string> values, string key) =>
        int.TryParse(Get(values, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static short ShortValue(IReadOnlyDictionary<string, string> values, string key) =>
        short.TryParse(Get(values, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : (short)0;
}
