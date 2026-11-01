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
                    [Tesoros]
                    CantidadMapas=1
                    Mapa1=16
                    TiposDeTesoros=1
                    Tesoro1=3140-2

                    [Regalos]
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
}
