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
