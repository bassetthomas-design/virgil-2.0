using System.Windows.Media;

namespace Virgil.Tests;

internal static class ContrastChecker
{
    public static double Ratio(Color fg, Color bg)
    {
        static double Linear(byte value)
        {
            var s = value / 255d;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        var a = Luminance(fg);
        var b = Luminance(bg);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
