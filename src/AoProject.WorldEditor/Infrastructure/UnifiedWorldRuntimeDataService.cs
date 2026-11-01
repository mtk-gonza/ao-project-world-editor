using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>Writes large gameplay catalogs beside the spatial bundle.</summary>
internal sealed class UnifiedWorldRuntimeDataService
{
    private sealed record Manifest(int Schema, Dictionary<string, string> Files);

    public void Save(UnifiedWorldBundle bundle, string worldDirectory)
    {
        var directory = Path.Combine(worldDirectory, "runtime-data");
        Directory.CreateDirectory(directory);
        var payloads = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["events.json"] = NormalizeEvents(bundle.Events),
            ["treasures.json"] = NormalizeTreasures(bundle.Treasures),
            ["quests.json"] = NormalizeQuests(bundle.Quests),
            ["npcs.dat"] = bundle.Npcs,
            ["objects.dat"] = bundle.Objects,
            ["castles.json"] = NormalizeCastles(bundle.Castles)
        };
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var payload in payloads)
        {
            if (payload.Value.Length == 0)
                throw new InvalidDataException($"El WORLD no contiene el payload requerido: {payload.Key}.");
            var path = Path.Combine(directory, payload.Key);
            File.WriteAllBytes(path, payload.Value);
            hashes[payload.Key] = Convert.ToHexString(SHA256.HashData(payload.Value));
        }
        var manifest = new Manifest(1, hashes);
        File.WriteAllText(Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static byte[] NormalizeCastles(byte[] payload)
    {
        if (payload.Length == 0)
            return "{\"schema\":1,\"catalog\":\"castles\",\"items\":[]}"u8.ToArray();

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            HasProperty(root, "schema") && HasProperty(root, "items"))
            return payload;
        if (root.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Castles.json debe contener una lista JSON de castillos.");

        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            Schema = 1,
            Catalog = "castles",
            Items = root
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static byte[] NormalizeTreasures(byte[] payload)
    {
        if (payload.Length == 0)
            return JsonSerializer.SerializeToUtf8Bytes(new { Schema = 1, Catalog = "treasures", TreasureSectors = Array.Empty<int>(), Treasures = Array.Empty<object>(), GiftSectors = Array.Empty<int>(), Gifts = Array.Empty<object>(), NpcNumbers = Array.Empty<short>(), NpcSectors = Array.Empty<int>() });
        if (Encoding.UTF8.GetString(payload).TrimStart().StartsWith('{'))
            return payload;

        var sections = ParseIni(payload);
        var treasureSectors = ReadSectors(sections, "Tesoros");
        var giftSectors = ReadSectors(sections, "Regalos");
        var treasures = ReadRewards(sections, "Tesoros", "TiposDeTesoros", "Tesoro");
        var gifts = ReadRewards(sections, "Regalos", "TiposDeRegalos", "Regalo");
        var creature = GetSection(sections, "Criatura");
        var npcCount = ParseInt(Get(creature, "NPCs"));
        var npcNumbers = Enumerable.Range(1, Math.Max(0, npcCount))
            .Select(index => ParseInt(Get(creature, "NPC" + index))).Where(value => value > 0).Select(value => (short)value).ToArray();
        var npcSectors = ReadSectors(sections, "Criatura");
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            Schema = 1, Catalog = "treasures", TreasureSectors = treasureSectors,
            Treasures = treasures, GiftSectors = giftSectors, Gifts = gifts,
            NpcNumbers = npcNumbers, NpcSectors = npcSectors
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static byte[] NormalizeEvents(byte[] payload)
    {
        if (payload.Length == 0)
            return JsonSerializer.SerializeToUtf8Bytes(
                new { Schema = 1, Catalog = "events", Invasions = Array.Empty<object>(), Schedule = Array.Empty<object>() });
        if (Encoding.UTF8.GetString(payload).TrimStart().StartsWith('{'))
            return payload;

        var sections = ParseIni(payload);
        var invasions = GetSection(sections, "Invasiones");
        var count = Math.Max(0, ParseInt(Get(invasions, "Cantidad")));
        var items = Enumerable.Range(1, count).Select(index =>
        {
            var name = Get(invasions, "Invasion" + index).Trim();
            if (name.Length == 0) return null;
            var section = GetSection(sections, name);
            var maxNpcs = (short)ParseInt(Get(section, "MaxNPCs"));
            var boxCount = Math.Max(0, ParseInt(Get(section, "SpawnBoxes")));
            var boxes = Enumerable.Range(1, boxCount).Select(boxIndex =>
                {
                    var fields = Get(section, "SpawnBox" + boxIndex).Split('-');
                    if (fields.Length != 7) return null;
                    var heading = HeadingName(fields[5]);
                    if (heading is null) return null;
                    var sectorId = (short)ParseInt(fields[0]);
                    if (sectorId <= 0) return null;
                    return new
                    {
                        SectorId = sectorId,
                        TopLeftX = (short)ParseInt(fields[1]),
                        TopLeftY = (short)ParseInt(fields[2]),
                        BottomRightX = (short)ParseInt(fields[3]),
                        BottomRightY = (short)ParseInt(fields[4]),
                        Heading = heading,
                        WallCoordinate = (short)ParseInt(fields[6])
                    };
                }).Where(box => box is not null).Select(box => box!).ToArray();
            if (maxNpcs <= 0 || boxes.Length == 0) return null;

            var npcNumbers = Get(section, "NPCs")
                .Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => (short)ParseInt(value))
                .Where(number => number > 0).ToArray();

            return new
            {
                Index = index,
                Name = name,
                MaxWallHp = ParseInt(Get(section, "MaxVidaMuralla")),
                MaxNpcs = maxNpcs,
                Warning = Get(section, "Aviso"),
                WarningMinutes = (short)ParseInt(Get(section, "AvisarTiempo")),
                RepeatWarning = (short)ParseInt(Get(section, "RepetirAviso")),
                Description = Get(section, "Desc"),
                RepeatDescription = (short)ParseInt(Get(section, "RepetirDesc")),
                SpawnIntervalSeconds = (short)ParseInt(Get(section, "IntervaloSpawn")),
                DurationMinutes = (short)ParseInt(Get(section, "Duracion")),
                IntervalMinutes = (short)ParseInt(Get(section, "Intervalo")),
                OffsetMinutes = (short)ParseInt(Get(section, "Offset")),
                WonMessage = Get(section, "MensajeGanaron"),
                LostMessage = Get(section, "MensajePerdieron"),
                NpcNumbers = npcNumbers,
                SpawnBoxes = boxes
            };
        }).Where(invasion => invasion is not null).Select(invasion => invasion!).ToArray();

        var scheduleSection = GetSection(sections, "EVENTOS");
        var schedule = Enumerable.Range(0, 24).Select(hour =>
        {
            var raw = Get(scheduleSection, hour.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (raw.Length == 0) return null;
            var fields = raw.Split('-');
            int Field(int position) => position < fields.Length ? ParseInt(fields[position]) : 0;
            return new
            {
                Hour = (byte)hour,
                Type = (byte)Math.Clamp(Field(0), 0, byte.MaxValue),
                Duration = (byte)Math.Clamp(Field(1), 0, byte.MaxValue),
                Multiplier = (byte)Math.Clamp(Field(2), 0, byte.MaxValue)
            };
        }).Where(entry => entry is not null).Select(entry => entry!).ToArray();

        return JsonSerializer.SerializeToUtf8Bytes(
            new { Schema = 1, Catalog = "events", Invasions = items, Schedule = schedule },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static string? HeadingName(string value) => value.Trim().ToLowerInvariant() switch
    {
        "norte" => "NORTH",
        "sur" => "SOUTH",
        "este" => "EAST",
        "oeste" => "WEST",
        _ => null
    };

    private static byte[] NormalizeQuests(byte[] payload)
    {
        if (payload.Length == 0)
            return JsonSerializer.SerializeToUtf8Bytes(
                new { Schema = 1, Catalog = "quests", Items = Array.Empty<object>() });
        if (Encoding.UTF8.GetString(payload).TrimStart().StartsWith('{'))
            return payload;

        var sections = ParseIni(payload);
        var init = GetSection(sections, "INIT");
        var count = Math.Max(0, ParseInt(Get(init, "NumQuests")));
        var items = Enumerable.Range(1, count).Select(index =>
        {
            var section = GetSection(sections, "QUEST" + index);
            return new
            {
                Index = index,
                Name = Get(section, "Nombre").Trim(),
                Description = Get(section, "Desc"),
                RequiredLevel = ClampByte(ParseInt(Get(section, "RequiredLevel"))),
                RequiredNpcs = ReadRequirements(section, "RequiredNPCs", "RequiredNPC"),
                RequiredObjects = ReadRequirements(section, "RequiredOBJs", "RequiredOBJ"),
                RewardGold = ParseInt(Get(section, "RewardGLD")),
                RewardExperience = ParseInt(Get(section, "RewardEXP")),
                RewardObjects = ReadRequirements(section, "RewardOBJs", "RewardOBJ"),
                RequiredQuest = (short)ParseInt(Get(section, "RequiredQuest")),
                LimitLevel = ClampByte(ParseInt(Get(section, "LimitLevel"))),
                FinalDescription = Get(section, "DescFinal"),
                NextQuest = Get(section, "NextQuest"),
                RequiredClasses = ReadShorts(section, "RequiredClassesCount", "RequiredClass", byte.MaxValue)
                    .Where(value => value > 0).Select(value => (int)value).ToArray(),
                RequiredSpells = ReadShorts(section, "RequiredSpellCount", "RequiredSpell", 100)
                    .Where(value => value > 0).ToArray(),
                RequiredSkill = ClampByte(ParseInt(Get(section, "RequiredSkill"))),
                RequiredSkillValue = ClampByte(ParseInt(Get(section, "RequiredValue"))),
                RewardSpells = ReadShorts(section, "RewardSkills", "RewardSkill", 100)
                    .Where(value => value > 0).ToArray(),
                WorkerOnly = ParseInt(Get(section, "Trabajador")) == 1,
                TalkTo = (short)ParseInt(Get(section, "TalkTo")),
                Repeatable = ParseInt(Get(section, "Repetible")) != 0,
                PermittedFactions = ParseInt(Get(section, "PermittedFactions")),
                RequiredTargetNpcs = ReadShorts(section, "RequiredTargetNPCs", "RequiredTargetNPC", 100)
                    .Where(value => value > 0)
                    .Select(value => new { Index = value, Amount = (short)1 }).ToArray(),
                GlobalQuestIndex = ParseInt(Get(section, "GlobalQuestIndex")),
                GlobalQuestThresholdNeeded = ParseInt(Get(section, "GlobalQuestThresholdNeeded"))
            };
        }).Where(quest => quest.Name.Length > 0).ToArray();

        return JsonSerializer.SerializeToUtf8Bytes(
            new { Schema = 1, Catalog = "quests", Items = items },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static object[] ReadRequirements(Dictionary<string, string> section, string countKey, string prefix)
    {
        var count = Math.Max(0, ParseInt(Get(section, countKey)));
        return Enumerable.Range(1, count)
            .Select(index => Get(section, prefix + index).Split('-', 2))
            .Where(parts => parts.Length == 2)
            .Select(parts => new { Index = (short)ParseInt(parts[0]), Amount = (short)ParseInt(parts[1]) })
            .Where(requirement => requirement.Index > 0).ToArray();
    }

    private static long[] ReadShorts(Dictionary<string, string> section, string countKey, string prefix, int max)
    {
        var count = Math.Clamp(ParseInt(Get(section, countKey)), 0, max);
        return Enumerable.Range(1, count).Select(index => (long)ParseInt(Get(section, prefix + index))).ToArray();
    }

    private static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, byte.MaxValue);

    private static Dictionary<string, Dictionary<string, string>> ParseIni(byte[] payload)    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        foreach (var raw in Encoding.UTF8.GetString(payload).Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                result[line[1..^1].Trim()] = current;
                continue;
            }
            var separator = line.IndexOf('=');
            if (separator > 0 && current is not null)
                current[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return result;
    }

    private static int[] ReadSectors(Dictionary<string, Dictionary<string, string>> sections, string section)
    {
        var values = GetSection(sections, section);
        var count = Math.Max(0, ParseInt(Get(values, "CantidadMapas")));
        return Enumerable.Range(1, count).Select(index => ParseInt(Get(values, "Mapa" + index))).Where(value => value > 0).ToArray();
    }

    private static object[] ReadRewards(Dictionary<string, Dictionary<string, string>> sections, string section, string countKey, string prefix)
    {
        var values = GetSection(sections, section);
        var count = Math.Max(0, ParseInt(Get(values, countKey)));
        return Enumerable.Range(1, count).Select(index => Get(values, prefix + index).Split('-', 2))
            .Where(parts => parts.Length == 2).Select(parts => new { ObjectIndex = ParseInt(parts[0]), Amount = ParseInt(parts[1]) })
            .Where(reward => reward.ObjectIndex > 0).ToArray();
    }

    private static Dictionary<string, string> GetSection(Dictionary<string, Dictionary<string, string>> sections, string name) =>
        sections.GetValueOrDefault(name) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static string Get(Dictionary<string, string> section, string key) => section.GetValueOrDefault(key) ?? "";

    private static int ParseInt(string value)
    {
        var text = value.Trim();
        var sign = text.StartsWith('-') ? -1 : 1;
        var start = text.StartsWith('+') || text.StartsWith('-') ? 1 : 0;
        var end = start;
        while (end < text.Length && char.IsDigit(text[end])) end++;
        return end == start || !int.TryParse(text[start..end], out var number) ? 0 : sign * number;
    }

    private static bool HasProperty(JsonElement element, string name) =>
        element.EnumerateObject().Any(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
