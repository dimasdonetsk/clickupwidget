using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace ClickUpWidget
{
    public sealed class Settings
    {
        /// <summary>Особистий API-токен ClickUp, зашифрований DPAPI (розшифрувати може лише цей користувач Windows).</summary>
        public string? TokenEnc { get; set; }
        /// <summary>Робочий простір (team id). Якщо їх кілька — обирається в меню трею.</summary>
        public string? TeamId { get; set; }

        public bool ShowPanel { get; set; } = true;
        public bool LockPanel { get; set; }
        /// <summary>Положення панелі на столі (у WPF-одиницях). null — правий верхній кут.</summary>
        public double? PanelLeft { get; set; }
        public double? PanelTop { get; set; }

        // Що показувати на панелі
        public bool ShowTodayTime { get; set; } = true;
        public bool ShowTimer { get; set; } = true;
        public bool ShowDueSection { get; set; } = true;
        public bool ShowAllSection { get; set; } = true;
        // …у флайауті з трею
        public bool FlyoutShowTimer { get; set; } = true;
        public bool FlyoutShowDue { get; set; } = true;
        // …в рядку задачі
        public bool RowShowProject { get; set; } = true;
        public bool RowShowDue { get; set; } = true;
        public bool RowShowPriority { get; set; }
        // …в інбоксі (кожен тип подій окремо)
        public bool InboxComments { get; set; } = true;
        public bool InboxMentions { get; set; } = true;
        public bool InboxStatuses { get; set; } = true;
        /// <summary>Нова задача на мене. За замовчуванням вимкнено — призначення відволікають.</summary>
        public bool InboxAssigned { get; set; }
        public bool InboxUnassigned { get; set; } = true;
        public bool InboxClosed { get; set; } = true;
        public bool InboxDue { get; set; } = true;
        public bool InboxPriority { get; set; } = true;
        public bool InboxRenamed { get; set; } = true;
        public bool InboxAssignees { get; set; } = true;
        public bool InboxOwnComments { get; set; }

        /// <summary>Чи показувати подію інбоксу за налаштуваннями.</summary>
        public bool InboxShows(InboxEvent e)
        {
            if (e.Mine) return InboxOwnComments;
            return e.Kind switch
            {
                Inbox.KindComment => InboxComments,
                Inbox.KindMention => InboxMentions || InboxComments,
                Inbox.KindStatus => InboxStatuses,
                Inbox.KindAssigned => InboxAssigned,
                Inbox.KindUnassigned => InboxUnassigned,
                Inbox.KindClosed or Inbox.KindDeleted => InboxClosed,
                Inbox.KindDue => InboxDue,
                Inbox.KindPriority => InboxPriority,
                Inbox.KindRenamed => InboxRenamed,
                Inbox.KindAssignees => InboxAssignees,
                _ => true,
            };
        }

        /// <summary>
        /// Вибір людини для окремих статусів (назва в нижньому регістрі → показувати чи ні).
        /// Статуси без запису — за правилом за замовчуванням (беклог, скасовані й виконані сховані).
        /// </summary>
        public Dictionary<string, bool> StatusVisibility { get; set; } = new();
        /// <summary>Сховані проєкти (мітки АП, GM…; порожній рядок — задачі без мітки).</summary>
        public List<string> HiddenProjects { get; set; } = new();

        /// <summary>Маленьке віконце оновлень (коментарі й статуси).</summary>
        public bool ShowInbox { get; set; } = true;
        public double? InboxLeft { get; set; }
        public double? InboxTop { get; set; }

        public bool Autostart { get; set; } = true;
        /// <summary>Dark (темна) або Light (світла).</summary>
        public string Theme { get; set; } = "Dark";
        /// <summary>Як часто оновлювати задачі, хв.</summary>
        public int RefreshMinutes { get; set; } = 2;

        public static string DataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClickUpWidget");

        private static string FilePath => Path.Combine(DataDir, "settings.json");

        [JsonIgnore]
        public string? Token
        {
            get => Secret.Unprotect(TokenEnc);
            set => TokenEnc = Secret.Protect(value);
        }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
            catch (Exception ex) { Log.Write("Settings load: " + ex.Message); }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { Log.Write("Settings save: " + ex.Message); }
        }
    }

    internal static class Secret
    {
        public static string? Protect(string? plain)
        {
            if (string.IsNullOrEmpty(plain)) return null;
            try
            {
                byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(data);
            }
            catch (Exception ex) { Log.Write("Protect: " + ex.Message); return null; }
        }

        public static string? Unprotect(string? enc)
        {
            if (string.IsNullOrEmpty(enc)) return null;
            try
            {
                byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch (Exception ex) { Log.Write("Unprotect: " + ex.Message); return null; }
        }
    }

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
