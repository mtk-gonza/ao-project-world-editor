using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>Read-only port of the non-graphical loaders in modIndices.bas.</summary>
internal sealed class ReferenceIndexLoader
{
    /// <summary>
    /// Raiz secundaria para los indices legacy. El arbol generado ya no lleva
    /// <c>Dat/</c>: los <c>.dat</c> canonicos viven en <c>reference/Recursos</c>,
    /// que es la fuente del importador.
    /// </summary>
    private readonly string? fallbackRoot;

    public ReferenceIndexLoader(string? fallbackRoot = null) => this.fallbackRoot = fallbackRoot;

    public IReadOnlyList<SurfaceData> LoadSurfaces(string resourceRoot)
    {
        var document = IniDocument.Load(Find(resourceRoot, "indices.ini"));
        var count = document.GetInt("INIT", "Referencias");
        var result = new SurfaceData[count + 1];
        for (var index = 0; index <= count; index++)
        {
            var section = "REFERENCIA" + index;
            result[index] = new SurfaceData
            {
                Name = document.Get(section, "Nombre"),
                GraphicIndex = document.GetInt(section, "GrhIndice"),
                Width = Byte(document.GetInt(section, "Ancho")),
                Height = Byte(document.GetInt(section, "Alto")),
                Blocked = document.GetInt(section, "Bloquear") == 1,
                Layer = Byte(document.GetInt(section, "Capa"))
            };
        }
        return result;
    }

    public IReadOnlyList<ObjectData> LoadObjects(string resourceRoot)
    {
        var document = IniDocument.Load(Find(resourceRoot, "OBJ.dat"));
        var count = document.GetInt("INIT", "NumOBJs");
        var result = new ObjectData[count + 1];
        for (var index = 1; index <= count; index++)
        {
            var section = "OBJ" + index;
            result[index] = new ObjectData
            {
                Name = document.Get(section, "Name"),
                GraphicIndex = document.GetInt(section, "GrhIndex"),
                ObjectType = Short(document.GetInt(section, "ObjType")),
                ClothingIndex = Short(document.GetInt(section, "NumRopaje")),
                Info = document.Get(section, "Info"),
                WeaponAnimation = Short(document.GetInt(section, "Anim")),
                Text = document.Get(section, "Texto"),
                SecondaryGraphicIndex = Short(document.GetInt(section, "GrhSec")),
                Closed = Byte(document.GetInt(section, "Cerrada")),
                Subtype = Byte(document.GetInt(section, "Subtipo"))
                ,DefaultBlockedMask = ReadMask(document, section)
            };
        }
        return result;
    }

    public IReadOnlyList<NpcData> LoadNpcs(string resourceRoot)
    {
        var document = IniDocument.Load(Find(resourceRoot, "NPCs.dat"));
        var count = document.GetInt("INIT", "NumNPCs");
        var result = new NpcData[count + 1];
        for (var index = 1; index <= count; index++)
        {
            var section = "NPC" + index;
            result[index] = new NpcData
            {
                Name = document.Get(section, "Name"),
                Body = Short(document.GetInt(section, "Body")),
                Head = Short(document.GetInt(section, "Head")),
                Heading = Byte(document.GetInt(section, "Heading")),
                Weapon = Short(document.GetInt(section, "Arma")),
                Shield = Short(document.GetInt(section, "Escudo")),
                Helmet = Short(document.GetInt(section, "Casco"))
            };
        }
        return result;
    }

    public IReadOnlyList<TriggerData> LoadTriggers(string resourceRoot)
    {
        var document = IniDocument.Load(Find(resourceRoot, "triggers.ini"));
        var count = document.GetInt("INIT", "NumTriggers");
        var result = new List<TriggerData>(count);
        for (var index = 1; index <= count; index++)
            result.Add(new TriggerData { Index = index - 1, Name = document.Get("Trig" + index, "Name") });
        return result;
    }

    private string Find(string root, string fileName)
    {
        var path = FindIn(root, fileName) ?? (fallbackRoot is null ? null : FindIn(fallbackRoot, fileName));
        return path ?? throw new FileNotFoundException($"No existe el indice {fileName}.");
    }

    private static string? FindIn(string root, string fileName)
    {
        if (!Directory.Exists(root))
            return null;
        return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static byte Byte(int value) => value is >= byte.MinValue and <= byte.MaxValue ? (byte)value : throw new InvalidDataException("Valor byte fuera de rango.");
    private static short Short(int value) => value is >= short.MinValue and <= short.MaxValue ? (short)value : throw new InvalidDataException("Valor short fuera de rango.");
    private static byte ReadMask(IniDocument document, string section)
    {
        var value = document.GetInt(section, "BlockedMask");
        if (value == 0) value = document.GetInt(section, "Bloqueo");
        return value is >= 0 and <= 15 ? (byte)value : throw new InvalidDataException("DefaultBlockedMask fuera de rango.");
    }
}
