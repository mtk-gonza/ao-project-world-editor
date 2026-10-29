using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GameIniServiceTests
{
    [Fact]
    public void Configuration_round_trips_through_inicio_con()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-gameini-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "Inicio.con");
            var configuration = new GameIniConfiguration
            {
                Port = 7666, Music = 1, Fx = 1, Tip = 2, Password = "secret", Name = "Noland",
                GraphicsDirectory = "Graficos", SoundsDirectory = "Sonidos", MusicDirectory = "Musica",
                MapsDirectory = "Mapas", BitmapCount = 49488, MapCount = 318
            };
            var service = new GameIniService();
            service.Escribir(path, configuration, service.IniciarCabecera(12, 34));

            var loaded = service.Leer(path);
            Assert.Equal(7666, loaded.Port); Assert.Equal("Noland", loaded.Name);
            Assert.Equal("Mapas", loaded.MapsDirectory); Assert.Equal(49488, loaded.BitmapCount); Assert.Equal((short)318, loaded.MapCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
