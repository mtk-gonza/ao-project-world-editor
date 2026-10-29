using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class DelimitedFieldServiceTests
{
    [Fact]
    public void Read_returns_one_based_fields_and_the_trailing_remainder()
    {
        Assert.Equal("mapa", DelimitedFieldService.Read(1, "mapa|7|nombre", "|"));
        Assert.Equal("7", DelimitedFieldService.Read(2, "mapa|7|nombre", "|"));
        Assert.Equal("nombre", DelimitedFieldService.Read(3, "mapa|7|nombre", "|"));
        Assert.Equal("nombre", DelimitedFieldService.Read(9, "mapa|7|nombre", "|"));
    }

    [Fact]
    public void Count_matches_reference_semantics()
    {
        Assert.Equal(0, DelimitedFieldService.Count(string.Empty, '|'));
        Assert.Equal(1, DelimitedFieldService.Count("mapa", '|'));
        Assert.Equal(3, DelimitedFieldService.Count("mapa|7|nombre", '|'));
    }
}
