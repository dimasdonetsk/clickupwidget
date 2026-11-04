using System;
using System.IO;
using Microsoft.Win32;

namespace ClickUpWidget
{
    /// <summary>Автозапуск у Windows: запис у реєстрі (HKCU\…\Run).</summary>
    internal static class AutostartHelper
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        // Сюди Windows пише, чи автозапуск не вимкнули в Диспетчері завдань → «Автозавантаження».
        private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ValueName = "ClickUpWidget";

        /// <summary>Шлях до exe. Інсталятор кладе програму в …\current\ClickUpWidget.exe — він не змінюється між оновленнями.</summary>
        private static string? ExePath()
        {
            string? path = Environment.ProcessPath;
            if (path == null) return null;
            string? dir = Path.GetDirectoryName(path);
            string? root = dir != null ? Path.GetDirectoryName(dir) : null;
            if (root != null && !string.Equals(Path.GetFileName(dir), "current", StringComparison.OrdinalIgnoreCase))
            {
                string current = Path.Combine(root, "current", Path.GetFileName(path));
                if (File.Exists(current) && File.Exists(Path.Combine(root, "Update.exe"))) return current;
            }
            return path;
        }

        public static void Apply(bool enabled, bool userAction = false)
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
                string? exe = ExePath();
                if (enabled && exe != null)
                {
                    string value = "\"" + exe + "\" --autostart";
                    if (!string.Equals(key.GetValue(ValueName) as string, value, StringComparison.Ordinal))
                        key.SetValue(ValueName, value);
                    if (userAction)
                        using (RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
                            approved?.DeleteValue(ValueName, false);
                }
                else
                    key.DeleteValue(ValueName, false);
            }
            catch (Exception ex) { Log.Write("Autostart: " + ex.Message); }
        }

        /// <summary>Автозапуск вимкнули в Диспетчері завдань (Windows пропускає наш запис).</summary>
        public static bool DisabledByWindows()
        {
            try
            {
                using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
                return approved?.GetValue(ValueName) is byte[] b && b.Length > 0 && (b[0] & 1) == 1;
            }
            catch { return false; }
        }
    }
}
