using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal sealed class MapEntityEditingService
{
    public void SetObject(WorldSector map, IReadOnlyList<ObjectData> objects, int x, int y, short index, short amount = 1)
    {
        ValidateTile(map, x, y); ArgumentNullException.ThrowIfNull(objects);
        if (index < 0 || index >= objects.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var block = map.Blocks[x, y]; block.Object.Index = index; block.Object.Amount = index == 0 ? (short)0 : amount;
        block.ObjectGraphic = index == 0 ? default : new GraphicAnimation { GraphicIndex = objects[index].GraphicIndex };
        if (index != 0) block.Blocked = objects[index].DefaultBlockedMask;
        map.Info.Changed = true;
    }

    public void SetNpc(WorldSector map, IReadOnlyList<NpcData> npcs, int x, int y, short index)
    {
        ValidateTile(map, x, y); ArgumentNullException.ThrowIfNull(npcs);
        if (index < 0 || index >= npcs.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var block = map.Blocks[x, y]; block.NpcIndex = index; block.CharacterIndex = 0; map.Info.Changed = true;
    }

    public void SetTrigger(WorldSector map, int x, int y, short trigger)
    {
        ValidateTile(map, x, y); map.Blocks[x, y].Trigger = trigger; map.Info.Changed = true;
    }

    public void SetTransfer(WorldSector map, int x, int y, short destinationMap, short destinationX, short destinationY)
    {
        ValidateTile(map, x, y);
        if (destinationMap < 0 || destinationX is < 0 or > 100 || destinationY is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(destinationMap));
        var exit = map.Blocks[x, y].TileExit; exit.Map = destinationMap; exit.X = destinationMap == 0 ? (short)0 : destinationX; exit.Y = destinationMap == 0 ? (short)0 : destinationY;
        map.Info.Changed = true;
    }

    private static void ValidateTile(WorldSector map, int x, int y)
    {
        if (x < map.CsmSectorSize.XMin || x > map.CsmSectorSize.XMax ||
            y < map.CsmSectorSize.YMin || y > map.CsmSectorSize.YMax ||
            x is < 1 or > WorldSector.Width || y is < 1 or > WorldSector.Height)
            throw new ArgumentOutOfRangeException(nameof(x));
    }
}
