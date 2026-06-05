using Windows.UI;

namespace FluentCourse.Runtime.Player;

/// <summary>Parses <c>#AARRGGBB</c> / <c>#RRGGBB</c>.</summary>
internal static class ColorParser
{
    public static Color Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Color.FromArgb(0xFF, 0, 0, 0);
        var t = s.Trim().TrimStart('#');
        return t.Length switch
        {
            6 => Color.FromArgb(0xFF, ByteAt(t, 0), ByteAt(t, 2), ByteAt(t, 4)),
            8 => Color.FromArgb(ByteAt(t, 0), ByteAt(t, 2), ByteAt(t, 4), ByteAt(t, 6)),
            _ => throw new FormatException($"Color '{s}' must be #RRGGBB or #AARRGGBB.")
        };

        static byte ByteAt(string s, int i) => Convert.ToByte(s.Substring(i, 2), 16);
    }
}
