namespace AoProject.WorldEditor.Infrastructure;

using System.Text;

internal sealed class IniDocument
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    public static IniDocument Load(string path) => Load(path, Encoding.UTF8);

    public static IniDocument Load(string path, Encoding encoding)
    {
        var document = new IniDocument();
        Dictionary<string, string>? section = null;
        foreach (var raw in File.ReadLines(path, encoding))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('\''))
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim();
                section = document._sections.TryGetValue(name, out var existing)
                    ? existing
                    : document._sections[name] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            var separator = line.IndexOf('=');
            if (section is not null && separator > 0)
                section[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return document;
    }

    public string Get(string section, string key) =>
        _sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value)
            ? value
            : string.Empty;

    public bool HasSection(string section) => _sections.ContainsKey(section);

    public IReadOnlyCollection<string> Sections => _sections.Keys;

    public int GetInt(string section, string key) =>
        int.TryParse(Get(section, key), out var value) ? value : 0;
}
