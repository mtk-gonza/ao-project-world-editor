using System.Text;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>
/// Small INI editor with <c>WritePrivateProfileString</c> semantics: it edits an
/// existing file in place, updating only the requested keys and preserving every
/// other line (unknown keys, comments and section order). The legacy editor wrote
/// <c>NameMapa.dat</c> and <c>translados.ini</c> that way, which is why the
/// <c>mapa&lt;id&gt;Desc</c> entries survived a save.
/// </summary>
internal sealed class IniEditor
{
    private readonly List<string> _lines;

    private IniEditor(List<string> lines) => _lines = lines;

    public static IniEditor Create() => new([]);

    public static IniEditor Load(string path, Encoding encoding) =>
        new(new List<string>(File.ReadAllLines(path, encoding)));

    public static IniEditor LoadOrCreate(string path, Encoding encoding) =>
        File.Exists(path) ? Load(path, encoding) : Create();

    public int LineCount => _lines.Count;

    public void Set(string section, string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var line = $"{key}={value}";
        var header = FindSection(section);
        if (header < 0)
        {
            _lines.Add($"[{section}]");
            _lines.Add(line);
            return;
        }

        var end = FindSectionEnd(header);
        var replaced = false;
        for (var index = header + 1; index < end; index++)
        {
            if (!TryParseKey(_lines[index], out var existing) ||
                !existing.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            _lines[index] = line;
            replaced = true;
        }
        if (replaced) return;

        var insert = end;
        while (insert > header + 1 && _lines[insert - 1].Trim().Length == 0) insert--;
        _lines.Insert(insert, line);
    }

    public string Get(string section, string key, string fallback = "")
    {
        var header = FindSection(section);
        if (header < 0) return fallback;
        var end = FindSectionEnd(header);
        var result = fallback;
        for (var index = header + 1; index < end; index++)
            if (TryParseKey(_lines[index], out var existing) &&
                existing.Equals(key, StringComparison.OrdinalIgnoreCase))
                result = ValueOf(_lines[index]);
        return result;
    }

    public void Save(string path, Encoding encoding)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var text = _lines.Count == 0 ? string.Empty : string.Join("\r\n", _lines) + "\r\n";
        File.WriteAllText(path, text, encoding);
    }

    private int FindSection(string section)
    {
        for (var index = 0; index < _lines.Count; index++)
            if (TryParseSection(_lines[index], out var name) &&
                name.Equals(section, StringComparison.OrdinalIgnoreCase))
                return index;
        return -1;
    }

    private int FindSectionEnd(int header)
    {
        for (var index = header + 1; index < _lines.Count; index++)
            if (TryParseSection(_lines[index], out _)) return index;
        return _lines.Count;
    }

    private static bool TryParseSection(string raw, out string name)
    {
        var line = raw.Trim();
        if (line.Length > 2 && line[0] == '[' && line[^1] == ']')
        {
            name = line[1..^1].Trim();
            return true;
        }
        name = string.Empty;
        return false;
    }

    private static bool TryParseKey(string raw, out string key)
    {
        var line = raw.Trim();
        if (line.Length == 0 || line[0] is ';' or '[')
        {
            key = string.Empty;
            return false;
        }
        var separator = line.IndexOf('=');
        if (separator <= 0)
        {
            key = string.Empty;
            return false;
        }
        key = line[..separator].Trim();
        return key.Length > 0;
    }

    private static string ValueOf(string raw)
    {
        var line = raw.Trim();
        var separator = line.IndexOf('=');
        return separator < 0 ? string.Empty : line[(separator + 1)..].Trim();
    }
}
