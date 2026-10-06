using System;
using System.Globalization;
using System.Windows.Media;

namespace SideStack
{
    public static class Ui
    {
        public static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) { return fallback; }
                string s = hex.Trim();
                if (s.StartsWith("#"))
                {
                    string h = s.Substring(1);
                    if (h.Length == 3)
                    {
                        byte r3 = Convert.ToByte(new string(h[0], 2), 16);
                        byte g3 = Convert.ToByte(new string(h[1], 2), 16);
                        byte b3 = Convert.ToByte(new string(h[2], 2), 16);
                        return Color.FromRgb(r3, g3, b3);
                    }
                    if (h.Length == 6)
                    {
                        return Color.FromRgb(
                            Convert.ToByte(h.Substring(0, 2), 16),
                            Convert.ToByte(h.Substring(2, 2), 16),
                            Convert.ToByte(h.Substring(4, 2), 16));
                    }
                    if (h.Length == 8)
                    {
                        return Color.FromArgb(
                            Convert.ToByte(h.Substring(0, 2), 16),
                            Convert.ToByte(h.Substring(2, 2), 16),
                            Convert.ToByte(h.Substring(4, 2), 16),
                            Convert.ToByte(h.Substring(6, 2), 16));
                    }
                }
                object o = ColorConverter.ConvertFromString(s);
                if (o is Color) { return (Color)o; }
            }
            catch { }
            return fallback;
        }

        public static SolidColorBrush MakeBrush(string hex, double opacity, Color fallback)
        {
            Color c = ParseColor(hex, fallback);
            return MakeBrush(c, opacity);
        }

        public static SolidColorBrush MakeBrush(Color c, double opacity)
        {
            double a = opacity;
            if (a < 0) { a = 0; }
            if (a > 1) { a = 1; }
            byte alpha = (byte)Math.Round(a * 255.0);
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }

        public static string ToHex(Color c)
        {
            return "#" + c.R.ToString("X2", CultureInfo.InvariantCulture)
                       + c.G.ToString("X2", CultureInfo.InvariantCulture)
                       + c.B.ToString("X2", CultureInfo.InvariantCulture);
        }
    }
}
