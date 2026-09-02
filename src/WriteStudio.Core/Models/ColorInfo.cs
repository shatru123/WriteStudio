namespace WriteStudio.Core.Models;

/// <summary>
/// Represents RGBA color information for vector strokes and canvas backgrounds.
/// </summary>
public class ColorInfo
{
    public byte R { get; set; } = 0;
    public byte G { get; set; } = 0;
    public byte B { get; set; } = 0;
    public byte A { get; set; } = 255;

    public ColorInfo() { }

    public ColorInfo(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public static ColorInfo Black => new(0, 0, 0, 255);
    public static ColorInfo White => new(255, 255, 255, 255);
    public static ColorInfo Red => new(235, 52, 52, 255);
    public static ColorInfo Blue => new(52, 131, 235, 255);
    public static ColorInfo Green => new(46, 184, 92, 255);
    public static ColorInfo Yellow => new(245, 197, 24, 255);
    public static ColorInfo Orange => new(245, 130, 32, 255);
    public static ColorInfo Purple => new(155, 89, 182, 255);
    public static ColorInfo Cyan => new(26, 188, 156, 255);
    
    // Highlighters (semi-transparent)
    public static ColorInfo HighlighterYellow => new(255, 241, 118, 100);
    public static ColorInfo HighlighterGreen => new(129, 199, 132, 100);
    public static ColorInfo HighlighterPink => new(240, 98, 146, 100);
    public static ColorInfo HighlighterCyan => new(77, 208, 225, 100);

    public string ToHex(bool includeAlpha = true)
    {
        return includeAlpha 
            ? $"#{A:X2}{R:X2}{G:X2}{B:X2}" 
            : $"#{R:X2}{G:X2}{B:X2}";
    }

    public static ColorInfo FromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Black;
        hex = hex.TrimStart('#');

        if (hex.Length == 6)
        {
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return new ColorInfo(r, g, b, 255);
        }
        if (hex.Length == 8)
        {
            byte a = Convert.ToByte(hex.Substring(0, 2), 16);
            byte r = Convert.ToByte(hex.Substring(2, 2), 16);
            byte g = Convert.ToByte(hex.Substring(4, 2), 16);
            byte b = Convert.ToByte(hex.Substring(6, 2), 16);
            return new ColorInfo(r, g, b, a);
        }

        return Black;
    }
}
