using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed class CharacterEntry
{
    public int Index { get; set; }
    public short Body { get; set; }
    public short Head { get; set; }
    public byte Heading { get; set; }
    public short X { get; set; }
    public short Y { get; set; }
    public short Weapon { get; set; }
    public short Shield { get; set; }
    public short Helmet { get; set; }
    public bool Active { get; set; }
    public short GrhIndex { get; set; }
    public bool HasFx { get; set; }
}

/// <summary>
/// Port of the VB6 character preview list used by the editor
/// (MakeChar, EraseChar, ResetCharInfo, NextOpenChar, MoveCharbyHead,
/// MoveCharbyPos and RefreshAllChars).
/// </summary>
internal sealed class CharacterListService
{
    private readonly List<CharacterEntry> _characters = [];

    public IReadOnlyList<CharacterEntry> Characters => _characters;

    public CharacterEntry Make(int index, short body, short head, byte heading, short x, short y,
        short weapon = 0, short shield = 0, short helmet = 0)
    {
        if (index < 1) throw new ArgumentOutOfRangeException(nameof(index));
        while (_characters.Count < index) _characters.Add(new CharacterEntry { Index = _characters.Count + 1 });
        var entry = _characters[index - 1];
        entry.Index = index; entry.Body = body; entry.Head = head; entry.Heading = heading;
        entry.Weapon = weapon; entry.Shield = shield; entry.Helmet = helmet;
        entry.X = x; entry.Y = y; entry.Active = true; entry.HasFx = false;
        return entry;
    }

    public void Erase(int index)
    {
        if (index < 1 || index > _characters.Count) return;
        _characters[index - 1] = new CharacterEntry { Index = index };
    }

    public void ResetCharInfo(int index)
    {
        if (index < 1 || index > _characters.Count) return;
        var entry = _characters[index - 1];
        entry.GrhIndex = 0; entry.HasFx = false;
    }

    public int NextOpen()
    {
        for (var index = 0; index < _characters.Count; index++)
            if (!_characters[index].Active) return index + 1;
        return _characters.Count + 1;
    }

    public void MoveCharbyHead(int index, byte heading)
    {
        if (index < 1 || index > _characters.Count) return;
        var entry = _characters[index - 1];
        entry.Heading = heading;
        var (dx, dy) = Delta(heading);
        entry.X = (short)Math.Clamp(entry.X + dx, 1, WorldSector.Width);
        entry.Y = (short)Math.Clamp(entry.Y + dy, 1, WorldSector.Height);
    }

    public void MoveCharbyPos(int index, short x, short y)
    {
        if (index < 1 || index > _characters.Count) return;
        if (x is < 1 or > WorldSector.Width || y is < 1 or > WorldSector.Height) return;
        var entry = _characters[index - 1];
        entry.X = x; entry.Y = y;
    }

    public bool Check(int index) => index >= 1 && index <= _characters.Count && _characters[index - 1].Active;

    public int RefreshAll(WorldSector map, IReadOnlyList<NpcData> npcs)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(npcs);
        _characters.Clear();
        var created = 0;
        var minX = Math.Clamp((int)map.CsmSectorSize.XMin, 1, WorldSector.Width);
        var maxX = Math.Clamp((int)map.CsmSectorSize.XMax, 1, WorldSector.Width);
        var minY = Math.Clamp((int)map.CsmSectorSize.YMin, 1, WorldSector.Height);
        var maxY = Math.Clamp((int)map.CsmSectorSize.YMax, 1, WorldSector.Height);
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            var index = map.Blocks[x, y].NpcIndex;
            if (index <= 0 || index >= npcs.Count) continue;
            var npc = npcs[index];
            Make(++created, npc.Body, npc.Head, npc.Heading, (short)x, (short)y,
                npc.Weapon, npc.Shield, npc.Helmet);
        }
        return created;
    }

    private static (short Dx, short Dy) Delta(byte heading) => heading switch
    {
        (byte)Heading.North => (0, -1),
        (byte)Heading.East => (1, 0),
        (byte)Heading.South => (0, 1),
        (byte)Heading.West => (-1, 0),
        _ => (0, 0)
    };
}
