using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class CharacterListTests
{
    [Fact]
    public void Character_list_supports_make_move_and_erase()
    {
        var list = new CharacterListService();
        var entry = list.Make(1, 2, 3, (byte)Heading.North, 10, 10);
        Assert.True(entry.Active); Assert.Equal(10, entry.X);
        list.MoveCharbyHead(1, (byte)Heading.East);
        Assert.Equal(11, entry.X); Assert.Equal(10, entry.Y);
        list.MoveCharbyPos(1, 40, 41);
        Assert.Equal(40, entry.X); Assert.Equal(41, entry.Y);
        list.Erase(1);
        Assert.False(list.Characters[0].Active);
        Assert.Equal(1, list.NextOpen());
    }

    [Fact]
    public void Refresh_rebuilds_the_list_from_map_npcs()
    {
        var map = new WorldSector();
        map.Blocks[5, 6].NpcIndex = 1;
        map.Blocks[7, 8].NpcIndex = 1;
        var npcs = new[] { new NpcData(), new NpcData { Body = 4, Head = 5, Heading = 3 } };
        var created = new CharacterListService().RefreshAll(map, npcs);
        Assert.Equal(2, created);
    }
}
