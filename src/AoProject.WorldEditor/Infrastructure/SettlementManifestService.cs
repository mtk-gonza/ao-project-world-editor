using System.Text.Json;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>Converts the legacy Ciudades.Dat catalog to the runtime settlement manifest.</summary>
internal sealed class SettlementManifestService
{
    private static readonly string[] CityNames =
    [
        "Ullathorpe", "Nix", "Banderbill", "Lindos", "Arghal",
        "Arkhein", "Forgat", "Eldoria", "Penthar", "Morgrim"
    ];

    /// <summary>
    /// Convierte Ciudades.Dat al manifiesto de settlements en memoria. Devuelve
    /// vacío si el archivo no existe, de forma que el WORLD se pueda compilar sin
    /// settlements. El intermedio legacy nunca se persiste: se transforma a
    /// coordenadas globales y se embebe en el bundle.
    /// </summary>
    public byte[] Build(string ciudadesPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciudadesPath);
        if (!File.Exists(ciudadesPath)) return [];

        var document = IniDocument.Load(ciudadesPath);
        var settlements = new List<object>();
        for (var i = 0; i < CityNames.Length; i++)
        {
            var section = CityNames[i];
            var sectorId = document.GetInt(section, "MAPA");
            var x = document.GetInt(section, "X");
            var y = document.GetInt(section, "Y");
            if (sectorId <= 0 || x <= 0 || y <= 0) continue;
            var travelSector = document.GetInt(section, "MAPAVIAJE");
            var travelX = document.GetInt(section, "ViajeX");
            var travelY = document.GetInt(section, "ViajeY");
            var resurrectionSector = document.GetInt(section, "MapaResu");
            var resurrectionX = document.GetInt(section, "ResuX");
            var resurrectionY = document.GetInt(section, "ResuY");

            settlements.Add(new
            {
                homeId = i + 1,
                name = section,
                arrival = Position(sectorId, x, y),
                travel = Position(travelSector > 0 ? travelSector : sectorId,
                    travelX > 0 ? travelX : x, travelY > 0 ? travelY : y),
                resurrection = Position(resurrectionSector > 0 ? resurrectionSector : sectorId,
                    resurrectionX > 0 ? resurrectionX : x, resurrectionY > 0 ? resurrectionY : y),
                requiresBoat = document.GetInt(section, "NecesitaNave") != 0,
                associatedSectors = ParseSectorList(document.Get(section, "Mapas"))
            });
        }

        if (settlements.Count == 0)
            throw new InvalidDataException("Ciudades.Dat no contiene settlements utilizables.");

        return JsonSerializer.SerializeToUtf8Bytes(settlements, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static object Position(int sectorId, int x, int y) => new
    {
        sectorId,
        x,
        y,
        floorZ = 0
    };

    private static int[] ParseSectorList(string value) => value
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(item => int.TryParse(item, out var id) ? id : 0)
        .Where(id => id > 0)
        .ToArray();
}
