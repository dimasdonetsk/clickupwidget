using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ClickUpWidget
{
    /// <summary>Подія в тихому інбоксі. Які типи показувати — налаштовується в меню «Що показувати».</summary>
    public sealed class InboxEvent
    {
        /// <summary>Тип події, див. Inbox.Kind*.</summary>
        public string Kind { get; set; } = "";
        /// <summary>Унікальний ключ, щоб не задвоювати.</summary>
        public string Key { get; set; } = "";
        public string TaskId { get; set; } = "";
        public string Project { get; set; } = "";
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string Who { get; set; } = "";
        /// <summary>Автор коментаря (щоб відповісти з @згадкою).</summary>
        public long WhoId { get; set; }
        public string Text { get; set; } = "";
        public string From { get; set; } = "";
        public string? FromColor { get; set; }
        public string To { get; set; } = "";
        public string? ToColor { get; set; }
        public long AtMs { get; set; }
        /// <summary>Мій власний коментар (за замовчуванням не показується).</summary>
        public bool Mine { get; set; }
    }

    /// <summary>Що віджет пам'ятає про задачу з минулого оновлення.</summary>
    public sealed class SeenTask
    {
        public string Status { get; set; } = "";
        public string? StatusColor { get; set; }
        public long UpdatedMs { get; set; }
        public long LastCommentMs { get; set; }
        /// <summary>false — запис зі старої версії без полів нижче: заповнюємо мовчки, без подій.</summary>
        public bool Full { get; set; }
        public string Name { get; set; } = "";
        public long DueMs { get; set; }
        public string? Priority { get; set; }
        public string? PriorityColor { get; set; }
        /// <summary>Виконавці: id → ім'я.</summary>
        public Dictionary<string, string> Assignees { get; set; } = new();
    }

    public sealed class InboxState
    {
        public string? TeamId { get; set; }
        public bool Seeded { get; set; }
        public long LastReadMs { get; set; }
        public Dictionary<string, SeenTask> Tasks { get; set; } = new();
        public List<InboxEvent> Events { get; set; } = new();
    }

    /// <summary>Результат порівняння: нові події, задачі для перевірки коментарів, задачі, що зникли зі списку.</summary>
    public sealed class InboxDiff
    {
        public List<InboxEvent> Events { get; } = new();
        public List<TaskItem> CheckComments { get; } = new();
        public List<string> Gone { get; } = new();

        public void Deconstruct(out List<InboxEvent> events, out List<TaskItem> checkComments)
        {
            events = Events;
            checkComments = CheckComments;
        }
    }

    /// <summary>Порівняння задач між оновленнями. Чиста логіка без мережі — її покривають тести.</summary>
    public static class Inbox
    {
        public const int MaxEvents = 80;

        public const string KindComment = "comment";
        public const string KindMention = "mention";
        public const string KindStatus = "status";
        public const string KindAssigned = "assigned";
        public const string KindUnassigned = "unassigned";
        public const string KindClosed = "closed";
        public const string KindDeleted = "deleted";
        public const string KindDue = "due";
        public const string KindPriority = "priority";
        public const string KindRenamed = "renamed";
        public const string KindAssignees = "assignees";

        private static SeenTask Remember(TaskItem t, long nowMs)
        {
            var seen = new SeenTask { LastCommentMs = nowMs };
            Fill(seen, t);
            return seen;
        }

        private static void Fill(SeenTask seen, TaskItem t)
        {
            seen.Status = t.Status;
            seen.StatusColor = t.StatusColor;
            seen.UpdatedMs = t.UpdatedMs;
            seen.Name = t.RawName;
            seen.DueMs = DueMs(t);
            seen.Priority = t.Priority;
            seen.PriorityColor = t.PriorityColor;
            seen.Assignees = t.Assignees.ToDictionary(p => p.Id.ToString(), p => p.Name);
            seen.Full = true;
        }

        private static long DueMs(TaskItem t) => t.Due is DateTime d ? new DateTimeOffset(d).ToUnixTimeMilliseconds() : 0;

        private static InboxEvent New(string kind, TaskItem t, long at, string keyTail = "") => new()
        {
            Kind = kind,
            Key = kind + ":" + t.Id + ":" + keyTail + ":" + at,
            TaskId = t.Id, Project = t.Project, Title = t.Title, Url = t.Url,
            AtMs = at,
        };

        /// <summary>
        /// Порівняти задачі з тим, що бачили минулого разу.
        /// selfChanges — статуси, які людина сама поставила через віджет (їх у інбокс не кладемо).
        /// </summary>
        public static InboxDiff Diff(InboxState state, IReadOnlyList<TaskItem> tasks, long nowMs,
            IDictionary<string, string> selfChanges, long myUserId = 0)
        {
            var diff = new InboxDiff();

            // Перший запуск: просто запам'ятовуємо, як є, щоб не завалити інбокс старим.
            if (!state.Seeded)
            {
                state.Tasks = tasks.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => Remember(g.First(), nowMs));
                state.Seeded = true;
                return diff;
            }

            var present = new HashSet<string>();
            foreach (TaskItem t in tasks)
            {
                if (!present.Add(t.Id)) continue;
                long at = t.UpdatedMs > 0 ? t.UpdatedMs : nowMs;

                if (!state.Tasks.TryGetValue(t.Id, out SeenTask? seen))
                {
                    // Нова задача в моєму списку — мене призначили.
                    diff.Events.Add(New(KindAssigned, t, at));
                    state.Tasks[t.Id] = Remember(t, nowMs);
                    continue;
                }

                if (!string.Equals(seen.Status, t.Status, StringComparison.OrdinalIgnoreCase))
                {
                    bool mine = selfChanges.TryGetValue(t.Id, out string? self)
                                && string.Equals(self, t.Status, StringComparison.OrdinalIgnoreCase);
                    if (mine) selfChanges.Remove(t.Id);
                    else
                    {
                        InboxEvent e = New(KindStatus, t, at, t.Status);
                        e.From = seen.Status; e.FromColor = seen.StatusColor;
                        e.To = t.Status; e.ToColor = t.StatusColor;
                        diff.Events.Add(e);
                    }
                }

                if (seen.Full)
                {
                    long due = DueMs(t);
                    if (due != seen.DueMs && TaskText.FormatDate(due) != TaskText.FormatDate(seen.DueMs))
                    {
                        InboxEvent e = New(KindDue, t, at, due.ToString());
                        e.From = TaskText.FormatDate(seen.DueMs);
                        e.To = TaskText.FormatDate(due);
                        diff.Events.Add(e);
                    }

                    if (!string.Equals(seen.Priority ?? "", t.Priority ?? "", StringComparison.OrdinalIgnoreCase))
                    {
                        InboxEvent e = New(KindPriority, t, at, t.Priority ?? "none");
                        e.From = TaskText.PriorityLabel(seen.Priority); e.FromColor = seen.PriorityColor;
                        e.To = TaskText.PriorityLabel(t.Priority); e.ToColor = t.PriorityColor;
                        diff.Events.Add(e);
                    }

                    if (seen.Name.Length > 0 && !string.Equals(seen.Name, t.RawName, StringComparison.Ordinal))
                    {
                        InboxEvent e = New(KindRenamed, t, at, t.RawName.GetHashCode().ToString());
                        e.From = TaskText.Split(seen.Name).Title;
                        e.To = t.Title;
                        e.Text = "Було: " + e.From;
                        diff.Events.Add(e);
                    }

                    // Інші виконавці (себе не рахуємо — для цього є «призначили» і «прибрали»).
                    string me = myUserId.ToString();
                    var now = t.Assignees.Where(p => p.Id != myUserId).ToDictionary(p => p.Id.ToString(), p => p.Name);
                    var was = seen.Assignees.Where(kv => kv.Key != me).ToDictionary(kv => kv.Key, kv => kv.Value);
                    List<string> added = now.Where(kv => !was.ContainsKey(kv.Key)).Select(kv => "+ " + kv.Value).ToList();
                    List<string> removed = was.Where(kv => !now.ContainsKey(kv.Key)).Select(kv => "− " + kv.Value).ToList();
                    if (added.Count + removed.Count > 0)
                    {
                        InboxEvent e = New(KindAssignees, t, at, string.Join(",", now.Keys.OrderBy(k => k)));
                        e.Text = string.Join(" · ", added.Concat(removed));
                        diff.Events.Add(e);
                    }
                }

                // Задачу змінювали після нашого останнього погляду — там може бути новий коментар.
                if (t.UpdatedMs > seen.UpdatedMs) diff.CheckComments.Add(t);

                long lastComment = seen.LastCommentMs, updated = seen.UpdatedMs;
                Fill(seen, t);
                seen.LastCommentMs = lastComment;
                seen.UpdatedMs = updated;     // оновлюється в ApplyComments, коли коментарі вже перевірено
            }

            foreach (string id in state.Tasks.Keys.Where(id => !present.Contains(id)))
                diff.Gone.Add(id);

            return diff;
        }

        /// <summary>
        /// Задача зникла з мого списку. fetched — що з нею зараз (null — видалена).
        /// Повертає подію (закрили, прибрали мене, видалили) або null і забуває задачу.
        /// </summary>
        public static InboxEvent? ResolveGone(InboxState state, string taskId, TaskItem? fetched, long myUserId, long nowMs,
            IDictionary<string, string> selfChanges)
        {
            if (!state.Tasks.TryGetValue(taskId, out SeenTask? seen)) return null;
            state.Tasks.Remove(taskId);

            (string project, string title) = TaskText.Split(seen.Name);
            if (fetched == null)
            {
                var gone = new TaskItem { Id = taskId, Project = project, Title = title.Length > 0 ? title : "Задача" };
                return New(KindDeleted, gone, nowMs);
            }

            long at = fetched.UpdatedMs > 0 ? fetched.UpdatedMs : nowMs;
            bool closed = string.Equals(fetched.StatusType, "closed", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(fetched.StatusType, "done", StringComparison.OrdinalIgnoreCase);
            if (closed)
            {
                if (selfChanges.TryGetValue(taskId, out string? self) && string.Equals(self, fetched.Status, StringComparison.OrdinalIgnoreCase))
                {
                    selfChanges.Remove(taskId);
                    return null;
                }
                InboxEvent e = New(KindClosed, fetched, at, fetched.Status);
                e.From = seen.Status; e.FromColor = seen.StatusColor;
                e.To = fetched.Status; e.ToColor = fetched.StatusColor;
                return e;
            }

            if (fetched.Assignees.All(p => p.Id != myUserId))
                return New(KindUnassigned, fetched, at);

            return null;
        }

        /// <summary>Нові коментарі в задачі. Згадки мене — окремий тип; власні позначаються Mine.</summary>
        public static List<InboxEvent> ApplyComments(InboxState state, TaskItem task, IEnumerable<CommentInfo> comments, long myUserId)
        {
            var events = new List<InboxEvent>();
            if (!state.Tasks.TryGetValue(task.Id, out SeenTask? seen)) return events;

            long newest = seen.LastCommentMs;
            foreach (CommentInfo c in comments)
            {
                if (c.DateMs > newest) newest = c.DateMs;
                if (c.DateMs <= seen.LastCommentMs) continue;
                bool mine = c.UserId == myUserId;
                bool mentionsMe = !mine && c.Tagged != null && c.Tagged.Contains(myUserId);
                events.Add(new InboxEvent
                {
                    Kind = mentionsMe ? KindMention : KindComment,
                    Key = "c:" + c.Id,
                    TaskId = task.Id, Project = task.Project, Title = task.Title, Url = task.Url,
                    Who = c.UserName, WhoId = c.UserId, Text = OneLine(c.Text), AtMs = c.DateMs, Mine = mine,
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
