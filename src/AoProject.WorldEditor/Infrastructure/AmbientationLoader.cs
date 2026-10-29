namespace AoProject.WorldEditor.Infrastructure;

using AoProject.WorldEditor.Domain;

internal sealed class AmbientationLoader
{
    public IReadOnlyList<Ambientation> Load(string resourceRoot)
    {
        var path = Directory.EnumerateFiles(resourceRoot, "ambientacion.ini", SearchOption.AllDirectories).First();
        var ini = IniDocument.Load(path);
        var count = ini.GetInt("INIT", "AmbientacionTotales");
        var result = new List<Ambientation>(count);
        for (var i = 1; i <= count; i++)
        {
            var section = i.ToString();
            result.Add(new Ambientation
            {
                Name = ini.Get(section, "Nombre"),
                Type = checked((byte)ini.GetInt(section, "Tipo")),
                GraphicIndex = ini.GetInt(section, "GrhIndex")
            });
        }
        return result;
    }
}
