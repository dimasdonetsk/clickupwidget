using System.Windows;
using System.Windows.Media;

namespace ClickUpWidget
{
    /// <summary>Дві теми: темна (як «Опівніч» у Lumos) і світла.</summary>
    internal static class Theme
    {
        public static bool IsLight { get; private set; }

        public static void Apply(bool light)
        {
            IsLight = light;
            if (light)
            {
                Set("Card", "#EBF5F7FB"); Set("CardSolid", "#F5F7FB"); Set("Line", "#1A142033");
                Set("Fg", "#142033"); Set("Fg2", "#4E5D73"); Set("Fg3", "#7A879A");
                Set("Hover", "#0F142033"); Set("Btn", "#14142033"); Set("Menu", "#FFFFFF");
                Set("TimerBg", "#1A17803F"); Set("TimerLine", "#4017803F"); Set("TimerFg", "#17803F");
            }
            else
            {
                Set("Card", "#E6121218"); Set("CardSolid", "#121218"); Set("Line", "#14FFFFFF");
                Set("Fg", "#ECEBF2"); Set("Fg2", "#9B99A8"); Set("Fg3", "#6E6C79");
                Set("Hover", "#0DFFFFFF"); Set("Btn", "#14FFFFFF"); Set("Menu", "#1C1B24");
                Set("TimerBg", "#176FD39A"); Set("TimerLine", "#386FD39A"); Set("TimerFg", "#6FD39A");
            }
        }

        // Мітки проєктів: спокійні напівпрозорі плашки, колір тексту читається в обох темах.
        private static readonly string[] ChipColors = { "#7AA7FF", "#E0A14E", "#B48CFF", "#4FBF85", "#E07A9B", "#4EB6C9", "#C9A227" };

        public static (Brush Back, Brush Fore) Chip(string project)
        {
            string hex = project switch
            {
                "АП" => ChipColors[0],
                "GM" => ChipColors[1],
                "ML" => ChipColors[2],
                "IM" => ChipColors[3],
                "RM" => ChipColors[4],
                _ => ChipColors[(int)(Hash(project) % (uint)ChipColors.Length)],
            };
            Color c = (Color)ColorConverter.ConvertFromString(hex);
            var back = new SolidColorBrush(Color.FromArgb(0x26, c.R, c.G, c.B));
            back.Freeze();
            Color fc = IsLight ? Darken(c, 0.62) : c;
            var fore = new SolidColorBrush(fc);
            fore.Freeze();
            return (back, fore);
        }

        public static Brush StatusBrush(string? hex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex))
                {
                    var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                    b.Freeze();
                    return b;
                }
            }
            catch { }
            return Get("Fg3");
        }

        public static Brush Get(string key) =>
            Application.Current.Resources[key] as Brush ?? Brushes.Gray;

        private static uint Hash(string s)
        {
            uint h = 2166136261;
            foreach (char ch in s) { h ^= ch; h *= 16777619; }
            return h;
        }

        private static Color Darken(Color c, double k) =>
            Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));

        private static void Set(string key, string hex) => Application.Current.Resources[key] = Make(hex);

        public static SolidColorBrush Make(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }
}
