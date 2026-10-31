using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapsToSectorsMigrationServiceTests
{
    [Fact]
    public void Migrates_csm_files_into_sector_directories()
    {
        var root = Path.Combine(Path.GetTempPath(), "maps-to-sectors-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "Mapas");
        var destination = Path.Combine(root, "sectors");
        Directory.CreateDirectory(source);
        try
        {
            File.WriteAllBytes(Path.Combine(source, "mapa12.csm"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(source, "mapa12.inf"), [4]);
            File.WriteAllBytes(Path.Combine(source, "mapa13.csm"), [5]);
            File.WriteAllText(Path.Combine(source, "README.txt"), "ignore");

            var result = new MapsToSectorsMigrationService().Migrate(source, destination);

            Assert.Equal(2, result.Migrated);
            Assert.Equal(0, result.Skipped);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(destination, "sector-12", "sector.csm")));
            Assert.Equal([4], File.ReadAllBytes(Path.Combine(destination, "sector-12", "sector.inf")));
            Assert.Equal([5], File.ReadAllBytes(Path.Combine(destination, "sector-13", "sector.csm")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
