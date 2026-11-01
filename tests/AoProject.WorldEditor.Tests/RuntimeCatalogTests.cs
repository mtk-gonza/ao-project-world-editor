using System.Text;
using System.Text.Json;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class RuntimeCatalogTests
{
    [Fact]
    public void Runtime_data_converts_legacy_treasure_ini_to_versioned_json()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-catalog-");
        try
        {
            var bundle = new UnifiedWorldBundle
            {
                Events = "# events"u8.ToArray(),
                Treasures = Encoding.UTF8.GetBytes("""
                    [Tesoros] 'Campo
                    CantidadMapas=1
                    Mapa1=16
                    TiposDeTesoros=1
                    Tesoro1=3140-2

                    [Regalos] 'Dubgeon
                    CantidadMapas=1
                    Mapa1=293
                    TiposDeRegalos=1
                    Regalo1=3744-3
                    """),
                Quests = "# quests"u8.ToArray(),
                Npcs = "# npcs"u8.ToArray(),
                Objects = "# objects"u8.ToArray(),
                Castles = "[]"u8.ToArray()
            };

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);

            var path = Path.Combine(root.FullName, "runtime-data", "treasures.json");
            Assert.True(File.Exists(path));
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var rootElement = document.RootElement;
            Assert.Equal(1, rootElement.GetProperty("Schema").GetInt32());
            Assert.Equal("treasures", rootElement.GetProperty("Catalog").GetString());
            Assert.Equal(16, rootElement.GetProperty("TreasureSectors")[0].GetInt32());
            Assert.Equal(3140, rootElement.GetProperty("Treasures")[0].GetProperty("ObjectIndex").GetInt32());
            Assert.Equal(293, rootElement.GetProperty("GiftSectors")[0].GetInt32());
            Assert.Equal(3744, rootElement.GetProperty("Gifts")[0].GetProperty("ObjectIndex").GetInt32());
        }
        finally
        {
            try { Directory.Delete(root.FullName, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Runtime_data_converts_legacy_obj_dat_to_versioned_json()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-objects-");
        try
        {
            var bundle = new UnifiedWorldBundle
            {
                Events = "# events"u8.ToArray(),
                Treasures = "# treasures"u8.ToArray(),
                Quests = "# quests"u8.ToArray(),
                Npcs = "# npcs"u8.ToArray(),
                Objects = Encoding.UTF8.GetBytes("""
                    [INIT]
                    NumOBJs=3

                    [OBJ1]
                    Name=Espada
                    ObjType=1
                    GrhIndex=100
                    Valor=5
                    Newbie=1
                    Agarrable=1
                    CP1=Guerrero
                    RP1=Elfo
                    SkillRequerido=Combate+con+armas-30
                    ItemCrafteo1=10-2

                    [OBJ2]
                    Name=Cofre
                    ObjType=48
                    CantItem=1
                    Item1=500
                    Cantidad1=2
                    Drop1=3

                    [OBJ3]
                    Name=Pasaje
                    ObjType=36
                    SectorId=399
                    X=50
                    Y=60
                    HastaMap=1
                    HastaX=1
                    HastaY=1
                    """),
                Castles = "[]"u8.ToArray()
            };

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(root.FullName, "runtime-data", "objects.json")));
            var rootElement = document.RootElement;
            Assert.Equal("objects", rootElement.GetProperty("Catalog").GetString());
            var items = rootElement.GetProperty("Items");
            Assert.Equal(3, items.GetArrayLength());

            var sword = items[0];
            Assert.Equal("Espada", sword.GetProperty("Name").GetString());
            Assert.Equal(100, sword.GetProperty("Graphic").GetInt32());
            Assert.True(sword.GetProperty("Newbie").GetBoolean());
            Assert.Equal(3, sword.GetProperty("ProhibitedClasses")[0].GetInt32());
            Assert.Equal(2, sword.GetProperty("ProhibitedRaces")[0].GetInt32());
            Assert.Equal(4, sword.GetProperty("RequiredSkill").GetInt32());
            Assert.Equal(30, sword.GetProperty("RequiredSkillAmount").GetInt32());
            Assert.Equal(10, sword.GetProperty("CraftingMaterials")[0].GetProperty("ObjectIndex").GetInt32());
            Assert.Equal(2, sword.GetProperty("CraftingMaterials")[0].GetProperty("Amount").GetInt32());

            var chest = items[1];
            Assert.Equal(48, chest.GetProperty("Type").GetInt32());
            Assert.Equal(500, chest.GetProperty("ChestItems")[0].GetProperty("ObjectIndex").GetInt32());
            Assert.Equal(2, chest.GetProperty("ChestItems")[0].GetProperty("Amount").GetInt32());
            Assert.Equal(3, chest.GetProperty("ChestItems")[0].GetProperty("DropChance").GetInt32());

            // Los pasajes guardan el destino en SectorId/X/Y, no en HastaMap.
            var ticket = items[2];
            Assert.Equal(399, ticket.GetProperty("HastaMap").GetInt32());
            Assert.Equal(50, ticket.GetProperty("HastaX").GetInt32());
            Assert.Equal(60, ticket.GetProperty("HastaY").GetInt32());
        }
        finally
        {
            try { Directory.Delete(root.FullName, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Runtime_data_decodes_legacy_obj_dat_as_cp1252()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-objects-ansi-");
        try
        {
            var payload = new List<byte>();
            payload.AddRange(Encoding.ASCII.GetBytes("[INIT]\nNumOBJs=1\n\n[OBJ1]\nName="));
            payload.Add(0xC1); // 'Á' en CP1252/ANSI, inválido como UTF-8 aislado.
            payload.AddRange(Encoding.ASCII.GetBytes("rbol\nObjType=1\nGrhIndex=4\n"));

            var bundle = new UnifiedWorldBundle
            {
                Events = "# events"u8.ToArray(),
                Treasures = "# treasures"u8.ToArray(),
                Quests = "# quests"u8.ToArray(),
                Npcs = "# npcs"u8.ToArray(),
                Objects = payload.ToArray(),
                Castles = "[]"u8.ToArray()
            };

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(root.FullName, "runtime-data", "objects.json")));
            Assert.Equal("Árbol", document.RootElement.GetProperty("Items")[0].GetProperty("Name").GetString());
        }
        finally
        {
            try { Directory.Delete(root.FullName, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Runtime_data_converts_legacy_npc_dat_to_versioned_json()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-npcs-");
        try
        {
            var bundle = new UnifiedWorldBundle
            {
                Events = "# events"u8.ToArray(),
                Treasures = "# treasures"u8.ToArray(),
                Quests = "# quests"u8.ToArray(),
                Npcs = Encoding.UTF8.GetBytes("""
                    [INIT]
                    NumNPCs=1

                    [NPC1]
                    Name=Guardia
                    NpcType=3
                    Body=12
                    Head=5
                    Attackable=1
                    Comercia=1
                    MinHP=100
                    MaxHP=200
                    MinMana=1
                    MaxMana=4
                    ReSpawn=1
                    InvReSpawn=0
                    DropCount=1
                    Drop1=200-500-1-3
                    NROITEMS=1
                    Obj1=100-1
                    LanzaSpells=2
                    Sp1=9
                    Sp2=12
                    Cd1=3
                    Cd2=4
                    CaminataLen=1
                    Caminata1=1:0:5
                    NumQuest=2
                    QuestNumber1=5
                    QuestNumber2=6
                    CityCount=1
                    City1=Ciudad-35-50-50-100
                    """),
                Objects = "# objects"u8.ToArray(),
                Castles = "[]"u8.ToArray()
            };

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(root.FullName, "runtime-data", "npcs.json")));
            var rootElement = document.RootElement;
            Assert.Equal("npcs", rootElement.GetProperty("Catalog").GetString());
            var npc = rootElement.GetProperty("Items")[0];
            Assert.Equal(1, npc.GetProperty("Number").GetInt32());
            Assert.Equal("Guardia", npc.GetProperty("Name").GetString());
            Assert.Equal(3, npc.GetProperty("Heading").GetInt32());
            Assert.True(npc.GetProperty("Attackable").GetBoolean());
            // ReSpawn=1 significa "no reaparece" en el formato legacy.
            Assert.False(npc.GetProperty("Respawns").GetBoolean());
            Assert.True(npc.GetProperty("InventoryRespawns").GetBoolean());
            Assert.Equal(500, npc.GetProperty("Drops")[0].GetProperty("Chance").GetInt32());
            Assert.Equal(9, npc.GetProperty("Spells")[0].GetInt32());
            Assert.Equal(3, npc.GetProperty("SpellCooldowns")[0].GetInt32());
            Assert.Equal(35, npc.GetProperty("TransportCities")[0].GetProperty("SectorId").GetInt32());
            Assert.Equal(2, npc.GetProperty("QuestNumbers").GetArrayLength());
        }
        finally
        {
            try { Directory.Delete(root.FullName, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Runtime_data_converts_legacy_event_dat_to_versioned_json()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-events-");
        try
        {
            var bundle = new UnifiedWorldBundle
            {
                Events = Encoding.UTF8.GetBytes("""
                    [Invasiones]
                    Cantidad=1
                    Invasion1=Bander

                    [Bander]
                    MaxVidaMuralla=200000
                    MaxNPCs=2
                    Aviso=Las hordas se acercan
                    AvisarTiempo=2
                    RepetirAviso=1
                    Desc=Las hordas atacan
                    RepetirDesc=5
                    IntervaloSpawn=5
                    Duracion=3
                    Intervalo=5
                    Offset=1
                    MensajeGanaron=Banderbill resistio
                    MensajePerdieron=La legion entro
                    NPCs=1049-1050
                    SpawnBoxes=1
                    SpawnBox1=58-53-21-81-24-Norte-20

                    [EVENTOS]
                    20=1-60-2
                    """),
                Treasures = "# treasures"u8.ToArray(),
                Quests = "# quests"u8.ToArray(),
                Npcs = "# npcs"u8.ToArray(),
                Objects = "# objects"u8.ToArray(),
                Castles = "[]"u8.ToArray()
            };

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(root.FullName, "runtime-data", "events.json")));
            var rootElement = document.RootElement;
            Assert.Equal("events", rootElement.GetProperty("Catalog").GetString());
            var invasion = rootElement.GetProperty("Invasions")[0];
            Assert.Equal("Bander", invasion.GetProperty("Name").GetString());
            Assert.Equal(2, invasion.GetProperty("MaxNpcs").GetInt32());
            Assert.Equal(1049, invasion.GetProperty("NpcNumbers")[0].GetInt32());
            Assert.Equal("NORTH", invasion.GetProperty("SpawnBoxes")[0].GetProperty("Heading").GetString());
            var schedule = rootElement.GetProperty("Schedule")[0];
            Assert.Equal(20, schedule.GetProperty("Hour").GetInt32());
            Assert.Equal(60, schedule.GetProperty("Duration").GetInt32());
            Assert.Equal(2, schedule.GetProperty("Multiplier").GetInt32());
        }
        finally
        {
            try { Directory.Delete(root.FullName, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Runtime_data_converts_legacy_quest_dat_to_versioned_json()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-quests-");
        try
        {
            var bundle = new UnifiedWorldBundle
            {
                Events = "# events"u8.ToArray(),
                Treasures = "# treasures"u8.ToArray(),
                Quests = Encoding.UTF8.GetBytes("""
                    [INIT]
                    NumQuests=1

                    [QUEST1]
                    Nombre=La mision
                    Desc=Descripcion
                    RequiredLevel=5
                    LimitLevel=20
                    RequiredQuest=3
                    RequiredClassesCount=2
                    RequiredClass1=3
                    RequiredClass2=7
                    RequiredOBJs=1
                    RequiredOBJ1=500-2
                    RequiredSpellCount=1
                    RequiredSpell1=9
                    RequiredNPCs=1
                    RequiredNPC1=40-3
                    RequiredSkill=4
                    RequiredValue=30
                    RewardGLD=100
                    RewardEXP=200
                    RewardOBJs=1
                    RewardOBJ1=600-1
                    RewardSkills=1
                    RewardSkill1=11
                    Trabajador=1
                    TalkTo=77
                    Repetible=1
                    PermittedFactions=6
                    RequiredTargetNPCs=1
                    RequiredTargetNPC1=88
                    GlobalQuestIndex=5
                    GlobalQuestThresholdNeeded=10
                    """),
                Npcs = "# npcs"u8.ToArray(),
                Objects = "# objects"u8.ToArray(),
                Castles = "[]"u8.ToArray()
            };

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(root.FullName, "runtime-data", "quests.json")));
            var rootElement = document.RootElement;
            Assert.Equal("quests", rootElement.GetProperty("Catalog").GetString());
            var quest = rootElement.GetProperty("Items")[0];
            Assert.Equal(1, quest.GetProperty("Index").GetInt32());
            Assert.Equal("La mision", quest.GetProperty("Name").GetString());
            Assert.Equal(3, quest.GetProperty("RequiredQuest").GetInt32());
            Assert.Equal(2, quest.GetProperty("RequiredClasses").GetArrayLength());
            Assert.Equal(500, quest.GetProperty("RequiredObjects")[0].GetProperty("Index").GetInt32());
            Assert.Equal(40, quest.GetProperty("RequiredNpcs")[0].GetProperty("Index").GetInt32());
            Assert.True(quest.GetProperty("WorkerOnly").GetBoolean());
            Assert.Equal(77, quest.GetProperty("TalkTo").GetInt32());
            Assert.Equal(5, quest.GetProperty("GlobalQuestIndex").GetInt32());
        }
        finally
        {
            try { Directory.Delete(root.FullName, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Runtime_data_writes_versioned_castle_catalog()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-castles-");
        try
        {
            var bundle = new UnifiedWorldBundle
            {
                Events = "# events"u8.ToArray(),
                Treasures = "# treasures"u8.ToArray(),
                Quests = "# quests"u8.ToArray(),
                Npcs = "# npcs"u8.ToArray(),
                Objects = "# objects"u8.ToArray(),
                Castles = "[{\"Id\":3,\"Name\":\"Castle\"}]"u8.ToArray()
            };

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(root.FullName, "runtime-data", "castles.json")));
            Assert.Equal("castles", document.RootElement.GetProperty("Catalog").GetString());
            Assert.Equal(3, document.RootElement.GetProperty("Items")[0].GetProperty("Id").GetInt32());
        }
        finally
        {
            try { Directory.Delete(root.FullName, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Runtime_data_preserves_client_only_object_and_localization_fields()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-objects-client-");
        try
        {
            var bundle = new UnifiedWorldBundle
            {
                Events = "# events"u8.ToArray(),
                Treasures = "# treasures"u8.ToArray(),
                Quests = "# quests"u8.ToArray(),
                Npcs = "# npcs"u8.ToArray(),
                Objects = Encoding.UTF8.GetBytes("""
                    [INIT]
                    NumOBJs=1

                    [OBJ1]
                    Name=Iglesia
                    ObjType=1
                    Apuñala=1
                    Peso=10
                    Permanente=4
                    NoWiki=1
                    PuntosCofre=1
                    LightRadius=160
                    LightColorR=0.2
                    LightFlickerEnabled=1
                    LocaleID=37
                    en_Name=Church
                    en_Info=Church information
                    """),
                Castles = "[]"u8.ToArray()
            };

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(root.FullName, "runtime-data", "objects.json")));
            var item = document.RootElement.GetProperty("Items")[0];
            // La clave real del DAT lleva enie; antes se leia "Apunala".
            Assert.True(item.GetProperty("Stabs").GetBoolean());
            Assert.Equal(10, item.GetProperty("Peso").GetInt32());
            Assert.Equal(4, item.GetProperty("Permanente").GetInt32());
            Assert.True(item.GetProperty("NoWiki").GetBoolean());
            Assert.True(item.GetProperty("PuntosCofre").GetBoolean());
            Assert.Equal(160, item.GetProperty("LightRadius").GetInt32());
            Assert.Equal(0.2, item.GetProperty("LightColorR").GetDouble(), 3);
            Assert.True(item.GetProperty("LightFlickerEnabled").GetBoolean());
            Assert.Equal(37, item.GetProperty("LocaleID").GetInt32());
            var localization = item.GetProperty("Localization").GetProperty("en");
            Assert.Equal("Church", localization.GetProperty("Name").GetString());
            Assert.Equal("Church information", localization.GetProperty("Info").GetString());
        }
        finally
        {
            try { Directory.Delete(root.FullName, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Runtime_data_preserves_client_only_npc_and_quest_fields()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-npc-quest-client-");
        try
        {
            var bundle = new UnifiedWorldBundle
            {
                Events = "# events"u8.ToArray(),
                Treasures = "# treasures"u8.ToArray(),
                Quests = Encoding.UTF8.GetBytes("""
                    [INIT]
                    NumQuests=1

                    [QUEST1]
                    Nombre=Prueba
                    DescAudio=1048
                    DescFinalAudio=1049
                    NoWiki=1
                    RequiredFactionScore=2
                    RequiredKills=1
                    RequiredKill1=5-3
                    en_Nombre=Test
                    """),
                Npcs = Encoding.UTF8.GetBytes("""
                    [INIT]
                    NumNPCs=1

                    [NPC1]
                    Name=Comerciante
                    DescClose=Adios
                    WikiSellerType=Pociones
                    Miedo=5
                    NpcFaceGrh=7016
                    BodyIdle=6235
                    NoMapInfo=1
                    fr_Name=Marchand
                    """),
                Objects = "# objects"u8.ToArray(),
                Castles = "[]"u8.ToArray()
            };

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);
            var data = Path.Combine(root.FullName, "runtime-data");

            using (var npcs = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(data, "npcs.json"))))
            {
                var npc = npcs.RootElement.GetProperty("Items")[0];
                Assert.Equal("Adios", npc.GetProperty("DescClose").GetString());
                Assert.Equal("Pociones", npc.GetProperty("WikiSellerType").GetString());
                Assert.Equal(5, npc.GetProperty("Miedo").GetInt32());
                Assert.Equal(7016, npc.GetProperty("NpcFaceGrh").GetInt32());
                Assert.Equal(6235, npc.GetProperty("BodyIdle").GetInt32());
                Assert.True(npc.GetProperty("NoMapInfo").GetBoolean());
                Assert.Equal("Marchand", npc.GetProperty("Localization").GetProperty("fr").GetProperty("Name").GetString());
            }

            using (var quests = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(data, "quests.json"))))
            {
                var quest = quests.RootElement.GetProperty("Items")[0];
                Assert.Equal(1048, quest.GetProperty("DescAudio").GetInt32());
                Assert.Equal(1049, quest.GetProperty("DescFinalAudio").GetInt32());
                Assert.True(quest.GetProperty("NoWiki").GetBoolean());
                Assert.Equal(2, quest.GetProperty("RequiredFactionScore").GetInt32());
                Assert.Equal(1, quest.GetProperty("RequiredKillCount").GetInt32());
                Assert.Equal(5, quest.GetProperty("RequiredKillTargets")[0].GetProperty("Index").GetInt32());
                Assert.Equal(3, quest.GetProperty("RequiredKillTargets")[0].GetProperty("Amount").GetInt32());
                Assert.Equal("Test", quest.GetProperty("Localization").GetProperty("en").GetProperty("Nombre").GetString());
            }
        }
        finally
        {
            try { Directory.Delete(root.FullName, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Runtime_data_resolves_legacy_maps_to_world_coordinates()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-spatial-");
        try
        {
            var bundle = new UnifiedWorldBundle
            {
                Events = "# events"u8.ToArray(),
                Treasures = Encoding.UTF8.GetBytes("""
                    [Tesoros]
                    CantidadMapas=1
                    Mapa1=400
                    TiposDeTesoros=1
                    Tesoro1=100-5
                    """),
                Quests = Encoding.UTF8.GetBytes("""
                    [INIT]
                    NumQuests=1

                    [QUEST1]
                    Nombre=Global
                    PosMap=400
                    """),
                Npcs = Encoding.UTF8.GetBytes("""
                    [INIT]
                    NumNPCs=1

                    [NPC1]
                    Name=Barquero
                    MapTargetEntry=400
                    MapTargetEntryX=5
                    MapTargetEntryY=6
                    CityCount=1
                    City1=Ciudad-400-10-20-100
                    """),
                Objects = Encoding.UTF8.GetBytes("""
                    [INIT]
                    NumOBJs=1

                    [OBJ1]
                    Name=Pasaje
                    ObjType=36
                    SectorId=399
                    X=50
                    Y=60
                    """),
                Castles = "[]"u8.ToArray()
            };
            bundle.Add(new UnifiedWorldMap(400, 200, 0));
            bundle.SetPlacements(
            [
                new GlobalSectorPlacement(399, new GlobalChunkCoordinate(0, 0), "surface"),
                new GlobalSectorPlacement(400, new GlobalChunkCoordinate(1, 0), "surface")
            ]);
            bundle.SetSectorFloors(
            [
                new KeyValuePair<short, int>(399, 0),
                new KeyValuePair<short, int>(400, 0)
            ]);

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);
            var data = Path.Combine(root.FullName, "runtime-data");

            using (var treasures = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(data, "treasures.json"))))
            {
                var areas = treasures.RootElement.GetProperty("TreasureAreas");
                Assert.Equal(1, areas.GetArrayLength());
                Assert.Equal(400, areas[0].GetProperty("SectorId").GetInt32());
                Assert.Equal(0, areas[0].GetProperty("FloorZ").GetInt32());
                Assert.Equal(75, areas[0].GetProperty("MinX").GetInt32());
                Assert.Equal(174, areas[0].GetProperty("MaxX").GetInt32());
                Assert.Equal(99, areas[0].GetProperty("MaxY").GetInt32());
                // La clave legacy se conserva para provenance.
                Assert.Equal(400, treasures.RootElement.GetProperty("TreasureSectors")[0].GetInt32());
            }

            using (var objects = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(data, "objects.json"))))
            {
                var destination = objects.RootElement.GetProperty("Items")[0].GetProperty("DestinationGlobal");
                Assert.Equal(0, destination.GetProperty("FloorZ").GetInt32());
                Assert.Equal(49, destination.GetProperty("GlobalX").GetInt32());
                Assert.Equal(59, destination.GetProperty("GlobalY").GetInt32());
            }

            using (var quests = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(data, "quests.json"))))
            {
                Assert.Equal(0, quests.RootElement.GetProperty("Items")[0].GetProperty("PosFloorZ").GetInt32());
            }

            using (var npcs = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(data, "npcs.json"))))
            {
                var npc = npcs.RootElement.GetProperty("Items")[0];
                var city = npc.GetProperty("TransportCities")[0];
                Assert.Equal(84, city.GetProperty("Global").GetProperty("GlobalX").GetInt32());
                Assert.Equal(19, city.GetProperty("Global").GetProperty("GlobalY").GetInt32());
                var target = npc.GetProperty("TargetGlobal");
                Assert.Equal(79, target.GetProperty("GlobalX").GetInt32());
                Assert.Equal(5, target.GetProperty("GlobalY").GetInt32());
            }
        }
        finally
        {
            try { Directory.Delete(root.FullName, true); } catch (IOException) { }
        }
    }
}
