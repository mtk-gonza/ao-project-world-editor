using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapNavigationTests
{
    [Fact]
    public void Navigation_resolves_existing_transfer_target_and_rejects_missing_target()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-navigation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var current = Path.Combine(root, "mapa1.csm"); var target = Path.Combine(root, "mapa7.csm"); File.WriteAllBytes(target, []);
            var transfer = new WorldPosition { SectorId = 7, X = 20, Y = 21 };
            var service = new MapNavigationService();
            Assert.Equal(target, service.ResolveTransferPath(current, transfer));
            Assert.Null(service.ResolveTransferPath(current, new WorldPosition { SectorId = 8, X = 20, Y = 21 }));
        }
        finally { Directory.Delete(root, true); }
    }
}
