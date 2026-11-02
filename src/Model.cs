using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ClickUpWidget
{
    /// <summary>Задача ClickUp у тому вигляді, який потрібен віджету.</summary>
    public sealed class TaskItem
    {
        public string Id { get; init; } = "";
        public string RawName { get; init; } = "";
        public string Project { get; init; } = "";
        public string Title { get; init; } = "";
        public string Status { get; init; } = "";
        public string? StatusColor { get; init; }
        /// <summary>open, custom, done, closed.</summary>
        public string StatusType { get; init; } = "";
        public int StatusOrder { get; init; }
        public DateTime? Due { get; init; }
        public string Url { get; init; } = "";
        public string ListId { get; init; } = "";
    }

    public sealed record StatusOption(string Name, string? Color, string Type, int Order);

    public sealed record RunningTimer(string TaskId, string TaskName, DateTime StartUtc);

    /// <summary>Чисті правила розбору й форматування (без вікон) — їх покривають тести.</summary>
    public static class TaskText
    {
        private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");

        /// <summary>
        /// «📝 GM: Хелловін – подарунок - контент» → ("GM", "Хелловін – подарунок — контент").
        /// Префікс — коротке слово до двокрапки на початку назви (АП, GM, ML…). Емодзі на початку ігноруються.
        /// </summary>
        public static (string Project, string Title) Split(string raw)
        {
            string s = (raw ?? "").Trim();
            int start = 0;
            while (start < s.Length && !char.IsLetterOrDigit(s[start])) start++;
            string body = s.Substring(start);

            string project = "";
            int colon = body.IndexOf(':');
            if (colon > 0 && colon <= 6)
            {
                string candidate = body.Substring(0, colon).Trim();
                if (candidate.Length > 0 && candidate.All(char.IsLetterOrDigit))
                {
                    project = candidate.ToUpper(Uk);
                    body = body.Substring(colon + 1);
                }
            }

            string title = body.Trim();
            // Останній « - » зазвичай відділяє тип роботи (контент, верстка, запуск): робимо його тире.
            int dash = title.LastIndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0) title = title.Substring(0, dash) + " — " + title.Substring(dash + 3);
            if (title.Length == 0) title = s;
            return (project, title);
        }

        /// <summary>Дедлайн словами: сьогодні, завтра, вчора, день тижня, або дата.</summary>
        public static string FormatDue(DateTime? due, DateTime now)
        {
            if (due == null) return "";
            int days = (due.Value.Date - now.Date).Days;
            return days switch
            {
                0 => "сьогодні",
                1 => "завтра",
                -1 => "вчора",
                > 1 and < 7 => Uk.DateTimeFormat.GetAbbreviatedDayName(due.Value.DayOfWeek),
                _ => due.Value.Day + " " + Months[due.Value.Month - 1],
            };
        }

        private static readonly string[] Months = { "січ", "лют", "бер", "кві", "тра", "чер", "лип", "сер", "вер", "жов", "лис", "гру" };

        /// <summary>Дедлайн сьогодні або вже минув.</summary>
        public static bool IsDueNow(DateTime? due, DateTime now) => due != null && due.Value.Date <= now.Date;

        /// <summary>Закриті й виконані статуси у віджеті не показуємо.</summary>
        public static bool IsOpen(TaskItem t) =>
            !string.Equals(t.StatusType, "closed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(t.StatusType, "done", StringComparison.OrdinalIgnoreCase);

        /// <summary>Групи за статусом у порядку, як у ClickUp; всередині — за дедлайном.</summary>
        public static List<(string Status, string? Color, List<TaskItem> Tasks)> Group(IEnumerable<TaskItem> tasks)
        {
            return tasks
                .GroupBy(t => t.Status.ToLowerInvariant())
                .Select(g => (
                    Order: g.Min(t => StatusRank(t)),
                    Status: g.First().Status,
                    Color: g.First().StatusColor,
                    Tasks: g.OrderBy(t => t.Due ?? DateTime.MaxValue).ThenBy(t => t.Title, StringComparer.CurrentCulture).ToList()))
                .OrderBy(x => x.Order)
                .Select(x => (x.Status, x.Color, x.Tasks))
                .ToList();
        }

        /// <summary>«В роботі» вище, ніж «to do»: спершу те, що вже почато, потім решта за порядком ClickUp.</summary>
        private static int StatusRank(TaskItem t)
        {
            bool started = t.Status.Contains("progress", StringComparison.OrdinalIgnoreCase)
                           || t.Status.Contains("робот", StringComparison.OrdinalIgnoreCase);
            return (started ? 0 : 1000) + t.StatusOrder;
        }

        /// <summary>«2 год 14 хв», «35 хв», «0 хв».</summary>
        public static string FormatSpan(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            int h = (int)span.TotalHours, m = span.Minutes;
            return h > 0 ? h + " год " + m + " хв" : m + " хв";
        }

        /// <summary>Таймер: 00:23:41.</summary>
        public static string FormatClock(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            return ((int)span.TotalHours).ToString("00") + ":" + span.Minutes.ToString("00") + ":" + span.Seconds.ToString("00");
        }

        public static string StatusLabel(string status) =>
            status.Length == 0 ? status : char.ToUpper(status[0], Uk) + status.Substring(1);
    }
}
