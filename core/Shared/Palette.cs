using System;
using System.Collections.Generic;
using System.Globalization;

namespace ClickUpWidget.Shared
{
    /// <summary>
    /// Кольори теми й міток як рядки #AARRGGBB — без прив'язки до WPF чи Avalonia.
    /// Темна — як «Опівніч» у Lumos, світла — спокійна сіро-синя. Червоних акцентів немає.
    /// </summary>
    public static class Palette
    {
        public static bool IsLight { get; set; }

        private static readonly Dictionary<string, string> Dark = new()
        {
            ["Card"] = "#E6121218", ["CardSolid"] = "#121218", ["Line"] = "#14FFFFFF",
            ["Fg"] = "#ECEBF2", ["Fg2"] = "#9B99A8", ["Fg3"] = "#6E6C79",
            ["Hover"] = "#0DFFFFFF", ["Btn"] = "#14FFFFFF", ["Menu"] = "#1C1B24",
            ["TimerBg"] = "#176FD39A", ["TimerLine"] = "#386FD39A", ["TimerFg"] = "#6FD39A",
        };

        private static readonly Dictionary<string, string> Light = new()
        {
            ["Card"] = "#EBF5F7FB", ["CardSolid"] = "#F5F7FB", ["Line"] = "#1A142033",
            ["Fg"] = "#142033", ["Fg2"] = "#4E5D73", ["Fg3"] = "#7A879A",
            ["Hover"] = "#0F142033", ["Btn"] = "#14142033", ["Menu"] = "#FFFFFF",
            ["TimerBg"] = "#1A17803F", ["TimerLine"] = "#4017803F", ["TimerFg"] = "#17803F",
        };

        /// <summary>Усі кольори теми (ключі ті самі, що й ресурси у вікнах: Card, Fg, Fg2…).</summary>
        public static IReadOnlyDictionary<string, string> Theme(bool light) => light ? Light : Dark;

        public static string Get(string key) => Theme(IsLight).TryGetValue(key, out string? v) ? v : "#808080";

        // Мітки проєктів: спокійні напівпрозорі плашки, колір тексту читається в обох темах.
        private static readonly string[] ChipColors = { "#7AA7FF", "#E0A14E", "#B48CFF", "#4FBF85", "#E07A9B", "#4EB6C9", "#C9A227" };

        /// <summary>Фон і текст мітки проєкту (АП, GM…). Колір сталий для мітки.</summary>
        public static (string Back, string Fore) Chip(string project)
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
            (byte r, byte g, byte b) = Rgb(hex);
            string back = "#26" + Hex(r, g, b);
            string fore = IsLight ? "#" + Hex((byte)(r * 0.62), (byte)(g * 0.62), (byte)(b * 0.62)) : hex;
            return (back, fore);
        }

        /// <summary>Колір статусу з ClickUp; якщо його немає або він дивний — приглушений сірий теми.</summary>
        public static string Status(string? hex) => IsValid(hex) ? hex!.Trim() : Get("Fg3");

        public static bool IsValid(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return false;
            string h = hex.Trim();
            if (h[0] != '#' || !(h.Length is 4 or 7 or 9)) return false;
            for (int i = 1; i < h.Length; i++)
                if (!Uri.IsHexDigit(h[i])) return false;
            return true;
        }

        private static (byte R, byte G, byte B) Rgb(string hex)
        {
            string h = hex.TrimStart('#');
            if (h.Length == 3) h = string.Concat(h[0], h[0], h[1], h[1], h[2], h[2]);
            h = h.Substring(h.Length - 6);
            return (byte.Parse(h.Substring(0, 2), NumberStyles.HexNumber),
                    byte.Parse(h.Substring(2, 2), NumberStyles.HexNumber),
                    byte.Parse(h.Substring(4, 2), NumberStyles.HexNumber));
        }

        private static string Hex(byte r, byte g, byte b) => r.ToString("X2") + g.ToString("X2") + b.ToString("X2");

        private static uint Hash(string s)
        {
            uint h = 2166136261;
            foreach (char ch in s) { h ^= ch; h *= 16777619; }
            return h;
        }
    }
}
