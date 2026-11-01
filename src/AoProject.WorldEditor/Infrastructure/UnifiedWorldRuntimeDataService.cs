using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>Writes large gameplay catalogs beside the spatial bundle.</summary>
internal sealed class UnifiedWorldRuntimeDataService
{
    static UnifiedWorldRuntimeDataService()
    {
        // .dat legacy usa ANSI/CP1252; .NET Core no registra codepages por defecto.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Igual que <c>IniFile.DetectEncoding</c> del servidor: UTF-8 estricto y,
    /// si el archivo no es UTF-8 valido, CP1252 (ANSI de VB6).
    /// </summary>
    private static string DecodeLegacyText(byte[] payload)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(payload);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(payload);
        }
    }

    private sealed record Manifest(int Schema, Dictionary<string, string> Files);

    public void Save(UnifiedWorldBundle bundle, string worldDirectory)
    {
        var directory = Path.Combine(worldDirectory, "runtime-data");
        Directory.CreateDirectory(directory);
        // Referencias legacy (SectorId/coordenadas) se resuelven al contrato del
        // WORLD al mismo tiempo que se conservan 1:1 los campos originales.
        var spatial = new WorldSpatialResolver(bundle);
        var payloads = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["events.json"] = NormalizeEvents(bundle.Events),
            ["treasures.json"] = NormalizeTreasures(bundle.Treasures, spatial),
            ["quests.json"] = NormalizeQuests(bundle.Quests, spatial),
            ["npcs.json"] = NormalizeNpcs(bundle.Npcs, spatial),
            ["objects.json"] = NormalizeObjects(bundle.Objects, spatial),
            ["castles.json"] = NormalizeCastles(bundle.Castles),
            ["spells.json"] = NormalizeSpells(bundle.Spells),
            ["effectsovertime.json"] = NormalizeEffectsOverTime(bundle.EffectsOverTime),
            ["globalquests.json"] = NormalizeGlobalQuests(bundle.GlobalQuests),
            ["scenarios.json"] = NormalizeScenarios(bundle.Scenarios)
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

    private static byte[] NormalizeTreasures(byte[] payload, WorldSpatialResolver? spatial)
    {
        if (payload.Length == 0)
            return JsonSerializer.SerializeToUtf8Bytes(new { Schema = 1, Catalog = "treasures", TreasureSectors = Array.Empty<int>(), TreasureAreas = Array.Empty<object>(), Treasures = Array.Empty<object>(), GiftSectors = Array.Empty<int>(), GiftAreas = Array.Empty<object>(), Gifts = Array.Empty<object>(), NpcNumbers = Array.Empty<short>(), NpcSectors = Array.Empty<int>(), NpcAreas = Array.Empty<object>() });
        if (DecodeLegacyText(payload).TrimStart().StartsWith('{'))
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
            TreasureAreas = ResolveAreas(spatial, treasureSectors),
            Treasures = treasures, GiftSectors = giftSectors,
            GiftAreas = ResolveAreas(spatial, giftSectors),
            Gifts = gifts,
            NpcNumbers = npcNumbers, NpcSectors = npcSectors,
            NpcAreas = ResolveAreas(spatial, npcSectors)
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Maps legacy a areas globales; omite los que el WORLD no conoce.</summary>
    private static object[] ResolveAreas(WorldSpatialResolver? spatial, IReadOnlyList<int> sectors)
    {
        if (spatial is null || sectors.Count == 0) return [];
        var areas = new List<object>(sectors.Count);
        foreach (var sector in sectors)
        {
            if (sector is <= 0 or > short.MaxValue) continue;
            if (spatial.TryResolveArea((short)sector, out var area))
                areas.Add(new { area.SectorId, area.FloorZ, area.MinX, area.MinY, area.MaxX, area.MaxY });
        }
        return areas.ToArray();
    }

    /// <summary>Resuelve un tile legacy al contrato global; null si no aplica.</summary>
    private static SpatialPoint? ResolvePoint(WorldSpatialResolver? spatial, int map, int x, int y)
    {
        if (spatial is null || map is <= 0 or > short.MaxValue || x <= 0 || y <= 0) return null;
        return spatial.TryResolve((short)map, x, y, out var point) ? point : null;
    }

    /// <summary>Piso global de un mapa legacy; null si el WORLD no lo conoce.</summary>
    private static int? ResolveFloor(WorldSpatialResolver? spatial, int map)
    {
        if (spatial is null || map is <= 0 or > short.MaxValue) return null;
        return spatial.Contains((short)map) && spatial.TryGetFloorZ((short)map, out var floorZ)
            ? floorZ
            : null;
    }

    private static byte[] NormalizeEvents(byte[] payload)
    {
        if (payload.Length == 0)
            return JsonSerializer.SerializeToUtf8Bytes(
                new { Schema = 1, Catalog = "events", Invasions = Array.Empty<object>(), Schedule = Array.Empty<object>() });
        if (DecodeLegacyText(payload).TrimStart().StartsWith('{'))
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

    private static byte[] NormalizeEffectsOverTime(byte[] payload)
    {
        if (payload.Length == 0)
            return JsonSerializer.SerializeToUtf8Bytes(
                new { Schema = 1, Catalog = "effectsovertime", Items = Array.Empty<object>() });
        if (DecodeLegacyText(payload).TrimStart().StartsWith('{'))
            return payload;

        var sections = ParseIni(payload);
        var init = GetSection(sections, "INIT");
        var count = Math.Max(0, ParseInt(Get(init, "EffectCount")));
        var items = new List<Dictionary<string, object?>>(count);
        for (var index = 1; index <= count; index++)
        {
            var section = GetSection(sections, "EOT" + index.ToString(CultureInfo.InvariantCulture));
            items.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Index"] = index,
                ["Type"] = Val(section, "Type"),
                ["SubType"] = Val(section, "SubType", Val(section, "Subtype")),
                ["SharedTypeId"] = Val(section, "SharedTypeId"),
                ["TickPowerMin"] = ValDouble(section, "TickPowerMin"),
                ["TickPowerMax"] = ValDouble(section, "TickPowerMax"),
                ["Ticks"] = Val(section, "Ticks"),
                ["TickTime"] = Val(section, "TickTime"),
                ["TickFx"] = ValShort(section, "TickFX"),
                ["TickManaConsumption"] = Val(section, "TickManaConsumption"),
                ["TickStaminaConsumption"] = Val(section, "TickStaminaConsumption"),
                ["OnHitFx"] = ValShort(section, "OnHitFx"),
                ["OnHitWav"] = ValShort(section, "OnHitWAV"),
                ["OnRemoveFx"] = ValShort(section, "OnRemoveFx"),
                ["OnRemoveWav"] = ValShort(section, "OnRemoveWav"),
                ["Override"] = Val(section, "Override") != 0,
                ["Limit"] = Val(section, "Limit"),
                ["PhysicalDamageReduction"] = ValDouble(section, "PhysicalDamageReduction"),
                ["MagicDamageReduction"] = ValDouble(section, "MagicDamageReduction"),
                ["PhysicalDamageDone"] = ValDouble(section, "PhysicalDamageDone"),
                ["MagicDamageDone"] = ValDouble(section, "MagicDamageDone"),
                ["SpeedModifier"] = ValDouble(section, "SpeedModifier"),
                ["HitModifier"] = Val(section, "HitModifier"),
                ["EvasionModifier"] = Val(section, "EvasionModifier"),
                ["SelfHealingBonus"] = ValDouble(section, "SelfHealingBonus"),
                ["MagicHealingBonus"] = ValDouble(section, "MagicHealingBonus"),
                ["DefenseBonus"] = ValDouble(section, "DefenseBonus"),
                ["PhysicalLinearBonus"] = ValDouble(section, "PhysicalLinearBonus"),
                ["PhysicalBonusPveOnly"] = Val(section, "PhysicalBonusPveOnly") != 0,
                ["BuffType"] = Val(section, "BuffType"),
                ["Area"] = Val(section, "Area"),
                ["Aura"] = Get(section, "Aura"),
                ["ApplyEffectId"] = Val(section, "ApplyeffectID"),
                ["ClientEffectTypeId"] = Val(section, "ClientEffectTypeId"),
                ["SecondaryEffectId"] = Val(section, "SecondaryEffectId"),
                ["SecondaryTargetModifier"] = ValDouble(section, "SecondaryTargetModifier"),
                ["RequireTransform"] = Val(section, "RequireTransform"),
                ["RequireArmor"] = Val(section, "RequireArmor") != 0,
                ["RequireShield"] = Val(section, "RequireShield") != 0,
                ["RequireWeapon"] = Val(section, "RequireWeapon") != 0,
                ["AffectedByMagicBonus"] = Val(section, "AffectedByMagicBonus") != 0,
                ["AffectedByMagicReduction"] = Val(section, "AffectedByMagicReduction") != 0,
                ["AffectedByMagicHealing"] = Val(section, "AffectedByMagicHealing") != 0,
                ["AffectedBySelfHealing"] = Val(section, "AffectedBySelfHealing") != 0,
                ["LimitCastOnlyOnSelf"] = Val(section, "LimitCastOnlyOnSelf") != 0,
                ["ApplyStatusMask"] = Val(section, "ApplyStatusMask"),
                ["CcImmunity"] = Val(section, "CCInmunity"),
                ["NpcId"] = Val(section, "NpcId"),
                ["Transform"] = Val(section, "Transform"),

                // Claves conservadas 1:1 aunque el runtime no las consuma todavia.
                ["AffectedByPhysicalBonus"] = Val(section, "AffectedByPhysicalBonus") != 0,
                ["AffectedByPhysicalReduction"] = Val(section, "AffectedByPhysicalReduction") != 0,
                ["AffectedBySpeedModifier"] = Val(section, "AffectedBySpeedModifier") != 0,
                ["Limits"] = Val(section, "Limits")
            });
        }

        return JsonSerializer.SerializeToUtf8Bytes(
            new { Schema = 1, Catalog = "effectsovertime", Items = items },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static byte[] NormalizeGlobalQuests(byte[] payload)
    {
        if (payload.Length == 0)
            return JsonSerializer.SerializeToUtf8Bytes(
                new { Schema = 1, Catalog = "globalquests", Count = 0, Items = Array.Empty<object>() });
        if (DecodeLegacyText(payload).TrimStart().StartsWith('{'))
            return payload;

        var sections = ParseIni(payload);
        var init = GetSection(sections, "INIT");
        var count = Math.Clamp(ParseInt(Get(init, "NumGlobalQuest")), 0, 100);
        var items = new List<Dictionary<string, object?>>(count);
        for (var index = 1; index <= count; index++)
        {
            var section = GetSection(sections, "GlobalQuest" + index.ToString(CultureInfo.InvariantCulture));
            items.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Index"] = index,
                ["Name"] = Get(section, "Name"),
                ["GatheringThreshold"] = ParseInt(Get(section, "GatheringThreshold")),
                ["GatheringInitialInstallments"] = ParseInt(Get(section, "GatheringInitialInstallments")),
                ["BossIndexes"] = ReadDashShorts(Get(section, "BossIndex")),
                // El DAT solo trae BossSpawnMap: el runtime lee BossSpawnSector
                // (queda en 0, comportamiento legacy) y el mapa se conserva como
                // provenance.
                ["BossSpawnSector"] = ParseInt(Get(section, "BossSpawnSector")),
                ["BossSpawnMap"] = ParseInt(Get(section, "BossSpawnMap")),
                ["BossSpawnTopLeftX"] = ParseInt(Get(section, "BossSpawnPositionTopLeftX")),
                ["BossSpawnTopLeftY"] = ParseInt(Get(section, "BossSpawnPositionTopLeftY")),
                ["BossSpawnBottomRightX"] = ParseInt(Get(section, "BossSpawnPositionBottomRightX")),
                ["BossSpawnBottomRightY"] = ParseInt(Get(section, "BossSpawnPositionBottomRightY")),
                ["FinishOnThresholdReach"] = ParseInt(Get(section, "FinishOnThresholdReach")) != 0,
                // Se emite el string crudo del DAT y se parsea en el loader.
                ["StartDate"] = Get(section, "StartDate"),
                ["EndDate"] = Get(section, "EndDate"),
                ["ObjectIndex"] = ParseInt(Get(section, "ObjectIndex"))
            });
        }

        return JsonSerializer.SerializeToUtf8Bytes(
            new { Schema = 1, Catalog = "globalquests", Count = count, Items = items },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static byte[] NormalizeScenarios(IReadOnlyDictionary<string, byte[]> scenarios)
    {
        // El servidor reconstruye el IniFile desde Sections y reutiliza su
        // parseo tipado; se conservan todas las claves, incluidas las que el
        // runtime todavia no consume.
        var items = scenarios
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new { Name = Path.GetFileNameWithoutExtension(pair.Key), Sections = ParseIni(pair.Value) })
            .ToArray();
        return JsonSerializer.SerializeToUtf8Bytes(
            new { Schema = 1, Catalog = "scenarios", Items = items },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static short[] ReadDashShorts(string value) => value
        .Split('-', StringSplitOptions.RemoveEmptyEntries)
        .Select(ParseInt)
        .Where(number => number > 0)
        .Select(number => (short)Math.Clamp(number, 0, short.MaxValue))
        .ToArray();

    private static byte[] NormalizeSpells(byte[] payload)
    {
        if (payload.Length == 0)
            return JsonSerializer.SerializeToUtf8Bytes(
                new { Schema = 1, Catalog = "spells", Items = Array.Empty<object>() });
        if (DecodeLegacyText(payload).TrimStart().StartsWith('{'))
            return payload;

        var sections = ParseIni(payload);
        var init = GetSection(sections, "INIT");
        var count = Math.Max(0, ParseInt(Get(init, "NumeroHechizos")));
        var items = new List<Dictionary<string, object?>>(count);
        for (var index = 1; index <= count; index++)
        {
            var section = GetSection(sections, "HECHIZO" + index.ToString(CultureInfo.InvariantCulture));
            var subeHp = Val(section, "SubeHP");
            var min = Val(section, "MinHP");
            var max = Math.Max(min, Val(section, "MaxHP", min));
            var manaMin = Val(section, "MinMana");
            var manaMax = Math.Max(manaMin, Val(section, "MaxMana", manaMin));
            var staminaMin = Val(section, "MinSta");
            var staminaMax = Math.Max(staminaMin, Val(section, "MaxSta", staminaMin));
            items.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Index"] = index,
                ["ManaCost"] = Val(section, "ManaRequerido"),
                ["StaminaCost"] = Val(section, "StaRequerido"),
                ["MinSkill"] = Val(section, "MinSkill"),
                ["TargetType"] = Val(section, "Target"),
                ["Heals"] = subeHp == 1,
                ["RestoresMana"] = Val(section, "SubeMana") != 0,
                ["RestoresStamina"] = Val(section, "SubeSta") != 0,
                ["MinEffect"] = min,
                ["MaxEffect"] = max,
                ["MinManaEffect"] = manaMin,
                ["MaxManaEffect"] = manaMax,
                ["MinStaminaEffect"] = staminaMin,
                ["MaxStaminaEffect"] = staminaMax,
                ["Revives"] = Val(section, "Revivir") != 0,
                ["DamagesUser"] = subeHp == 2,
                ["SpellType"] = Math.Clamp(Val(section, "Tipo"), 0, 255),
                ["Mimics"] = Val(section, "Mimetiza") != 0,
                ["MagicPowerNeeded"] = Val(section, "MagicPowerNeeded"),
                ["AreaRadius"] = Math.Max(0, Val(section, "AreaRadio")),
                ["CuresPoison"] = Val(section, "CuraVeneno") != 0,
                ["Poisons"] = Val(section, "Envenena") != 0,
                ["Incinerates"] = Val(section, "Incinera") != 0,
                ["Paralyzes"] = Val(section, "Paraliza") != 0,
                ["Immobilizes"] = Val(section, "Inmoviliza") != 0,
                ["RemovesParalysis"] = Val(section, "RemoverParalisis") != 0,
                ["GrantsInvisibility"] = Val(section, "Invisibilidad") != 0,
                ["CursesTarget"] = Val(section, "Maldicion") != 0,
                ["RemovesCurse"] = Val(section, "RemoverMaldicion") != 0,
                ["RemovesPartialInvisibility"] = Val(section, "RemueveInvisibilidadParcial") != 0,
                ["BlindsTarget"] = Val(section, "Ceguera") != 0,
                ["DumbsTarget"] = Val(section, "Estupidez") != 0,
                ["RemovesDumbness"] = Val(section, "RemoverEstupidez") != 0,
                ["InvokeMode"] = Math.Clamp(Val(section, "Invoca"), 0, 2),
                ["SummonNpc"] = ValShort(section, "NumNpc"),
                ["SummonCount"] = ValByte(section, "Cant"),
                ["DurationSeconds"] = Math.Max(0, Val(section, "Duration")),
                ["Particle"] = Math.Max(0, Val(section, "Particle")),
                ["AutoCast"] = Val(section, "AutoLanzar") != 0,
                ["Bindable"] = Val(section, "IsBindable") != 0,
                ["CooldownSeconds"] = Math.Max(0, Val(section, "Cooldown")),
                ["CooldownEffectId"] = Math.Max(0, Val(section, "CdEffectId")),
                ["MaxLevelCasteable"] = ValByte(section, "MaxLevelCasteable"),
                ["RequiredHp"] = Math.Max(0, Val(section, "RequiredHP")),
                ["RequireWeaponType"] = ValByte(section, "RequireWeaponType"),
                ["RequireTransform"] = Math.Max(0, Val(section, "RequireTransform")),
                ["FxGrh"] = ValShort(section, "Fxgrh"),
                ["FxLoops"] = ValShort(section, "Loops"),
                ["TravelParticle"] = ValShort(section, "ParticleViaje"),
                ["ParticleTime"] = Math.Max(0, Val(section, "TimeParticula")),
                ["Wave"] = ValShort(section, "WAV"),
                ["ScreenColor"] = Val(section, "ScreenColor"),
                ["ScreenTime"] = Val(section, "TimeEfect"),
                ["Negative"] = Val(section, "TargetEffectType") == 1,
                ["IgnoresMagicResistance"] = Val(section, "AntiRm") != 0,
                ["StaminaPercent"] = Math.Clamp(Val(section, "StaPercentRequired"), 0, 100),
                ["NeedsObject"] = ValShort(section, "NecesitaObj"),
                ["NeedsObject2"] = ValShort(section, "NecesitaObj2"),
                ["Name"] = Get(section, "Nombre"),
                ["Description"] = Get(section, "Desc"),
                ["MagicWords"] = Get(section, "PalabrasMagicas"),
                ["SpeedModifier"] = ValDouble(section, "Velocidad"),
                ["SkillType"] = Val(section, "SkillType"),
                ["MaterializeObject"] = ValShort(section, "MaterializaObj"),
                ["MaterializeCount"] = Math.Max(0, Val(section, "MaterializaCant")),
                ["Teleports"] = Val(section, "Teleport"),
                ["TeleportSector"] = ValShort(section, "TeleportSector"),
                ["TeleportMap"] = ValShort(section, "TeleportMap"),
                ["TeleportDestinationX"] = ValShort(section, "TeleportX"),
                ["TeleportDestinationY"] = ValShort(section, "TeleportY"),
                ["AreaAffects"] = Math.Clamp(Val(section, "AreaAfecta"), 0, 3),
                ["Healing"] = Val(section, "Sanacion"),
                ["Disenchants"] = Val(section, "desencantar") != 0,
                ["RemovesDebuff"] = Val(section, "RemoveDebuff") != 0,
                ["StealsBuff"] = Val(section, "StealBuff") != 0,
                ["CancelsActiveEffect"] = Val(section, "CancelActiveEffect") != 0,
                ["AdjustsStatsWithCaster"] = Val(section, "AdjustStatsWithCaster") != 0,
                ["TogglesCleave"] = Val(section, "ToggleCleave") != 0,
                ["TogglesDivineBlood"] = Val(section, "ToggleDivineBlood") != 0,
                ["PreciseHit"] = Val(section, "GolpeCertero") != 0,
                ["IsElementalTagsOnly"] = Val(section, "IsElementalTagsOnly") != 0,
                ["EotId"] = Math.Max(0, Val(section, "EOTID")),
                ["RequireArmor"] = Val(section, "RequireArmor") != 0,
                ["RequireHelmet"] = Val(section, "RequireHelm") != 0,
                ["RequireShield"] = Val(section, "RequireShield") != 0,
                ["RequireWeapon"] = Val(section, "RequireWeapon") != 0,
                ["RequireKnuckles"] = Val(section, "RequireKnucle") != 0,
                ["RequireProjectile"] = Val(section, "RequireProjectile") != 0,
                ["RequireMagicItem"] = Val(section, "RequireMagicItem") != 0,
                ["RequireShip"] = Val(section, "RequireShip") != 0,
                ["RequireTargetOnLand"] = Val(section, "RequireTargetOnLand") != 0,
                ["RequireTargetOnWater"] = Val(section, "RequireTargetOnWater") != 0,
                ["WorkOnDead"] = Val(section, "WorkOnDead") != 0,
                ["IsSkill"] = Val(section, "IsSkill") != 0,
                ["RaiseAgility"] = Val(section, "SubeAG"),
                ["MinAgility"] = Val(section, "MinAG"),
                ["MaxAgility"] = Val(section, "MaxAG"),
                ["RaiseStrength"] = Val(section, "SubeFU"),
                ["MinStrength"] = Val(section, "MinFU"),
                ["MaxStrength"] = Val(section, "MaxFU"),
                ["RaiseCharisma"] = Val(section, "SubeCA"),
                ["MinCharisma"] = Val(section, "MinCA"),
                ["MaxCharisma"] = Val(section, "MaxCA"),
                ["RaiseHunger"] = Val(section, "SubeHam"),
                ["MinHunger"] = Val(section, "MinHam"),
                ["MaxHunger"] = Val(section, "MaxHam"),
                ["RaiseThirst"] = Val(section, "SubeSed"),
                ["MinThirst"] = Val(section, "MinSed"),
                ["MaxThirst"] = Val(section, "MaxSed"),

                // Claves conservadas 1:1 aunque el runtime no las consuma todavia:
                // mensajes, icono, metadatos y localizacion.
                ["HechizeroMsg"] = Get(section, "HechizeroMsg"),
                ["TargetMsg"] = Get(section, "TargetMsg"),
                ["PropioMsg"] = Get(section, "PropioMsg"),
                ["IconoIndex"] = Val(section, "IconoIndex"),
                ["NoWiki"] = Val(section, "NoWiki") != 0,
                ["Resis"] = Val(section, "Resis"),
                ["LightOrbObject"] = Val(section, "LightOrbObject"),
                ["LightOrbCastRange"] = Val(section, "LightOrbCastRange"),
                ["CdEffectIve"] = Val(section, "CdEffectIve"),
                ["Localization"] = ReadLocalization(section, SpellLocalizationKeys)
            });
        }

        return JsonSerializer.SerializeToUtf8Bytes(
            new { Schema = 1, Catalog = "spells", Items = items },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static byte[] NormalizeNpcs(byte[] payload, WorldSpatialResolver? spatial)
    {
        if (payload.Length == 0)
            return JsonSerializer.SerializeToUtf8Bytes(
                new { Schema = 1, Catalog = "npcs", Items = Array.Empty<object>() });
        if (DecodeLegacyText(payload).TrimStart().StartsWith('{'))
            return payload;

        var sections = ParseIni(payload);
        var init = GetSection(sections, "INIT");
        var count = Math.Max(0, ParseInt(Get(init, "NumNPCs")));
        var items = Enumerable.Range(1, count).Select(number =>
        {
            var section = GetSection(sections, "NPC" + number);
            var name = Get(section, "Name");
            if (name.Length == 0) return null;

            var spellCount = Math.Clamp(Int(section, "LanzaSpells"), 0, 32);
            var animationCount = Math.Clamp(Int(section, "Animaciones"), 0, 32);
            var targetMap = Short(section, "MapTargetEntry");
            var targetX = Byte(section, "MapTargetEntryX", 50);
            var targetY = Byte(section, "MapTargetEntryY", 50);

            return new
            {
                Number = number,
                Name = name,
                NpcType = Byte(section, "NpcType"),
                Body = Short(section, "Body"),
                Head = Short(section, "Head"),
                Heading = Byte(section, "Heading", 3),
                Movement = Byte(section, "Movement"),
                Attackable = Int(section, "Attackable") != 0,
                QuestNumber = Short(section, "QuestNumber"),
                Faction = Byte(section, "Faccion"),
                Hostile = Int(section, "Hostile") != 0,
                Comercia = Int(section, "Comercia") != 0,
                MinHp = Int(section, "MinHP"),
                MaxHp = Int(section, "MaxHP"),
                MinHit = Int(section, "MinHIT"),
                MaxHit = Int(section, "MaxHIT"),
                GiveExp = Int(section, "GiveEXP"),
                GiveGold = Int(section, "GiveGLD"),
                ShopItems = ReadShopItems(section),
                Drops = ReadDrops(section),
                Inventory = ReadInventory(section),
                Description = Get(section, "Desc"),
                MinMana = Math.Max(1, Int(section, "MinMana", 1)),
                MaxMana = Math.Max(1, Int(section, "MaxMana", Int(section, "MinMana", 1))),
                TrainerCreatures = ReadTrainerCreatures(section),
                Evasion = Int(section, "PoderEvasion"),
                AttackPower = Int(section, "PoderAtaque"),
                Defense = Int(section, "DEF"),
                Poisonous = Int(section, "Veneno") != 0,
                ItemType = Int(section, "TipoItems"),
                Respawns = Int(section, "ReSpawn") == 0,
                InventoryRespawns = Int(section, "InvReSpawn") == 0,
                IntervaloMovimiento = NormalizeInterval(Int(section, "IntervaloMovimiento"), 380),
                IntervaloAtaque = NormalizeInterval(Int(section, "IntervaloAtaque"), 2000),
                IntervaloLanzarHechizo = NormalizeInterval(Int(section, "IntervaloLanzarHechizo"), 8000),
                IntervaloRespawnMin = NormalizeInterval(Int(section, "IntervaloRespawnMin"), 0),
                IntervaloRespawnMax = NormalizeInterval(Int(section, "IntervaloRespawn"), 0),
                RespawnSchedule = (byte)Math.Clamp(Int(section, "RespawnSchedule"), 0, 2),
                InformarRespawn = Int(section, "InformarRespawn") != 0,
                SndRespawn = Short(section, "SndRespawn"),
                SubName = Get(section, "SubName"),
                WalkPath = ReadWalkPath(section, Int(section, "CaminataLen")),
                OrigPos = Get(section, "OrigPos"),
                Nivel = Int(section, "Nivel"),
                Exp = Int(section, "Exp"),
                NroExp = Int(section, "NROEXP"),
                Expressions = ReadStrings(section, "Exp", Int(section, "NROEXP")),
                GiveExpClan = Int(section, "GiveEXPClan"),
                DropCount = Int(section, "DropCount"),
                QuestDrops = ReadQuestDrops(section, Int(section, "NumDropQuest")),
                QuestNumbers = ReadShorts(section, "QuestNumber", Int(section, "NumQuest")),
                Destinations = ReadStrings(section, "Dest", Int(section, "NumDestinos")),
                GlobalQuestBossIndex = Int(section, "GlobalQuestBossIndex"),
                MagicBonus = Double(section, "MagicBonus"),
                MagicDef = Int(section, "MagicDef"),
                MagicResistance = Int(section, "MagicResistance"),
                ImmuneToSpells = Int(section, "ImmuneToSpells") != 0,
                ElementalTags = Int(section, "ElementalTags"),
                GolpeExacto = Int(section, "GolpeExacto"),
                AttackableByEveryone = Int(section, "AttackableByEveryone") != 0,
                AttackRange = Int(section, "AttackRange"),
                PreferedRange = Int(section, "PreferedRange"),
                RangoSpell = Int(section, "RangoSpell"),
                AfectaParalisis = Int(section, "AfectaParalisis") != 0,
                TranslationInmune = Int(section, "TranslationInmune") != 0,
                Humanoide = Int(section, "Humanoide") != 0,
                OnlyForGuilds = Int(section, "OnlyForGuilds") != 0,
                RestriccionDeAtaque = Int(section, "RestriccionDeAtaque"),
                RestriccionDeAyuda = Int(section, "RestriccionDeAyuda"),
                MinTameLevel = Int(section, "MinTameLevel", 1),
                ShowName = Int(section, "ShowName") != 0,
                ShowKillerConsole = Int(section, "ShowKillerConsole") != 0,
                Team = Int(section, "Team"),
                Alineacion = Int(section, "Alineacion"),
                CantidadInvocaciones = Int(section, "CantidadInvocaciones"),
                Cd = Int(section, "Cd"),
                Distancia = Int(section, "Distancia"),
                CaminataLen = Int(section, "CaminataLen"),
                AddToSectorAiList = Int(section, "AddToSectorAiList") != 0,
                AddToMapAiList = Int(section, "AddToMapAiList") != 0,
                BackUp = Int(section, "BackUp"),
                DisplayCastMessage = Int(section, "DisplayCastMessage") != 0,
                LavaValida = Int(section, "LavaValida") != 0,
                DontHitVisiblePlayers = Int(section, "DontHitVisiblePlayers") != 0,
                Snd1 = Short(section, "Snd1"),
                Snd2 = Short(section, "Snd2"),
                Snd3 = Short(section, "Snd3"),
                SoundOpen = Short(section, "SoundOpen"),
                SoundClose = Short(section, "SoundClose"),
                Arma = Short(section, "Arma"),
                Cart = Short(section, "Cart"),
                Casco = Short(section, "Casco"),
                Escudo = Short(section, "Escudo"),
                DefM = Short(section, "DEFm"),
                Dest = Short(section, "Dest"),
                NumDestinos = Int(section, "NumDestinos"),
                ProjectileType = Short(section, "ProjectileType"),
                GobernadorDe = Int(section, "GobernadorDe"),
                CollectibleCardIndex = Int(section, "CollectibleCardIndex"),
                Anim = Int(section, "Anim"),
                Animaciones = animationCount,
                AnimationIds = ReadShorts(section, "Anim", animationCount),
                CastAnimation = Int(section, "CastAnimation"),
                Interface = Int(section, "Interface"),
                TestOnly = Int(section, "TESTONLY") != 0,
                DisabledInBattleServer = Int(section, "DISABLEDINBATTLESERVER") != 0,
                OnlyEnabledInBattleServer = Int(section, "ONLYENABLEDINBATTLESERVER") != 0,
                RequireToggle = Int(section, "REQUIRETOGGLE") != 0,
                SpellCastPower = Byte(section, "LanzaSpells"),
                Spells = ReadShorts(section, "Sp", spellCount),
                SpellCooldowns = Enumerable.Range(1, spellCount)
                    .Select(index => Math.Max(0, Int(section, "Cd" + index))).ToArray(),
                WaterValid = Int(section, "AguaValida") != 0,
                Craftea = Byte(section, "Craftea"),
                Domable = Int(section, "Domable"),
                LandInvalid = Int(section, "TierraInvalida") != 0,
                PuedeInvocar = Int(section, "PuedeInvocar") != 0,
                MapEntryPrice = Int(section, "MapEntryPrice"),
                SectorTargetEntry = Short(section, "MapTargetEntry", 1),
                SectorTargetEntryX = Byte(section, "MapTargetEntryX", 50),
                SectorTargetEntryY = Byte(section, "MapTargetEntryY", 50),
                ArenaEnabled = Int(section, "ArenaEnabled") != 0,
                TransporterLevel = Int(section, "TransporterLevel"),
                TransportCities = ReadTransportCities(section, spatial),
                TargetGlobal = targetMap > 0 ? ResolvePoint(spatial, targetMap, targetX, targetY) : null,

                // Claves conservadas 1:1 aunque el cliente sea quien las use:
                // cuerpos alternativos, metadatos y localizacion.
                Ataque1 = Short(section, "Ataque1"),
                Ataque2 = Short(section, "Ataque2"),
                AttackFreeNpc = Int(section, "AttackFreeNpc") != 0,
                BodyIdle = Short(section, "BodyIdle"),
                BodyOnLand = Short(section, "BodyOnLand"),
                BodyOnWater = Short(section, "BodyOnWater"),
                BodyOnWaterIdle = Short(section, "BodyOnWaterIdle"),
                CambioAIdle = Int(section, "CambioAIdle"),
                DescClose = Get(section, "DescClose"),
                LocaleID = Int(section, "LocaleID"),
                Miedo = Int(section, "Miedo"),
                Minimap = Int(section, "Minimap") != 0,
                NoMapInfo = Int(section, "NoMapInfo") != 0,
                NoWiki = Int(section, "NoWiki") != 0,
                NpcFaceGrh = Int(section, "NpcFaceGrh"),
                PosOrig = Int(section, "PosOrig"),
                SubeSupervivencia = Int(section, "SubeSupervivencia") != 0,
                WikiSellerType = Get(section, "WikiSellerType"),
                Localization = ReadLocalization(section, NpcLocalizationKeys)
            };
        }).Where(npc => npc is not null).Select(npc => npc!).ToArray();

        return JsonSerializer.SerializeToUtf8Bytes(
            new { Schema = 1, Catalog = "npcs", Items = items },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static object[] ReadShopItems(Dictionary<string, string> section) =>
        Enumerable.Range(1, Math.Clamp(Int(section, "NROITEMS"), 0, 100))
            .Select(index => Get(section, "Obj" + index).Split('-', 2))
            .Select(parts => new
            {
                ObjectIndex = parts.Length > 0 ? (short)ParseInt(parts[0]) : (short)0,
                Amount = parts.Length > 1 ? (short)Math.Clamp(ParseInt(parts[1]), 0, 10000) : (short)0
            })
            .Where(item => item.ObjectIndex > 0).ToArray();

    private static object[] ReadDrops(Dictionary<string, string> section)
    {
        var count = Int(section, "DropCount");
        if (count <= 0) count = 5;
        return Enumerable.Range(1, Math.Clamp(count, 1, 50))
            .Select(index => Get(section, "Drop" + index).Split('-'))
            .Select(parts => new
            {
                ObjectIndex = parts.Length > 0 ? (short)ParseInt(parts[0]) : (short)0,
                Amount = (short)0,
                Chance = parts.Length > 1 ? ParseInt(parts[1]) : 0,
                MinAmount = parts.Length > 2 ? (short)ParseInt(parts[2]) : (short)0,
                MaxAmount = parts.Length > 3 ? (short)ParseInt(parts[3]) : (short)0
            })
            .Where(drop => drop.ObjectIndex > 0 && drop.Chance > 0).ToArray();
    }

    private static object[] ReadInventory(Dictionary<string, string> section) =>
        Enumerable.Range(1, Math.Clamp(Int(section, "NROITEMS"), 0, 50))
            .Select(index => Get(section, "Obj" + index).Split('-', 2))
            .Select(parts => new
            {
                ObjectIndex = parts.Length > 0 ? (short)ParseInt(parts[0]) : (short)0,
                Amount = parts.Length > 1 ? (short)ParseInt(parts[1]) : (short)0
            })
            .Where(item => item.ObjectIndex > 0 && item.Amount > 0).ToArray();

    private static object[] ReadTrainerCreatures(Dictionary<string, string> section) =>
        Enumerable.Range(1, Math.Clamp(Int(section, "NroCriaturas"), 0, 50))
            .Select(index => new { NpcIndex = Short(section, "CI" + index), Name = Get(section, "CN" + index) })
            .Where(creature => creature.NpcIndex > 0 && creature.Name.Length > 0).ToArray();

    private static object[] ReadTransportCities(Dictionary<string, string> section, WorldSpatialResolver? spatial)
    {
        var cities = new List<object>();
        for (var index = 1; index <= Math.Clamp(Int(section, "CityCount"), 0, 32); index++)
        {
            var parts = Get(section, "City" + index).Split('-');
            if (parts.Length < 5) continue;
            var sectorId = (short)ParseInt(parts[1]);
            var x = (byte)Math.Clamp(ParseInt(parts[2]), 0, byte.MaxValue);
            var y = (byte)Math.Clamp(ParseInt(parts[3]), 0, byte.MaxValue);
            cities.Add(new
            {
                SectorId = sectorId,
                X = x,
                Y = y,
                Price = ParseInt(parts[4]),
                Name = parts[0],
                Global = ResolvePoint(spatial, sectorId, x, y)
            });
        }
        return cities.ToArray();
    }

    private static object[] ReadWalkPath(Dictionary<string, string> section, int count)
    {
        if (count <= 0) return [];
        return Enumerable.Range(1, count).Select(index =>
        {
            var fields = Get(section, "Caminata" + index).Split(':');
            return new
            {
                OffsetX = fields.Length > 0 ? ParseInt(fields[0]) : 0,
                OffsetY = fields.Length > 1 ? ParseInt(fields[1]) : 0,
                Wait = fields.Length > 2 ? ParseInt(fields[2]) : 0
            };
        }).ToArray();
    }

    private static object[] ReadQuestDrops(Dictionary<string, string> section, int count)
    {
        if (count <= 0) return [];
        return Enumerable.Range(1, count).Select(index =>
        {
            var fields = Get(section, "DropQuest" + index).Split('-');
            return new
            {
                QuestIndex = fields.Length > 0 ? ParseInt(fields[0]) : 0,
                ObjectIndex = (short)(fields.Length > 1 ? ParseInt(fields[1]) : 0),
                Amount = (short)(fields.Length > 2 ? ParseInt(fields[2]) : 0),
                Probability = fields.Length > 3 ? ParseInt(fields[3]) : 0
            };
        }).ToArray();
    }

    private static string[] ReadStrings(Dictionary<string, string> section, string prefix, int count)
    {
        if (count <= 0) return [];
        return Enumerable.Range(1, count).Select(index => Get(section, prefix + index)).ToArray();
    }

    private static short[] ReadShorts(Dictionary<string, string> section, string prefix, int count)
    {
        if (count <= 0) return [];
        return Enumerable.Range(1, count).Select(index => Short(section, prefix + index)).ToArray();
    }

    private static int Int(Dictionary<string, string> section, string key, int fallback = 0)
    {
        var raw = Get(section, key);
        return raw.Length == 0 ? fallback : ParseInt(raw);
    }

    private static short Short(Dictionary<string, string> section, string key, short fallback = 0) =>
        (short)Math.Clamp(Int(section, key, fallback), short.MinValue, short.MaxValue);

    private static byte Byte(Dictionary<string, string> section, string key, byte fallback = 0) =>
        (byte)Math.Clamp(Int(section, key, fallback), 0, byte.MaxValue);

    private static double Double(Dictionary<string, string> section, string key) =>
        double.TryParse(Get(section, key), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;

    /// <summary>Igual que <c>Val()</c> de VB6/SpellStore: prefijo numerico aun con comentarios.</summary>
    private static int Val(Dictionary<string, string> section, string key, int fallback = 0)
    {
        var text = Get(section, key).Trim();
        var length = 0;
        while (length < text.Length && (char.IsDigit(text[length]) || text[length] is '-' or '+')) length++;
        return length > 0 && int.TryParse(text[..length], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static double ValDouble(Dictionary<string, string> section, string key)
    {
        var text = Get(section, key).Trim();
        var length = 0;
        var separator = false;
        while (length < text.Length)
        {
            var current = text[length];
            if (char.IsDigit(current) || current is '-' or '+') { length++; continue; }
            if (current is '.' or ',' && !separator) { separator = true; length++; continue; }
            break;
        }

        return length > 0 && double.TryParse(text[..length].Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static short ValShort(Dictionary<string, string> section, string key) =>
        (short)Math.Clamp(Val(section, key), 0, short.MaxValue);

    private static byte ValByte(Dictionary<string, string> section, string key) =>
        (byte)Math.Clamp(Val(section, key), 0, byte.MaxValue);

    private static int NormalizeInterval(int value, int fallback) => value > 0 ? value : fallback;

    private static byte[] NormalizeObjects(byte[] payload, WorldSpatialResolver? spatial)
    {
        if (payload.Length == 0)
            return JsonSerializer.SerializeToUtf8Bytes(
                new { Schema = 1, Catalog = "objects", Items = Array.Empty<object>() });
        if (DecodeLegacyText(payload).TrimStart().StartsWith('{'))
            return payload;

        var sections = ParseIni(payload);
        var init = GetSection(sections, "INIT");
        var count = Math.Max(0, LegacyInt(Get(init, "NumOBJs")));
        var items = new List<Dictionary<string, object?>>(count);
        for (var index = 1; index <= count; index++)
        {
            var section = GetSection(sections, "OBJ" + index);
            var name = Get(section, "Name");
            if (name.Length == 0) continue;
            items.Add(ReadObject(section, (short)index, name, spatial));
        }

        return JsonSerializer.SerializeToUtf8Bytes(
            new { Schema = 1, Catalog = "objects", Items = items },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static Dictionary<string, object?> ReadObject(
        Dictionary<string, string> section, short index, string name, WorldSpatialResolver? spatial)
    {
        var objectType = ObjInt(section, "ObjType");
        // FileIO.bas:1448-1455: pasajes, donador y quest guardan el destino en
        // SectorId/X/Y; el resto de objetos de transporte usa HastaMap/HastaX/HastaY.
        var destinationInMap = objectType is 36 or 50 or 51; // otPassageTicket, otDonator, OtQuest
        var skill = ParseSkillRequirement(Get(section, "SkillRequerido"));
        var minLevel = ObjInt(section, "MinELV");
        if (minLevel <= 0) minLevel = ObjInt(section, "MinLevel");
        var cooldown = ObjInt(section, "Cooldown");
        if (cooldown <= 0) cooldown = ObjInt(section, "CD");
        var spellIndex = ObjShort(section, "HechizoIndex");
        if (spellIndex == 0) spellIndex = ObjShort(section, "Hechizo");
        var skSastreria = ObjShort(section, "SKSastreria");
        if (skSastreria == 0) skSastreria = ObjShort(section, "SkSastreria");
        var map = (short)Math.Clamp(FirstNonZero(section, "Map", "SectorId"), short.MinValue, short.MaxValue);
        var desdeMap = ObjShort(section, "DesdeMap");
        var hastaMap = (short)Math.Clamp(
            destinationInMap ? FirstNonZero(section, "Map", "SectorId") : ObjInt(section, "HastaMap"),
            short.MinValue, short.MaxValue);
        var hastaX = (byte)Math.Clamp(
            destinationInMap ? FirstNonZero(section, "X") : ObjInt(section, "HastaX"), 0, byte.MaxValue);
        var hastaY = (byte)Math.Clamp(
            destinationInMap ? FirstNonZero(section, "Y") : ObjInt(section, "HastaY"), 0, byte.MaxValue);

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Index"] = index,
            ["Name"] = name,
            ["Graphic"] = ObjInt(section, "GrhIndex"),
            ["Type"] = (byte)objectType,
            ["MaxHit"] = ObjShort(section, "MaxHIT"),
            ["MinHit"] = ObjShort(section, "MinHIT"),
            ["MaxDefense"] = ObjShort(section, "MaxDef"),
            ["MinDefense"] = ObjShort(section, "MinDef"),
            ["Value"] = (float)LegacyDouble(Get(section, "Valor")),
            ["OpenIndex"] = ObjShort(section, "IndexAbierta"),
            ["ClosedIndex"] = ObjShort(section, "IndexCerrada"),
            ["MineralIndex"] = ObjShort(section, "MineralIndex"),
            ["LingoteIndex"] = ObjShort(section, "Lingoteindex"),
            ["Madera"] = ObjShort(section, "Madera"),
            ["MaderaElfica"] = ObjShort(section, "MaderaElfica"),
            ["SkHerreria"] = ObjShort(section, "SkHerreria"),
            ["SkCarpinteria"] = ObjShort(section, "SkCarpinteria"),
            ["LingH"] = ObjShort(section, "LingH"),
            ["LingP"] = ObjShort(section, "LingP"),
            ["LingO"] = ObjShort(section, "LingO"),
            ["Upgrade"] = ObjShort(section, "Upgrade"),

            ["ForumId"] = Get(section, "ID").Trim(),
            ["SignalText"] = Get(section, "Texto"),
            ["SecondaryGraphic"] = ObjInt(section, "GrhSecundario"),
            ["Ropaje"] = ObjShort(section, "NumRopaje"),
            ["MinSkill"] = ObjByte(section, "MinSkill"),
            ["MinHunger"] = ObjByte(section, "MinHam"),
            ["MinThirst"] = ObjByte(section, "MinSed"),
            ["KeyCode"] = ObjShort(section, "Clave"),
            ["KeyRequired"] = ObjInt(section, "Llave") != 0,
            ["SpellIndex"] = spellIndex,
            ["CraftingMaterials"] = ReadCraftingMaterials(section),
            ["Newbie"] = ObjInt(section, "Newbie") != 0,
            ["Invernal"] = ObjInt(section, "Invernal") != 0,
            ["Subastable"] = ObjInt(section, "Subastable") != 0,
            ["Agarrable"] = ObjInt(section, "Agarrable") != 0,
            ["NoSeCae"] = ObjInt(section, "NoSeCae") != 0,
            ["Proyectil"] = ObjInt(section, "Proyectil") != 0,
            ["UsesAmmo"] = ObjInt(section, "Municiones") != 0,
            ["AmmoType"] = ObjInt(section, "Municiones"),
            ["MinLevel"] = (byte)Math.Clamp(minLevel, 0, byte.MaxValue),
            ["PotionType"] = (byte)Math.Clamp(ObjInt(section, "TipoPocion"), 0, byte.MaxValue),
            ["MinModifier"] = ObjShort(section, "MinModificador"),
            ["MaxModifier"] = ObjShort(section, "MaxModificador"),
            ["RealFaction"] = ObjInt(section, "Real") != 0,
            ["ChaosFaction"] = ObjInt(section, "Caos") != 0,
            ["Crucial"] = ObjInt(section, "Crucial") != 0,
            ["Log"] = ObjInt(section, "Log") != 0,
            ["NoLog"] = ObjInt(section, "NoLog") != 0,
            ["ProhibitedClasses"] = Enumerable.Range(1, 12)
                .Select(position => ClassId(Get(section, "CP" + position)))
                .Where(id => id > 0).Select(id => (int)id).ToArray(),
            ["ProhibitedRaces"] = Enumerable.Range(1, 6)
                .Select(position => RaceId(Get(section, "RP" + position)))
                .Where(id => id > 0).Select(id => (int)id).ToArray(),
            ["RequiredSkill"] = skill.Skill,
            ["RequiredSkillAmount"] = skill.Amount,
            ["FemaleOnly"] = ObjInt(section, "Mujer") != 0,
            ["MaleOnly"] = ObjInt(section, "Hombre") != 0,
            ["DwarfClothing"] = ObjInt(section, "RazaEnana") != 0,
            ["DrowClothing"] = ObjInt(section, "RazaDrow") != 0,
            ["StaffPower"] = ObjInt(section, "StaffPower"),
            // FileIO.bas:1412: la clave del DAT lleva enie (Apuñala), no "Apunala".
            ["Stabs"] = ObjInt(section, "Apuñala") != 0,
            ["Slashes"] = ObjInt(section, "Acuchilla") != 0,
            ["Guante"] = ObjInt(section, "Guante") != 0,
            ["TeleportRadius"] = ObjShort(section, "Radio"),
            ["MochilaType"] = (byte)Math.Clamp(ObjInt(section, "MochilaType"), 0, 2),
            ["Anim"] = ObjShort(section, "Anim"),
            ["TwoHands"] = ObjInt(section, "DosManos") != 0,
            ["SndAura"] = ObjShort(section, "SndAura"),
            ["CreaGrh"] = Get(section, "CreaGRH"),
            ["SubType"] = (byte)Math.Clamp(ObjInt(section, "Subtipo"), 0, byte.MaxValue),
            ["WeaponType"] = (byte)Math.Clamp(ObjInt(section, "WeaponType"), 0, byte.MaxValue),
            ["RopajeHumano"] = ObjShort(section, "RopajeHumano"),
            ["RopajeHumana"] = ObjShort(section, "RopajeHumana"),
            ["RopajeElfo"] = ObjShort(section, "RopajeElfo"),
            ["RopajeElfa"] = ObjShort(section, "RopajeElfa"),
            ["RopajeElfoOscuro"] = ObjShort(section, "RopajeElfoOscuro"),
            ["RopajeElfaOscura"] = ObjShort(section, "RopajeElfaOscura"),
            ["RopajeEnano"] = ObjShort(section, "RopajeEnano"),
            ["RopajeEnana"] = ObjShort(section, "RopajeEnana"),
            ["RopajeGnomo"] = ObjShort(section, "RopajeGnomo"),
            ["RopajeGnoma"] = ObjShort(section, "RopajeGnoma"),
            ["RopajeOrco"] = ObjShort(section, "RopajeOrco"),
            ["RopajeOrca"] = ObjShort(section, "RopajeOrca"),
            ["MaxLevel"] = (byte)Math.Clamp(ObjInt(section, "MaxLev"), 0, byte.MaxValue),
            ["Cooldown"] = cooldown,
            ["CooldownType"] = (byte)Math.Clamp(ObjInt(section, "CDType"), 0, byte.MaxValue),
            ["PotionPercent"] = (byte)Math.Clamp(ObjInt(section, "Porcentaje"), 0, byte.MaxValue),
            ["EffectDuration"] = ObjInt(section, "DuracionEfecto"),
            ["Sound1"] = ObjShort(section, "SND1"),
            ["Sound2"] = ObjShort(section, "SND2"),
            ["Sound3"] = ObjShort(section, "SND3"),
            ["UseOnSafeAreaOnly"] = ObjInt(section, "UseOnSafeAreaOnly") != 0,
            ["VidaUtil"] = ObjShort(section, "VidaUtil"),
            ["TimeToRegenerate"] = ObjInt(section, "TiempoRegenerar"),
            ["ClosedKeyIndex"] = ObjShort(section, "IndexCerradaLlave"),
            ["Closed"] = ObjInt(section, "Cerrada") != 0,
            ["MinHitToNpc"] = ObjShort(section, "MinHitToNPC"),
            ["MaxHitToNpc"] = ObjShort(section, "MaxHitToNPC"),
            ["ShieldPercent"] = (byte)Math.Clamp(ObjInt(section, "Porcentaje"), 0, 100),
            ["Craftable"] = ObjInt(section, "Crafteable") == 1,
            ["CraftMaterials"] = Get(section, "Materiales")
                .Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => short.TryParse(value, NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var material) ? material : (short)0)
                .Where(material => material > 0).ToArray(),
            ["CraftType"] = (byte)Math.Clamp(ObjInt(section, "TipoCrafteo"), 0, byte.MaxValue),
            ["CraftProbability"] = (byte)Math.Clamp(ObjInt(section, "ProbCrafteo"), 0, 100),
            ["CraftCost"] = Math.Max(0, ObjInt(section, "CostoCrafteo")),
            ["CatalystType"] = (byte)Math.Clamp(ObjInt(section, "CatalizadorTipo"), 0, byte.MaxValue),
            ["CatalystBoost"] = (float)LegacyDouble(Get(section, "CatalizadorAumento")),
            ["MaderaPino"] = ObjShort(section, "Maderapino"),
            ["Coal"] = ObjShort(section, "Coal"),
            ["Blodium"] = ObjShort(section, "Blodium"),
            ["FireEssence"] = ObjShort(section, "FireEssence"),
            ["WaterEssence"] = ObjShort(section, "WaterEssence"),
            ["EarthEssence"] = ObjShort(section, "EarthEssence"),
            ["WindEssence"] = ObjShort(section, "WindEssence"),
            ["ElementalTags"] = ObjInt(section, "ElementalTags"),
            ["SkPociones"] = ObjShort(section, "SkPociones"),
            ["SkSastreria"] = skSastreria,
            ["Mortero"] = ObjShort(section, "Mortero"),
            ["FrascoAlq"] = ObjShort(section, "FrascoAlq"),
            ["HongoDeLuz"] = ObjShort(section, "HongoDeLuz"),
            ["Esporas"] = ObjShort(section, "Esporas"),
            ["Tuna"] = ObjShort(section, "Tuna"),
            ["Cala"] = ObjShort(section, "Cala"),
            ["ColaDeZorro"] = ObjShort(section, "ColaDeZorro"),
            ["FlorOceano"] = ObjShort(section, "FlorOceano"),
            ["FlorRoja"] = ObjShort(section, "FlorRoja"),
            ["SemillasProsperas"] = ObjShort(section, "SemillasProsperas"),
            ["PielLobo"] = ObjShort(section, "PielLobo"),
            ["PielOsoPardo"] = ObjShort(section, "PielOsoPardo"),
            ["PielOsoPolar"] = ObjShort(section, "PielOsoPolaR"),
            ["PielLoboNegro"] = ObjShort(section, "PielLoboNegro"),
            ["PielTigre"] = ObjShort(section, "PielTigre"),
            ["PielTigreBengala"] = ObjShort(section, "PielTigreBengala"),
            ["Envenena"] = ObjInt(section, "Envenena") != 0,
            ["Incinera"] = ObjInt(section, "Incinera") != 0,
            ["Paraliza"] = ObjInt(section, "Paraliza") != 0,
            ["Estupidiza"] = ObjInt(section, "Estupidiza") != 0,
            ["Revive"] = ObjInt(section, "Revive") != 0,
            ["Camouflage"] = ObjInt(section, "Camouflage") != 0,
            ["EfectoMagico"] = ObjInt(section, "EfectoMagico"),
            ["ApplyEffectId"] = ObjInt(section, "ApplyEffectId"),
            ["CreaFx"] = ObjInt(section, "CreaFX"),
            ["CreaLuz"] = Get(section, "CreaLuz"),
            ["CreaParticula"] = Get(section, "CreaParticula"),
            ["CreaWav"] = ObjInt(section, "CreaWav"),
            ["ParticulaGolpe"] = ObjInt(section, "ParticulaGolpe"),
            ["ParticulaGolpeTime"] = ObjInt(section, "ParticulaGolpeTime"),
            ["ParticulaViaje"] = ObjInt(section, "ParticulaViaje"),
            ["MagicPower"] = ObjInt(section, "MagicPower"),
            ["MagicDamageBonus"] = ObjInt(section, "MagicDamageBonus"),
            ["MagicAbsoluteBonus"] = ObjInt(section, "MagicAbsoluteBonus"),
            ["MagicPenetration"] = ObjInt(section, "MagicPenetration"),
            ["ResistenciaMagica"] = ObjInt(section, "ResistenciaMagica"),
            ["MinArmorPenetrationFlat"] = ObjInt(section, "MinArmorPenetrationFlat"),
            ["MaxArmorPenetrationFlat"] = ObjInt(section, "MaxArmorPenetrationFlat"),
            ["ArmorPenetrationPercent"] = ObjInt(section, "ArmorPenetrationPercent"),
            ["ExtraCritAndStabChance"] = ObjInt(section, "ExtraCritAndStabChance"),
            ["ImprovedMHit"] = ObjInt(section, "ImprovedMHit"),
            ["ImprovedRHit"] = ObjInt(section, "ImprovedRHit"),
            ["JineteLevel"] = ObjInt(section, "JineteLevel"),
            ["ProjectileType"] = ObjShort(section, "ProjectileType"),
            ["ArrowCategory"] = ObjShort(section, "ArrowCategory"),
            ["BowCategory"] = ObjShort(section, "BowCategory"),
            // FileIO.bas:1366/1385/1526: para baculos .Power = MagicPower;
            // para cañas de pescar la clave es "Power".
            ["Power"] = FirstNonZero(section, "Power", "MagicPower"),
            ["Velocidad"] = (float)LegacyDouble(Get(section, "Velocidad")),
            ["Rp"] = ObjInt(section, "RP"),
            ["ClaseTipo"] = ObjInt(section, "ClaseTipo"),
            ["MinHp"] = ObjShort(section, "MinHP"),
            ["MaxHp"] = ObjShort(section, "MaxHP"),
            ["MinSt"] = ObjShort(section, "MinST"),
            ["MinAgu"] = ObjShort(section, "MinAgu"),
            ["DesdeMap"] = desdeMap,
            ["Map"] = map,
            ["HastaMap"] = hastaMap,
            ["HastaX"] = hastaX,
            ["HastaY"] = hastaY,
            // Referencias al WORLD resueltas desde las claves legacy de arriba.
            ["DestinationGlobal"] = ResolvePoint(spatial, hastaMap, hastaX, hastaY),
            ["SourceFloorZ"] = ResolveFloor(spatial, desdeMap),
            ["SectorId"] = ObjShort(section, "SectorId"),
            ["X"] = (byte)Math.Clamp(ObjInt(section, "X"), 0, byte.MaxValue),
            ["Y"] = (byte)Math.Clamp(ObjInt(section, "Y"), 0, byte.MaxValue),
            ["NecesitaNave"] = ObjInt(section, "NecesitaNave") != 0,
            ["RecursoObj"] = ObjShort(section, "RecursoObj"),
            ["Cantidad"] = ObjShort(section, "Cantidad"),
            ["CantItem"] = ObjShort(section, "CantItem"),
            ["Item"] = ObjShort(section, "Item"),
            ["Drop"] = ObjShort(section, "Drop"),
            ["ChestItems"] = ReadObjectChestItems(section, objectType),
            ["Destruye"] = ObjInt(section, "Destruye") != 0,
            ["RepairTo"] = ObjShort(section, "RepairTo"),
            ["RequiereObjeto"] = ObjShort(section, "RequiereObjeto"),
            ["QuestId"] = ObjInt(section, "QuestID"),
            ["FactionScore"] = ObjInt(section, "FactionScore"),
            ["CollectibleCardIndex"] = ObjInt(section, "CollectibleCardIndex"),
            ["CollectibleCardRarity"] = ObjInt(section, "CollectibleCardRarity"),
            ["JailObject"] = ObjInt(section, "JailObject") != 0,
            ["LeadersOnly"] = ObjInt(section, "LeadersOnly") != 0,
            ["AssignedCastleIndex"] = ObjInt(section, "AssignedCastleIndex"),
            ["Gema"] = ObjShort(section, "Gema"),
            ["Gemas"] = ObjShort(section, "Gemas"),
            ["Peces"] = ObjShort(section, "Peces"),
            ["Pino"] = ObjShort(section, "Pino"),
            ["PuntosPesca"] = ObjShort(section, "PuntosPesca"),
            ["Runetype"] = ObjInt(section, "Runetype"),
            ["CantidadSkill"] = ObjInt(section, "CantidadSkill"),
            ["CuantoAumento"] = ObjInt(section, "cuantoaumento"),
            ["QueAtributo"] = ObjInt(section, "queatributo"),
            ["QueSkill"] = ObjInt(section, "QueSkill"),
            ["CantEntrega"] = ObjInt(section, "CantEntrega"),
            ["Jerarquia"] = ObjInt(section, "Jerarquia"),
            ["Abierta"] = ObjInt(section, "abierta") != 0,
            ["RazaAltos"] = ObjShort(section, "RazaAltos"),
            ["RazaBajos"] = ObjShort(section, "RazaBajos"),
            ["RazaElfa"] = ObjInt(section, "RazaElfa") != 0,
            ["RazaGnoma"] = ObjInt(section, "RazaGnoma") != 0,
            ["RazaHumana"] = ObjInt(section, "RazaHumana") != 0,
            ["RazaOrca"] = ObjInt(section, "RazaOrca") != 0,
            ["NoTradeableToNpc"] = ObjInt(section, "NoTradeableToNPC") != 0,
            ["Bindable"] = ObjInt(section, "Bindable") != 0,
            ["Instransferible"] = ObjInt(section, "Instransferible") != 0,
            ["Intirable"] = ObjInt(section, "Intirable") != 0,
            ["NoSeLimpia"] = ObjInt(section, "NoSeLimpia") != 0,
            ["NumRopaje"] = ObjShort(section, "NumRopaje"),
            ["VGrande"] = ObjInt(section, "VGrande") != 0,
            ["Elfico"] = ObjInt(section, "Elfico") != 0,
            ["Dorada"] = ObjInt(section, "Dorada") != 0,
            ["Nft"] = ObjInt(section, "NFT") != 0,

            // Claves conservadas 1:1 aunque el runtime no las consuma todavia:
            // render de luz del cliente, metadatos y localizacion.
            ["ClientOverwritebody"] = ObjInt(section, "ClientOverwritebody"),
            ["CofreIndex"] = ObjInt(section, "CofreIndex"),
            ["CollectibleCardImgPathing"] = Get(section, "CollectibleCardImgPathing"),
            ["ExtraStr"] = Get(section, "ExtraStr"),
            ["Info"] = Get(section, "Info"),
            ["ItemSeasonSpecial"] = ObjInt(section, "ItemSeasonSpecial") != 0,
            ["LocaleID"] = ObjInt(section, "LocaleID"),
            ["NoConsumeInCombatZones"] = ObjInt(section, "NoConsumeInCombatZones") != 0,
            ["NotConsumable"] = ObjInt(section, "NotConsumable") != 0,
            ["NoWiki"] = ObjInt(section, "NoWiki") != 0,
            ["NpcOnly"] = ObjInt(section, "NpcOnly") != 0,
            ["Permanente"] = ObjInt(section, "Permanente"),
            ["Peso"] = ObjInt(section, "Peso"),
            ["PuntosBufon"] = ObjInt(section, "PuntosBufón") != 0,
            ["PuntosBusqueda"] = ObjInt(section, "PuntosBusqueda") != 0,
            ["PuntosCofre"] = ObjInt(section, "PuntosCofre") != 0,
            ["PuntosConsejeros"] = ObjInt(section, "PuntosConsejeros") != 0,
            ["Raices"] = ObjInt(section, "Raices"),
            ["TipoRuna"] = ObjInt(section, "TipoRuna"),
            ["LightEnabled"] = ObjInt(section, "LightEnabled") != 0,
            ["LightRadius"] = ObjInt(section, "LightRadius"),
            ["LightColorR"] = (float)LegacyDouble(Get(section, "LightColorR")),
            ["LightColorG"] = (float)LegacyDouble(Get(section, "LightColorG")),
            ["LightColorB"] = (float)LegacyDouble(Get(section, "LightColorB")),
            ["LightIntensity"] = (float)LegacyDouble(Get(section, "LightIntensity")),
            ["LightFlickerEnabled"] = ObjInt(section, "LightFlickerEnabled") != 0,
            ["LightFlickerSpeed"] = (float)LegacyDouble(Get(section, "LightFlickerSpeed")),
            ["LightFlickerIntensityAmplitude"] = (float)LegacyDouble(Get(section, "LightFlickerIntensityAmplitude")),
            ["LightFlickerRadiusAmplitude"] = (float)LegacyDouble(Get(section, "LightFlickerRadiusAmplitude")),
            ["Localization"] = ReadLocalization(section, ObjectLocalizationKeys)
        };
    }

    private static object[] ReadObjectChestItems(Dictionary<string, string> section, int objectType)
    {
        if (objectType == 22) // otOreDeposit: GemaN = "ObjIndex-Probabilidad".
        {
            var count = Math.Clamp((int)ObjShort(section, "Gemas"), 0, 64);
            if (count <= 0) return [];
            var gems = new List<object>(count);
            for (var index = 1; index <= count; index++)
            {
                var parts = Get(section, "Gema" + index).Split('-', 2);
                var objectIndex = LegacyShort(parts[0]);
                var chance = parts.Length > 1 ? LegacyShort(parts[1]) : (short)1;
                if (objectIndex > 0)
                    gems.Add(new
                    {
                        ObjectIndex = objectIndex,
                        Amount = (short)1,
                        DropChance = Math.Max((short)1, chance)
                    });
            }
            return gems.ToArray();
        }

        var chestCount = Math.Clamp((int)ObjShort(section, "CantItem"), 0, 64);
        if (objectType != 48 || chestCount <= 0) return []; // otChest
        var items = new List<object>(chestCount);
        for (var index = 1; index <= chestCount; index++)
        {
            var objectIndex = ObjShort(section, "Item" + index);
            if (objectIndex <= 0) continue;
            var amount = Math.Max((short)1, ObjShort(section, "Cantidad" + index));
            // Subtipo 3: DropN es 1-en-X; se guarda como probabilidad.
            var drop = Math.Max((short)1, ObjShort(section, "Drop" + index));
            items.Add(new { ObjectIndex = objectIndex, Amount = amount, DropChance = drop });
        }
        return items.ToArray();
    }

    private static object[] ReadCraftingMaterials(Dictionary<string, string> section) =>
        Enumerable.Range(1, 4)
            .Select(position => Get(section, "ItemCrafteo" + position).Split('-', 2))
            .Select(values => new
            {
                ObjectIndex = ParseField(values, 0),
                Amount = ParseField(values, 1)
            })
            .Where(material => material.ObjectIndex > 0 && material.Amount > 0)
            .ToArray();

    private static readonly Dictionary<string, byte> ObjectClassNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Declares.bas:325-337 e_Class / General.bas:430-441 ListaClases.
        ["Mago"] = 1,
        ["Clerigo"] = 2,
        ["Guerrero"] = 3,
        ["Asesino"] = 4,
        ["Bardo"] = 5,
        ["Druida"] = 6,
        ["Paladin"] = 7,
        ["Cazador"] = 8,
        ["Trabajador"] = 9,
        ["Pirata"] = 10,
        ["Ladron"] = 11,
        ["Bandido"] = 12
    };

    private static readonly Dictionary<string, byte> ObjectSkillNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Magia"] = 1,
        ["Robar"] = 2,
        ["Evasion en combate"] = 3,
        ["Combate con armas"] = 4,
        ["Meditar"] = 5,
        ["Apunalar"] = 6,
        ["Ocultarse"] = 7,
        ["Supervivencia"] = 8,
        ["Talar"] = 9,
        ["Comercio"] = 10,
        ["Defensa con escudos"] = 11,
        ["Pesca"] = 12,
        ["Mineria"] = 13,
        ["Carpinteria"] = 14,
        ["Herreria"] = 15,
        ["Liderazgo"] = 16,
        ["Domar animales"] = 17,
        ["Combate a distancia"] = 18,
        ["Combate sin armas"] = 19,
        ["Navegacion"] = 20
    };

    private static readonly Dictionary<string, byte> ObjectRaceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Humano"] = 1,
        ["Elfo"] = 2,
        ["Drow"] = 3,
        ["Gnomo"] = 4,
        ["Enano"] = 5,
        ["Orco"] = 6
    };

    /// <summary>
    /// Claves de localizacion de obj.dat. Se declaran como datos para que la
    /// conversion siga siendo 1:1 y auditable clave por clave.
    /// </summary>
    private static readonly (string Code, string Field, string Key)[] ObjectLocalizationKeys =
    [
        ("en", "Name", "en_Name"), ("en", "Nombre", "en_Nombre"), ("en", "Texto", "en_Texto"), ("en", "Info", "en_Info"),
        ("fr", "Name", "fr_Name"), ("fr", "Nombre", "fr_Nombre"), ("fr", "Texto", "fr_Texto"),
        ("it", "Name", "it_Name"), ("it", "Nombre", "it_Nombre"), ("it", "Texto", "it_Texto"),
        ("pt", "Name", "pt_Name"), ("pt", "Nombre", "pt_Nombre"), ("pt", "Texto", "pt_Texto")
    ];

    private static readonly (string Code, string Field, string Key)[] NpcLocalizationKeys =
    [
        ("en", "Name", "en_Name"), ("en", "Desc", "en_Desc"), ("en", "DescClose", "en_DescClose"),
        ("fr", "Name", "fr_Name"), ("fr", "Desc", "fr_Desc"), ("fr", "DescClose", "fr_DescClose"),
        ("it", "Name", "it_Name"), ("it", "Desc", "it_Desc"), ("it", "DescClose", "it_DescClose"),
        ("pt", "Name", "pt_Name"), ("pt", "Desc", "pt_Desc"), ("pt", "DescClose", "pt_DescClose")
    ];

    private static readonly (string Code, string Field, string Key)[] QuestLocalizationKeys =
    [
        ("en", "Nombre", "en_Nombre"), ("en", "Desc", "en_Desc"), ("en", "DescFinal", "en_DescFinal"),
        ("fr", "Nombre", "fr_Nombre"), ("fr", "Desc", "fr_Desc"), ("fr", "DescFinal", "fr_DescFinal"),
        ("it", "Nombre", "it_Nombre"), ("it", "Desc", "it_Desc"), ("it", "DescFinal", "it_DescFinal"),
        ("pt", "Nombre", "pt_Nombre"), ("pt", "Desc", "pt_Desc"), ("pt", "DescFinal", "pt_DescFinal")
    ];

    private static readonly (string Code, string Field, string Key)[] SpellLocalizationKeys =
    [
        ("en", "Nombre", "en_Nombre"), ("en", "Desc", "en_Desc"), ("en", "HechizeroMsg", "en_HechizeroMsg"),
        ("en", "TargetMsg", "en_TargetMsg"), ("en", "PropioMsg", "en_PropioMsg"), ("en", "Name", "en_Name"), ("en", "Texto", "en_Texto"),
        ("pt", "Nombre", "pt_Nombre"), ("pt", "Desc", "pt_Desc"), ("pt", "HechizeroMsg", "pt_HechizeroMsg"),
        ("pt", "TargetMsg", "pt_TargetMsg"), ("pt", "PropioMsg", "pt_PropioMsg"), ("pt", "Name", "pt_Name"), ("pt", "Texto", "pt_Texto"),
        ("fr", "Nombre", "fr_Nombre"), ("fr", "Desc", "fr_Desc"), ("fr", "HechizeroMsg", "fr_HechizeroMsg"),
        ("fr", "TargetMsg", "fr_TargetMsg"), ("fr", "PropioMsg", "fr_PropioMsg"), ("fr", "Name", "fr_Name"), ("fr", "Texto", "fr_Texto"),
        ("it", "Nombre", "it_Nombre"), ("it", "Desc", "it_Desc"), ("it", "HechizeroMsg", "it_HechizeroMsg"),
        ("it", "TargetMsg", "it_TargetMsg"), ("it", "PropioMsg", "it_PropioMsg"), ("it", "Name", "it_Name"), ("it", "Texto", "it_Texto")
    ];

    private static Dictionary<string, Dictionary<string, string>> ReadLocalization(
        Dictionary<string, string> section, (string Code, string Field, string Key)[] map)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (code, field, key) in map)
        {
            var value = Get(section, key);
            if (value.Length == 0) continue;
            if (!result.TryGetValue(code, out var fields))
                result[code] = fields = new Dictionary<string, string>(StringComparer.Ordinal);
            fields[field] = value;
        }
        return result;
    }

    private static byte ClassId(string name) =>
        name.Length > 0 && ObjectClassNames.TryGetValue(name.Trim(), out var id) ? id : (byte)0;

    private static byte RaceId(string name) =>
        name.Length > 0 && ObjectRaceNames.TryGetValue(name.Trim(), out var id) ? id : (byte)0;

    private static (byte Skill, byte Amount) ParseSkillRequirement(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (0, 0);
        var parts = value.Split('-', 2);
        var name = parts[0].Replace("+", " ").Trim();
        var amount = parts.Length > 1 && byte.TryParse(parts[1], NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : (byte)0;
        return ObjectSkillNames.TryGetValue(name, out var skill) ? (skill, amount) : ((byte)0, (byte)0);
    }

    private static short ParseField(string[] values, int index) =>
        index < values.Length && short.TryParse(values[index], out var value) ? value : (short)0;

    private static int ObjInt(Dictionary<string, string> section, string key) =>
        LegacyInt(Get(section, key));

    private static short ObjShort(Dictionary<string, string> section, string key) =>
        LegacyShort(Get(section, key));

    private static byte ObjByte(Dictionary<string, string> section, string key) =>
        (byte)Math.Clamp(LegacyInt(Get(section, key)), 0, byte.MaxValue);

    private static int FirstNonZero(Dictionary<string, string> section, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = ObjInt(section, key);
            if (value != 0) return value;
        }
        return 0;
    }

    private static int LegacyInt(string value) => (int)LegacyDouble(value);

    private static short LegacyShort(string value) =>
        (short)Math.Clamp(LegacyDouble(value), short.MinValue, short.MaxValue);

    /// <summary>Equivalente a <c>Val()</c> de VB6, igual que LegacyValue del servidor.</summary>
    private static double LegacyDouble(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var text = value.Trim();
        var index = 0;
        var negative = false;
        if (text[index] is '+' or '-')
        {
            negative = text[index] == '-';
            index++;
        }

        if (index + 1 < text.Length && text[index] == '&' && text[index + 1] is 'H' or 'h')
        {
            index += 2;
            var startHex = index;
            while (index < text.Length && Uri.IsHexDigit(text[index])) index++;
            if (index == startHex) return 0;
            var hex = int.Parse(text[startHex..index], NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture);
            return negative ? -hex : hex;
        }

        var start = index;
        var seenSeparator = false;
        while (index < text.Length)
        {
            var character = text[index];
            if (char.IsDigit(character)) { index++; continue; }
            if (!seenSeparator && character is '.' or ',') { seenSeparator = true; index++; continue; }
            break;
        }

        if (index == start) return 0;
        var number = text[start..index].Replace(',', '.');
        return double.TryParse(number, NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? (negative ? -parsed : parsed)
            : 0;
    }

    private static byte[] NormalizeQuests(byte[] payload, WorldSpatialResolver? spatial)
    {
        if (payload.Length == 0)
            return JsonSerializer.SerializeToUtf8Bytes(
                new { Schema = 1, Catalog = "quests", Items = Array.Empty<object>() });
        if (DecodeLegacyText(payload).TrimStart().StartsWith('{'))
            return payload;

        var sections = ParseIni(payload);
        var init = GetSection(sections, "INIT");
        var count = Math.Max(0, ParseInt(Get(init, "NumQuests")));
        var items = Enumerable.Range(1, count).Select(index =>
        {
            var section = GetSection(sections, "QUEST" + index);
            var posMap = ParseInt(Get(section, "PosMap"));
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
                GlobalQuestThresholdNeeded = ParseInt(Get(section, "GlobalQuestThresholdNeeded")),

                // Claves conservadas 1:1 aunque el runtime no las consuma todavia.
                DescAudio = ParseInt(Get(section, "DescAudio")),
                DescFinalAudio = ParseInt(Get(section, "DescFinalAudio")),
                NoWiki = ParseInt(Get(section, "NoWiki")) != 0,
                PosMap = posMap,
                PosFloorZ = ResolveFloor(spatial, posMap),
                RequiredFactionScore = ParseInt(Get(section, "RequiredFactionScore")),
                RequiredKillCount = ParseInt(Get(section, "RequiredKills")),
                RequiredKillTargets = ReadRequirements(section, "RequiredKills", "RequiredKill"),
                Localization = ReadLocalization(section, QuestLocalizationKeys)
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
        foreach (var raw in DecodeLegacyText(payload).Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            // Igual que IniFile.ReadBytes: la seccion cierra en el primer ']' y
            // admite comentarios despues (por ejemplo "[Tesoros] 'Campo").
            if (line.StartsWith('['))
            {
                var end = line.IndexOf(']');
                if (end <= 1) continue;
                var name = line[1..end].Trim();
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (!result.TryAdd(name, current))
                    current = result[name];
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
