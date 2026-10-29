using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>Read-only port of body and head index loading from modIndices.bas.</summary>
internal sealed class CharacterIndexLoader
{
    public HeadIndexData[] LoadHeads(string resourceRoot)
    {
        var path = Find(resourceRoot, "cabezas.ind");
        using var reader = new BinaryReader(File.OpenRead(path));
        _ = reader.ReadBytes(255);
        _ = reader.ReadInt32();
        _ = reader.ReadInt32();
        var count = reader.ReadInt16();
        if (count < 0)
            throw new InvalidDataException("Cantidad de cabezas invalida.");

        var result = new HeadIndexData[count + 1];
        for (var i = 1; i <= count; i++)
        {
            result[i] = new HeadIndexData();
            for (var direction = 1; direction <= 4; direction++)
                result[i].Head[direction].GraphicIndex = reader.ReadInt32();
        }
        return result;
    }

    public DirectionalAnimation[] LoadBodies(string resourceRoot)
    {
        var document = IniDocument.Load(Find(resourceRoot, "cuerpos.dat"));
        var count = document.GetInt("INIT", "NumBodies");
        if (count < 0)
            throw new InvalidDataException("Cantidad de cuerpos invalida.");

        var result = new DirectionalAnimation[count + 1];
        for (var i = 1; i <= count; i++)
        {
            var section = "BODY" + i;
            var body = new DirectionalAnimation
            {
                HeadOffset = new Position(
                    Short(document.GetInt(section, "HeadOffsetX")),
                    Short(document.GetInt(section, "HeadOffsetY")))
            };
            var standard = document.GetInt(section, "Std");
            if (standard == 0)
            {
                for (var direction = 1; direction <= 4; direction++)
                    body.Walk[direction].GraphicIndex = document.GetInt(section, "Walk" + direction);
            }
            else if (standard > 0)
            {
                // Standard bodies are atlas templates. Their generated GRH
                // records are created by the renderer/resource compiler once
                // the moldes.ini atlas geometry is connected.
                body.Walk[1].GraphicIndex = standard;
                body.Walk[2].GraphicIndex = standard;
                body.Walk[3].GraphicIndex = standard;
                body.Walk[4].GraphicIndex = standard;
            }
            result[i] = body;
        }
        return result;
    }

    public DirectionalAnimation[] LoadBodies(string resourceRoot, IList<GraphicData> graphics)
    {
        var molds = LoadMolds(resourceRoot);
        var document = IniDocument.Load(Find(resourceRoot, "cuerpos.dat"));
        var count = document.GetInt("INIT", "NumBodies");
        var result = new DirectionalAnimation[count + 1];
        for (var i = 1; i <= count; i++)
        {
            var section = "BODY" + i;
            var body = new DirectionalAnimation
            {
                HeadOffset = new Position(
                    Short(document.GetInt(section, "HeadOffsetX")),
                    Short(document.GetInt(section, "HeadOffsetY")))
            };
            var standard = document.GetInt(section, "Std");
            if (standard == 0)
            {
                for (var direction = 1; direction <= 4; direction++)
                    body.Walk[direction].GraphicIndex = document.GetInt(section, "Walk" + direction);
            }
                else
                {
                    if (standard >= molds.Count)
                        throw new InvalidDataException($"Molde de cuerpo fuera de rango: {standard}.");
                    GenerateMoldAnimation(body, molds[standard], document.GetInt(section, "FileNum"), graphics);
                }
            result[i] = body;
        }
        return result;
    }

    public DirectionalAnimation[] LoadHelmets(string resourceRoot)
    {
        var document = IniDocument.Load(Find(resourceRoot, "cascos.ini"));
        var count = document.GetInt("INIT", "NumHeads");
        var result = new DirectionalAnimation[count + 1];
        for (var i = 1; i <= count; i++)
        {
            var helmet = new DirectionalAnimation();
            var section = "HEAD" + i;
            for (var direction = 1; direction <= 4; direction++)
                helmet.Walk[direction].GraphicIndex = document.GetInt(section, "Head" + direction);
            result[i] = helmet;
        }
        return result;
    }

    public DirectionalAnimation[] LoadWeapons(string resourceRoot, IList<GraphicData> graphics) =>
        LoadEquipment(resourceRoot, "armas.dat", "INIT", "NumArmas", "ARMA", "Dir", graphics);

    public DirectionalAnimation[] LoadShields(string resourceRoot, IList<GraphicData> graphics) =>
        LoadEquipment(resourceRoot, "escudos.dat", "INIT", "NumEscudos", "ESC", "Dir", graphics);

    private DirectionalAnimation[] LoadEquipment(string resourceRoot, string fileName, string initSection,
        string countKey, string sectionPrefix, string directPrefix, IList<GraphicData> graphics)
    {
        var document = IniDocument.Load(Find(resourceRoot, fileName));
        var molds = LoadMolds(resourceRoot);
        var count = document.GetInt(initSection, countKey);
        var result = new DirectionalAnimation[count + 1];
        for (var i = 1; i <= count; i++)
        {
            var section = sectionPrefix + i;
            var item = new DirectionalAnimation();
            var standard = document.GetInt(section, "Std");
            if (standard > 0)
            {
                if (standard >= molds.Count)
                    throw new InvalidDataException($"Molde de equipamiento fuera de rango: {standard}.");
                GenerateMoldAnimation(item, molds[standard], document.GetInt(section, "FileNum"), graphics);
            }
            else
            {
                for (var direction = 1; direction <= 4; direction++)
                    item.Walk[direction].GraphicIndex = document.GetInt(section, directPrefix + direction);
            }
            result[i] = item;
        }
        return result;
    }

    public IReadOnlyList<BodyMold> LoadMolds(string resourceRoot)
    {
        var document = IniDocument.Load(Find(resourceRoot, "moldes.ini"));
        var count = document.GetInt("INIT", "Moldes");
        var result = new BodyMold[count + 1];
        for (var i = 1; i <= count; i++)
        {
            var section = "Molde" + i;
            result[i] = new BodyMold
            {
                X = document.GetInt(section, "X"),
                Y = document.GetInt(section, "Y"),
                Width = document.GetInt(section, "Width"),
                Height = document.GetInt(section, "Height"),
                DirectionFrameCounts = new[]
                {
                    0,
                    document.GetInt(section, "Dir1"),
                    document.GetInt(section, "Dir2"),
                    document.GetInt(section, "Dir3"),
                    document.GetInt(section, "Dir4")
                }
            };
        }
        return result;
    }

    private static void GenerateMoldAnimation(
        DirectionalAnimation body,
        BodyMold mold,
        int fileNumber,
        IList<GraphicData> graphics)
    {
        // VB6 BodiesHeading: SOUTH, NORTH, WEST, EAST.
        var headings = new[] { Heading.South, Heading.North, Heading.West, Heading.East };
        var x = mold.X;
        var y = mold.Y;
        for (var directionIndex = 0; directionIndex < headings.Length; directionIndex++)
        {
            var frameCount = mold.DirectionFrameCounts[directionIndex + 1];
            if (frameCount <= 0)
                throw new InvalidDataException("Un molde contiene una direccion sin frames.");

            var frameIndices = new int[frameCount];
            for (var frame = 0; frame < frameCount; frame++)
            {
                frameIndices[frame] = graphics.Count;
                graphics.Add(new GraphicData
                {
                    Active = true,
                    FileNumber = fileNumber,
                    SourceX = checked((short)x),
                    SourceY = checked((short)y),
                    PixelWidth = checked((short)mold.Width),
                    PixelHeight = checked((short)mold.Height),
                    TileWidth = mold.Width / 32f,
                    TileHeight = mold.Height / 32f,
                    Frames = [graphics.Count]
                });
                x += mold.Width;
            }

            graphics.Add(new GraphicData
            {
                Active = true,
                Speed = frameCount / 0.018f,
                Frames = frameIndices,
                PixelWidth = checked((short)mold.Width),
                PixelHeight = checked((short)mold.Height),
                TileWidth = mold.Width / 32f,
                TileHeight = mold.Height / 32f
            });
            body.Walk[(int)headings[directionIndex]].GraphicIndex = graphics.Count - 1;
            x = mold.X;
            y += mold.Height;
        }
    }

    private static string Find(string root, string fileName)
    {
        var path = Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
        return path ?? throw new FileNotFoundException($"No existe el indice {fileName}.");
    }

    private static short Short(int value) => value is >= short.MinValue and <= short.MaxValue
        ? (short)value
        : throw new InvalidDataException("Valor de offset fuera de rango.");
}
