using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AutoSaveTests
{
    [Fact]
    public void Auto_save_accepts_reference_interval_range_only()
    {
        var service = new AutoSaveService();
        service.Configure(10);
        Assert.True(service.Enabled);
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Configure(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Configure(121));
        service.Stop();
        Assert.False(service.Enabled);
    }
}
