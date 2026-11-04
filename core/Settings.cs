using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClickUpWidget
{
    public sealed class Settings
    {
        /// <summary>
        /// Особистий API-токен ClickUp, зашифрований DPAPI (розшифрувати може лише цей користувач Windows).
        /// На Mac тут порожньо: токен лежить у Keychain (див. <see cref="Secret"/>).
        /// </summary>
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

        /// <summary>Налаштування, стан інбоксу й журнал: %AppData%\ClickUpWidget, на Mac — ~/Library/Application Support/ClickUpWidget.</summary>
        public static string DataDir { get; internal set; } = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "ClickUpWidget")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClickUpWidget");

        private static string FilePath => Path.Combine(DataDir, "settings.json");

        [JsonIgnore]
        public string? Token
        {
            get => Secret.LoadToken(this);
            set => Secret.SaveToken(this, value);
        }

        /// <summary>Чи збережено токен (без розшифрування; на Mac — з пам'яті після першого читання).</summary>
        [JsonIgnore]
        public bool HasToken => Secret.HasToken(this);

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
}
