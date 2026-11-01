using System.Text;
using System.Text.Json;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

/// <summary>
/// Catálogos canónicos del lote 3: rangos y recompensas de facción, listas de
/// crafteo, equipamiento inicial y pesca.
/// </summary>
public sealed class RuntimeCatalogLote3Tests
{
    [Fact]
    public void Runtime_data_converts_legacy_faction_ranks_to_versioned_json()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-factionranks-");
        try
        {
            var bundle = Bundle(b => b.FactionRanks = Encoding.UTF8.GetBytes("""
                    [INIT]
                    NumRangos=2

                    [ArmadaReal]
                    1Rango=23-500-Recluta Real
                    2Rango=26-1500-Soldado Real

                    [LegionCaos]
                    1Rango=23-500-Iniciado Oscuro
                    2Rango=26-1500-Acolito Impio
                    """));

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(root.FullName, "runtime-data", "factionranks.json")));
            var element = document.RootElement;
            Assert.Equal(1, element.GetProperty("Schema").GetInt32());
            Assert.Equal("factionranks", element.GetProperty("Catalog").GetString());

            var royal = element.GetProperty("Royal");
            Assert.Equal(2, royal.GetArrayLength());
            Assert.Equal(1, royal[0].GetProperty("Rank").GetInt32());
            Assert.Equal(23, royal[0].GetProperty("RequiredLevel").GetInt32());
            Assert.Equal(500, royal[0].GetProperty("RequiredScore").GetInt32());
            Assert.Equal("Recluta Real", royal[0].GetProperty("Title").GetString());
            Assert.Equal("Soldado Real", royal[1].GetProperty("Title").GetString());
            Assert.Equal("Acolito Impio", element.GetProperty("Chaos")[1].GetProperty("Title").GetString());
        }
        finally
        {
            TryDelete(root.FullName);
        }
    }

    [Fact]
    public void Runtime_data_converts_legacy_faction_rewards_to_versioned_json()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-factionrewards-");
        try
        {
            var bundle = Bundle(b => b.FactionRewards = Encoding.UTF8.GetBytes("""
                    [INIT]
                    NumRecompensas=2

                    [Recompensas]
                    Recompensa1=1-1234
                    Recompensa2=3-5678
                    """));

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(root.FullName, "runtime-data", "factionrewards.json")));
            var rewards = document.RootElement.GetProperty("Rewards");
            Assert.Equal("factionrewards", document.RootElement.GetProperty("Catalog").GetString());
            Assert.Equal(2, rewards.GetArrayLength());
            Assert.Equal(1, rewards[0].GetProperty("Rank").GetInt32());
            Assert.Equal(1234, rewards[0].GetProperty("ObjectIndex").GetInt32());
            Assert.Equal(5678, rewards[1].GetProperty("ObjectIndex").GetInt32());
        }
        finally
        {
            TryDelete(root.FullName);
        }
    }

    [Fact]
    public void Runtime_data_converts_legacy_crafting_lists_to_versioned_json()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-crafting-");
        try
        {
            var bundle = Bundle(b => b.CraftingLists = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["ArmasHerrero.dat"] = Encoding.UTF8.GetBytes("[INIT]\nNumArmas=2\n\n[Arma1]\nIndex=3980 'Bala de piedra\n\n[Arma2]\nIndex=165 'Daga +1\n"),
                ["ArmadurasHerrero.dat"] = Encoding.UTF8.GetBytes("[INIT]\nNumArmaduras=1\n\n[Armadura1] 'Relicario\nIndex=3769\n"),
                ["BlackSmithElementalRunes.dat"] = Encoding.UTF8.GetBytes("[INIT]\nNumRunas=1\n\n[Runa1] 'fuego\nIndex=5170\n"),
                ["ObjCarpintero.dat"] = Encoding.UTF8.GetBytes("[INIT]\nNumObjs=1\n\n[OBJ1] 'Cuchara\nIndex=163\n"),
                ["ObjAlquimista.dat"] = Encoding.UTF8.GetBytes("[INIT]\nNumObjs=1\n\n[OBJ1]\nIndex=5997\n"),
                ["ObjSastre.dat"] = Encoding.UTF8.GetBytes("[INIT]\nNumObjs=1\n\n[Obj1]\nIndex=3578\n")
            });

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(root.FullName, "runtime-data", "craftinglists.json")));
            var element = document.RootElement;
            Assert.Equal("craftinglists", element.GetProperty("Catalog").GetString());
            Assert.Equal(3980, element.GetProperty("BlacksmithWeapons")[0].GetInt32());
            Assert.Equal(165, element.GetProperty("BlacksmithWeapons")[1].GetInt32());
            Assert.Equal(3769, element.GetProperty("BlacksmithArmors")[0].GetInt32());
            Assert.Equal(5170, element.GetProperty("BlacksmithElementalRunes")[0].GetInt32());
            Assert.Equal(163, element.GetProperty("CarpenterObjects")[0].GetInt32());
            Assert.Equal(5997, element.GetProperty("AlchemyObjects")[0].GetInt32());
            Assert.Equal(3578, element.GetProperty("TailorObjects")[0].GetInt32());
        }
        finally
        {
            TryDelete(root.FullName);
        }
    }

    [Fact]
    public void Runtime_data_converts_legacy_initial_equipment_to_versioned_json()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-initialequipment-");
        try
        {
            var bundle = Bundle(b => b.InitialEquipment = Encoding.UTF8.GetBytes("""
                    [COMMON]
                    NumObjs=1
                    Obj1=4335,500,0

                    ; Mage = 1
                    [CLASS1]
                    NumObjs=2
                    Obj1=3495,1,0
                    Obj2=3502,1,1
                    NumSpells=3
                    Spell1=1
                    Spell2=7
                    Spell3=352
                    """));

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(root.FullName, "runtime-data", "initialequipment.json")));
            var element = document.RootElement;
            Assert.Equal("initialequipment", element.GetProperty("Catalog").GetString());

            var common = element.GetProperty("Common");
            Assert.Equal(1, common.GetArrayLength());
            Assert.Equal(4335, common[0].GetProperty("Index").GetInt32());
            Assert.Equal(500, common[0].GetProperty("Amount").GetInt32());
            Assert.False(common[0].GetProperty("Equipped").GetBoolean());

            var characterClass = Assert.Single(element.GetProperty("Classes").EnumerateArray());
            Assert.Equal(1, characterClass.GetProperty("Class").GetInt32());
            var items = characterClass.GetProperty("Items");
            Assert.Equal(2, items.GetArrayLength());
            Assert.True(items[1].GetProperty("Equipped").GetBoolean());
            var spells = characterClass.GetProperty("Spells");
            Assert.Equal(new[] { 1, 7, 352 }, spells.EnumerateArray().Select(value => value.GetInt32()));
        }
        finally
        {
            TryDelete(root.FullName);
        }
    }

    [Fact]
    public void Runtime_data_converts_legacy_fishing_to_versioned_json()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-fishing-");
        try
        {
            var pesca = Encoding.UTF8.GetBytes("""
                [PECES]
                NumPeces=3
                Maxlvlcaña=2
                Pez1=2089-3000-1-0        'Pez Lisa
                Pez2=3316-23-2-1
                Pez3=2153-100-9-0

                [POWERCANAS]
                Power1=2
                Power2=4

                [UNIQUEMAPFISH]
                UniqueMapfish1=2152
                UniqueMapfish2=3304
                UniqueMapfish3=
                """);
            var recursos = Encoding.UTF8.GetBytes("[Pesca]\nItems=1\nItem1=372-150000\n");
            var bundle = Bundle(b => b.Fishing = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["pesca.dat"] = pesca,
                ["RecursosEspeciales.dat"] = recursos
            });

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(root.FullName, "runtime-data", "fishing.json")));
            var element = document.RootElement;
            Assert.Equal("fishing", element.GetProperty("Catalog").GetString());
            Assert.Equal(2, element.GetProperty("MaxRodLevel").GetInt32());
            Assert.Equal(new[] { 0, 2, 4 }, element.GetProperty("RodPowerByLevel").EnumerateArray().Select(v => v.GetInt32()));

            var fish = element.GetProperty("Fish");
            Assert.Equal(3, fish.GetArrayLength());
            Assert.Equal(2089, fish[0].GetProperty("ObjectIndex").GetInt32());
            Assert.Equal(3000, fish[0].GetProperty("Weight").GetInt32());
            Assert.False(fish[0].GetProperty("IsSpecial").GetBoolean());
            Assert.True(fish[1].GetProperty("IsSpecial").GetBoolean());
            // El nivel de caña se clampa a Maxlvlcaña.
            Assert.Equal(2, fish[2].GetProperty("RodLevel").GetInt32());
            Assert.Equal(new[] { 2152, 3304 }, element.GetProperty("UniqueMapFish").EnumerateArray().Select(v => v.GetInt32()));

            var reward = Assert.Single(element.GetProperty("SpecialRewards").EnumerateArray());
            Assert.Equal(372, reward.GetProperty("ObjectIndex").GetInt32());
            Assert.Equal(150000, reward.GetProperty("Probability").GetInt32());
        }
        finally
        {
            TryDelete(root.FullName);
        }
    }

    private static UnifiedWorldBundle Bundle(Action<UnifiedWorldBundle> configure)
    {
        var bundle = new UnifiedWorldBundle
        {
            Events = "# events"u8.ToArray(),
            Treasures = "# treasures"u8.ToArray(),
            Quests = "# quests"u8.ToArray(),
            Npcs = "# npcs"u8.ToArray(),
            Objects = "# objects"u8.ToArray(),
            Castles = "[]"u8.ToArray(),
            Spells = "# spells"u8.ToArray(),
            EffectsOverTime = "# eot"u8.ToArray(),
            GlobalQuests = "# global quests"u8.ToArray(),
            Scenarios = new Dictionary<string, byte[]>()
        };
        configure(bundle);
        return bundle;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, true); } catch (IOException) { }
    }
}
