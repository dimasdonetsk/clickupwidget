using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ClickUpWidget
{
    /// <summary>Подія в тихому інбоксі: новий коментар або зміна статусу. Призначень тут немає.</summary>
    public sealed class InboxEvent
    {
        /// <summary>comment або status.</summary>
        public string Kind { get; set; } = "";
        /// <summary>Унікальний ключ (id коментаря або задача+статус+час), щоб не задвоювати.</summary>
        public string Key { get; set; } = "";
        public string TaskId { get; set; } = "";
        public string Project { get; set; } = "";
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string Who { get; set; } = "";
        public string Text { get; set; } = "";
        public string From { get; set; } = "";
        public string? FromColor { get; set; }
        public string To { get; set; } = "";
        public string? ToColor { get; set; }
        public long AtMs { get; set; }
    }

    /// <summary>Що віджет пам'ятає про задачу з минулого оновлення.</summary>
    public sealed class SeenTask
    {
        public string Status { get; set; } = "";
        public string? StatusColor { get; set; }
        public long UpdatedMs { get; set; }
        public long LastCommentMs { get; set; }
    }

    public sealed class InboxState
    {
        public string? TeamId { get; set; }
        public bool Seeded { get; set; }
        public long LastReadMs { get; set; }
        public Dictionary<string, SeenTask> Tasks { get; set; } = new();
        public List<InboxEvent> Events { get; set; } = new();
    }

    /// <summary>Порівняння задач між оновленнями. Чиста логіка без мережі — її покривають тести.</summary>
    public static class Inbox
    {
        public const int MaxEvents = 60;

        /// <summary>
        /// Знайти зміни статусів і задачі, де, можливо, з'явилися нові коментарі.
        /// selfChanges — статуси, які людина сама поставила через віджет (їх у інбокс не кладемо).
        /// </summary>
        public static (List<InboxEvent> Events, List<TaskItem> CheckComments) Diff(
            InboxState state, IReadOnlyList<TaskItem> tasks, long nowMs, IDictionary<string, string> selfChanges)
        {
            var events = new List<InboxEvent>();
            var check = new List<TaskItem>();

            // Перший запуск: просто запам'ятовуємо, як є, щоб не завалити інбокс старим.
            if (!state.Seeded)
            {
                state.Tasks = tasks.ToDictionary(t => t.Id, t => new SeenTask
                {
                    Status = t.Status, StatusColor = t.StatusColor, UpdatedMs = t.UpdatedMs, LastCommentMs = nowMs,
                });
                state.Seeded = true;
                return (events, check);
            }

            var present = new HashSet<string>();
            foreach (TaskItem t in tasks)
            {
                present.Add(t.Id);
                if (!state.Tasks.TryGetValue(t.Id, out SeenTask? seen))
                {
                    // Нова задача на мене — це призначення: в інбокс не кладемо, лише починаємо стежити.
                    state.Tasks[t.Id] = new SeenTask
                    {
                        Status = t.Status, StatusColor = t.StatusColor, UpdatedMs = t.UpdatedMs, LastCommentMs = nowMs,
                    };
                    continue;
                }

                if (!string.Equals(seen.Status, t.Status, StringComparison.OrdinalIgnoreCase))
                {
                    bool mine = selfChanges.TryGetValue(t.Id, out string? self)
                                && string.Equals(self, t.Status, StringComparison.OrdinalIgnoreCase);
                    if (mine) selfChanges.Remove(t.Id);
                    else
                    {
                        long at = t.UpdatedMs > 0 ? t.UpdatedMs : nowMs;
                        events.Add(new InboxEvent
                        {
                            Kind = "status",
                            Key = "s:" + t.Id + ":" + t.Status + ":" + at,
                            TaskId = t.Id, Project = t.Project, Title = t.Title, Url = t.Url,
                            From = seen.Status, FromColor = seen.StatusColor, To = t.Status, ToColor = t.StatusColor,
                            AtMs = at,
                        });
                    }
                    seen.Status = t.Status;
                    seen.StatusColor = t.StatusColor;
                }

                // Задачу змінювали після нашого останнього погляду — там може бути новий коментар.
                if (t.UpdatedMs > seen.UpdatedMs) check.Add(t);
            }

            foreach (string gone in state.Tasks.Keys.Where(id => !present.Contains(id)).ToList())
                state.Tasks.Remove(gone);

            return (events, check);
        }

        /// <summary>Нові коментарі інших людей у задачі. Свої коментарі не показуємо.</summary>
        public static List<InboxEvent> ApplyComments(InboxState state, TaskItem task, IEnumerable<CommentInfo> comments, long myUserId)
        {
            var events = new List<InboxEvent>();
            if (!state.Tasks.TryGetValue(task.Id, out SeenTask? seen)) return events;

            long newest = seen.LastCommentMs;
            foreach (CommentInfo c in comments)
            {
                if (c.DateMs > newest) newest = c.DateMs;
                if (c.DateMs <= seen.LastCommentMs || c.UserId == myUserId) continue;
                events.Add(new InboxEvent
                {
                    Kind = "comment",
                    Key = "c:" + c.Id,
                    TaskId = task.Id, Project = task.Project, Title = task.Title, Url = task.Url,
                    Who = c.UserName, Text = OneLine(c.Text), AtMs = c.DateMs,
                });
            }
            seen.LastCommentMs = newest;
            seen.UpdatedMs = task.UpdatedMs;
            return events;
        }

        /// <summary>Додати події без дублікатів, новіші зверху, не більше MaxEvents.</summary>
        public static void Add(InboxState state, IEnumerable<InboxEvent> events)
        {
            var keys = new HashSet<string>(state.Events.Select(e => e.Key));
            foreach (InboxEvent e in events)
                if (keys.Add(e.Key)) state.Events.Add(e);
            state.Events = state.Events.OrderByDescending(e => e.AtMs).Take(MaxEvents).ToList();
        }

        public static int Unread(InboxState state) => state.Events.Count(e => e.AtMs > state.LastReadMs);

        public static string OneLine(string text)
        {
            string s = string.Join(" ", (text ?? "").Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s;
        }

        /// <summary>«щойно», «5 хв», «2 год», «вчора», «3 дн».</summary>
        public static string Ago(long atMs, DateTime now)
        {
            DateTime at = DateTimeOffset.FromUnixTimeMilliseconds(atMs).LocalDateTime;
            TimeSpan d = now - at;
            if (d.TotalMinutes < 1) return "щойно";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " хв";
            if (at.Date == now.Date) return (int)d.TotalHours + " год";
            int days = (now.Date - at.Date).Days;
            return days == 1 ? "вчора" : days + " дн";
        }

        // ───────────────────────────── збереження ─────────────────────────────

        private static string FilePath => Path.Combine(Settings.DataDir, "inbox.json");

        public static InboxState Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<InboxState>(File.ReadAllText(FilePath)) ?? new InboxState();
            }
            catch (Exception ex) { Log.Write("Inbox load: " + ex.Message); }
            return new InboxState();
        }

        public static void Save(InboxState state)
        {
            try
            {
                Directory.CreateDirectory(Settings.DataDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(state));
            }
            catch (Exception ex) { Log.Write("Inbox save: " + ex.Message); }
        }
    }
}
