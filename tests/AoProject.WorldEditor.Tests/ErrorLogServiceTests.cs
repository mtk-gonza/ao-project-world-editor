using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class ErrorLogServiceTests
{
    [Fact]
    public void Register_appends_a_timestamped_entry()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new ErrorLogService(root); log.Register(5, "prueba", "Test.Procedure", 12);
            var text = File.ReadAllText(log.FilePath);
            Assert.Contains("Test.Procedure", text); Assert.Contains("prueba", text); Assert.Contains("[line 12]", text);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
