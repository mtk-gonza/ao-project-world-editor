using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

/// <summary>Deep snapshot used to undo one bulk editor command atomically.</summary>
internal sealed class WorldSectorSnapshot
{
    private readonly MapInfoSnapshot _info;
    private readonly MapEnvironmentSnapshot _environment;
    private readonly BlockSnapshot[,] _blocks = new BlockSnapshot[WorldSector.Width + 1, WorldSector.Height + 1];

    private WorldSectorSnapshot(WorldSector map)
    {
        _info = MapInfoSnapshot.Capture(map.Info);
        _environment = MapEnvironmentSnapshot.Capture(map.Environment);
        for (var x = 1; x <= WorldSector.Width; x++)
        for (var y = 1; y <= WorldSector.Height; y++)
            _blocks[x, y] = BlockSnapshot.Capture(map.Blocks[x, y]);
    }

    public static WorldSectorSnapshot Capture(WorldSector map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return new WorldSectorSnapshot(map);
    }

    public void Restore(WorldSector map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _info.Restore(map.Info);
        _environment.Restore(map.Environment);
        for (var x = 1; x <= WorldSector.Width; x++)
        for (var y = 1; y <= WorldSector.Height; y++)
            _blocks[x, y].Restore(map.Blocks[x, y]);
        map.Info.Changed = true;
    }

    private sealed class MapInfoSnapshot
    {
        private readonly string _music, _ambient, _exit, _name, _terrain, _zone, _restriction, _light;
        private readonly int _musicHigh, _musicLow, _letterGraphic, _level, _extra2;
        private readonly short _mapVersion;
        private readonly bool _playerKilling, _changed;
        private readonly byte _magic, _invisibility, _resurrection, _noEncrypt, _backup, _safety;

        private MapInfoSnapshot(SectorInfo info)
        {
            _music = info.Music; _musicHigh = info.MusicNumberHigh; _musicLow = info.MusicNumberLow;
            _ambient = info.Ambient; _exit = info.Exit; _letterGraphic = info.LetterGraphic;
            _level = info.Level; _extra2 = info.Extra2; _safety = info.Safety;
            _name = info.Name; _terrain = info.Terrain;
            _zone = info.Zone; _restriction = info.Restriction; _light = info.Light;
            _mapVersion = info.MapVersion; _playerKilling = info.PlayerKilling; _changed = info.Changed;
            _magic = info.MagicDisabled; _invisibility = info.InvisibilityDisabled;
            _resurrection = info.ResurrectionDisabled; _noEncrypt = info.NoEncryptMap; _backup = info.Backup;
        }

        public static MapInfoSnapshot Capture(SectorInfo info) => new(info);
        public void Restore(SectorInfo info)
        {
            info.Music = _music; info.MusicNumberHigh = _musicHigh; info.MusicNumberLow = _musicLow;
            info.Ambient = _ambient; info.Exit = _exit; info.LetterGraphic = _letterGraphic;
            info.Level = _level; info.Extra2 = _extra2; info.Safety = _safety;
            info.Name = _name; info.Terrain = _terrain; info.Zone = _zone;
            info.Restriction = _restriction; info.Light = _light; info.MapVersion = _mapVersion;
            info.PlayerKilling = _playerKilling; info.Changed = _changed; info.MagicDisabled = _magic;
            info.InvisibilityDisabled = _invisibility; info.ResurrectionDisabled = _resurrection;
            info.NoEncryptMap = _noEncrypt; info.Backup = _backup;
        }
    }

    private readonly record struct MapEnvironmentSnapshot(byte Rain, byte Snow, byte Fog, int BaseLight)
    {
        public static MapEnvironmentSnapshot Capture(MapEnvironment environment) =>
            new(environment.Rain, environment.Snow, environment.Fog, environment.BaseLight);
        public void Restore(MapEnvironment environment)
        {
            environment.Rain = Rain; environment.Snow = Snow; environment.Fog = Fog; environment.BaseLight = BaseLight;
        }
    }

    private sealed class BlockSnapshot
    {
        private readonly GraphicAnimation[] _graphics;
        private readonly GraphicAnimation _objectGraphic;
        private readonly int[] _lightValues, _colors;
        private readonly LightDataSnapshot _light;
        private readonly ObjectSnapshot _object;
        private readonly WorldPositionSnapshot _exit;
        private readonly short _character, _particleGroup, _npc;
        private readonly int _particle;
        private readonly bool _marked;
        private readonly byte _blocked, _red, _green, _blue;
        private readonly short _trigger;

        private BlockSnapshot(SectorTile block)
        {
            _graphics = (GraphicAnimation[])block.Graphics.Clone();
            _objectGraphic = block.ObjectGraphic;
            _lightValues = (int[])block.LightValues.Clone(); _colors = (int[])block.Colors.Clone();
            _light = new(block.Light.Range, block.Light.Color);
            _object = new(block.Object.Index, block.Object.Amount);
            _exit = new(block.TileExit.SectorId, block.TileExit.X, block.TileExit.Y);
            _character = block.CharacterIndex; _particleGroup = block.ParticleGroup; _npc = block.NpcIndex;
            _particle = block.ParticleIndex; _marked = block.Marked; _blocked = block.Blocked;
            _red = block.Red; _green = block.Green; _blue = block.Blue; _trigger = block.Trigger;
        }

        public static BlockSnapshot Capture(SectorTile block) => new(block);
        public void Restore(SectorTile block)
        {
            Array.Copy(_graphics, block.Graphics, _graphics.Length); block.ObjectGraphic = _objectGraphic;
            Array.Copy(_lightValues, block.LightValues, _lightValues.Length); Array.Copy(_colors, block.Colors, _colors.Length);
            block.Light.Range = _light.Range; block.Light.Color = _light.Color;
            block.Object.Index = _object.Index; block.Object.Amount = _object.Amount;
            block.TileExit.SectorId = _exit.Map; block.TileExit.X = _exit.X; block.TileExit.Y = _exit.Y;
            block.CharacterIndex = _character; block.ParticleGroup = _particleGroup; block.NpcIndex = _npc;
            block.ParticleIndex = _particle; block.Marked = _marked; block.Blocked = _blocked;
            block.Red = _red; block.Green = _green; block.Blue = _blue; block.Trigger = _trigger;
        }
    }

    private readonly record struct LightDataSnapshot(short Range, int Color);
    private readonly record struct ObjectSnapshot(short Index, short Amount);
    private readonly record struct WorldPositionSnapshot(short Map, short X, short Y);
}
