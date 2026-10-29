using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class SpecialWorldDataServiceTests
{
    [Fact]
    public void Reads_underworld_portals_without_assigning_them_to_surface_grid()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "[INIT]\nUnderworldMapPool=1\n[Portal1]\nSourceMap=10\nSourceX=2\nSourceY=3\nDestinationMap=20\nDestinationX=4\nDestinationY=5\n");
            var catalog = new SpecialWorldDataService().LoadUnderworld(path);
            var portal = Assert.Single(catalog.Portals);
            Assert.Equal((short)10, portal.SourceMap);
            Assert.Equal((short)20, portal.DestinationMap);
        }
        finally { File.Delete(path); }
    }
}
