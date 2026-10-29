using System.Text;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class IniEditorTests
{
    [Fact]
    public void Updates_a_key_and_preserves_unknown_keys_comments_and_order()
    {
        var root = NewDirectory();
        try
        {
            var path = Path.Combine(root, "namemapa.dat");
            File.WriteAllLines(path,
            [
                "[NameMapa]",
                "; comentario legacy",
                "mapa391=Dungeon Dragon",
                "mapa391Desc=Zona peligrosa",
                "mapa75=Cercanias"
            ], Encoding.Latin1);

            var editor = IniEditor.Load(path, Encoding.Latin1);
            editor.Set("NameMapa", "mapa75", "Cercanias de Pantano");
            editor.Set("NameMapa", "mapa76", "Nuevo");
            editor.Save(path, Encoding.Latin1);

            var lines = File.ReadAllLines(path, Encoding.Latin1);
            Assert.Equal("[NameMapa]", lines[0]);
            Assert.Equal("; comentario legacy", lines[1]);
            Assert.Equal("mapa391=Dungeon Dragon", lines[2]);
            Assert.Equal("mapa391Desc=Zona peligrosa", lines[3]);
            Assert.Equal("mapa75=Cercanias de Pantano", lines[4]);
            Assert.Equal("mapa76=Nuevo", lines[5]);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Creates_a_missing_section_at_the_end()
    {
        var root = NewDirectory();
        try
        {
            var path = Path.Combine(root, "translados.ini");
            File.WriteAllLines(path, ["[mapa1]", "Abajo=2"], Encoding.Latin1);

            var editor = IniEditor.Load(path, Encoding.Latin1);
            editor.Set("mapa1", "Arriba", "5");
            editor.Set("mapa2", "Abajo", "3");
            editor.Save(path, Encoding.Latin1);

            var lines = File.ReadAllLines(path, Encoding.Latin1);
            Assert.Equal(new[] { "[mapa1]", "Abajo=2", "Arriba=5", "[mapa2]", "Abajo=3" }, lines);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Replaces_every_duplicate_of_a_key()
    {
        var root = NewDirectory();
        try
        {
            var path = Path.Combine(root, "translados.ini");
            File.WriteAllLines(path, ["[mapa1]", "Abajo=1", "Abajo=9"], Encoding.Latin1);

            var editor = IniEditor.Load(path, Encoding.Latin1);
            editor.Set("mapa1", "Abajo", "7");
            editor.Save(path, Encoding.Latin1);

            var lines = File.ReadAllLines(path, Encoding.Latin1);
            Assert.Equal(new[] { "[mapa1]", "Abajo=7", "Abajo=7" }, lines);
        }
        finally { TryDelete(root); }
    }

    private static string NewDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-ini-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string root)
    {
        try { Directory.Delete(root, true); } catch { }
    }
}
