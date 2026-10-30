namespace AoProject.WorldEditor.Infrastructure;

using System.Text;

internal sealed class IniDocument
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    public static IniDocument Load(string path) => Parse(ReadLegacyText(path));

    /// <summary>
    /// Legacy AO resources are mixed: newer files are UTF-8 while the original
    /// VB6 files are commonly Windows-1252. Reading every INI as UTF-8 silently
    /// replaces accented names and can make the editor disagree with the client
    /// even when numeric index fields still parse correctly.
    /// </summary>
    private static string[] ReadLegacyText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes).Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return utf8.GetString(bytes).Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        }
        catch (DecoderFallbackException)
        {
            // AO resource files use the Windows-1252/ANSI byte values for the
            // Spanish/Portuguese text used by the indexes. Latin-1 preserves
            // those byte values without requiring a code-page provider in .NET.
            return Encoding.Latin1.GetString(bytes)
                .Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        }
    }

    public static IniDocument Load(string path, Encoding encoding)
    {
        return Parse(File.ReadLines(path, encoding));
    }

    private static IniDocument Parse(IEnumerable<string> lines)
    {
        var document = new IniDocument();
        Dictionary<string, string>? section = null;
        foreach (var raw in lines)
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
