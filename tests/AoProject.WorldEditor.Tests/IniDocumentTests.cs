using AoProject.WorldEditor.Infrastructure;
using System.Text;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class IniDocumentTests
{
    [Fact]
    public void Keys_are_read_with_sections_and_case_insensitive_names()
    {
        var path = Path.Combine(Path.GetTempPath(), "ao-ini-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(path, "[PATH]\r\nUltimoMapa=C:\\mapas\\mapa1.csm\r\n\r\n[INDEX]\r\nMaxGrhs=49488\r\n");
            var ini = IniDocument.Load(path);
            Assert.Equal("C:\\mapas\\mapa1.csm", ini.Get("path", "ultimomapa"));
            Assert.Equal(49488, ini.GetInt("INDEX", "MaxGrhs"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Utf8_bom_does_not_break_the_first_section()
    {
        var path = Path.Combine(Path.GetTempPath(), "ao-ini-bom-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(path, "[PATH]\r\nUltimoMapa=mapa1.csm\r\n", new System.Text.UTF8Encoding(true));
            Assert.Equal("mapa1.csm", IniDocument.Load(path).Get("PATH", "UltimoMapa"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Legacy_ansi_resources_are_decoded_as_windows1252()
    {
        var path = Path.Combine(Path.GetTempPath(), "ao-ini-ansi-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllBytes(path, Encoding.Latin1.GetBytes(
                "[OBJ1]\r\nName=Árbol ñ\r\n"));
            Assert.Equal("Árbol ñ", IniDocument.Load(path).Get("OBJ1", "Name"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
