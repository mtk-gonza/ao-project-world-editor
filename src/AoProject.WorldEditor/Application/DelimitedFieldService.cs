namespace AoProject.WorldEditor.Application;

/// <summary>
/// Port of modGeneral.ReadField, Module1.General_Field_Read and
/// Module1.General_Field_Count. Field positions are 1-based as in VB6.
/// </summary>
internal static class DelimitedFieldService
{
    public static string Read(int position, string text, string delimiter)
    {
        if (position < 1) throw new ArgumentOutOfRangeException(nameof(position));
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(delimiter);
        if (delimiter.Length == 0) return text;

        var last = 0;
        var current = 0;
        for (var field = 1; field <= position; field++)
        {
            last = current;
            current = IndexOfFromVbPosition(text, last + 1, delimiter);
        }

        return current == 0
            ? text[Math.Min(last, text.Length)..]
            : text.Substring(last, current - last - 1);
    }

    public static string Read(int position, string text, char separator) => Read(position, text, separator.ToString());

    public static int Count(string text, char delimiter)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) return 0;
        return text.Count(character => character == delimiter) + 1;
    }

    private static int IndexOfFromVbPosition(string text, int vbPosition, string delimiter)
    {
        if (vbPosition > text.Length) return 0;
        var index = text.IndexOf(delimiter, vbPosition - 1, StringComparison.Ordinal);
        return index < 0 ? 0 : index + 1;
    }
}
