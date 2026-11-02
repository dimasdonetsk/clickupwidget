using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClickUpWidget
{
    public sealed class ClickUpException : Exception
    {
        public bool Unauthorized { get; }
        public ClickUpException(string message, bool unauthorized = false) : base(message) { Unauthorized = unauthorized; }
    }

    /// <summary>Мінімальний клієнт ClickUp API v2 — лише те, що потрібно віджету.</summary>
    public sealed class ClickUpClient : IDisposable
    {
        private const string Base = "https://api.clickup.com/api/v2/";
        private readonly HttpClient _http;

        public ClickUpClient(string token)
        {
            _http = new HttpClient { BaseAddress = new Uri(Base), Timeout = TimeSpan.FromSeconds(30) };
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", token.Trim());
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ClickUpWidget", "1.0"));
        }

        public void Dispose() => _http.Dispose();

        // ───────────────────────────── користувач і простори ─────────────────────────────

        public async Task<(long Id, string Name)> GetUserAsync()
        {
            using JsonDocument doc = await GetAsync("user");
            JsonElement u = doc.RootElement.GetProperty("user");
            return (u.GetProperty("id").GetInt64(), Str(u, "username") ?? "");
        }

        public async Task<List<(string Id, string Name)>> GetTeamsAsync()
        {
            using JsonDocument doc = await GetAsync("team");
            var list = new List<(string, string)>();
            foreach (JsonElement t in doc.RootElement.GetProperty("teams").EnumerateArray())
                list.Add((Str(t, "id") ?? "", Str(t, "name") ?? ""));
            return list;
        }

        // ───────────────────────────── задачі ─────────────────────────────

        /// <summary>Усі відкриті задачі, призначені на мене, у робочому просторі.</summary>
        public async Task<List<TaskItem>> GetMyTasksAsync(string teamId, long userId)
        {
            var result = new List<TaskItem>();
            for (int page = 0; page < 20; page++)
            {
                string url = "team/" + teamId + "/task?page=" + page
                             + "&assignees[]=" + userId
                             + "&include_closed=false&subtasks=true&order_by=due_date&reverse=true";
                using JsonDocument doc = await GetAsync(url);
                JsonElement root = doc.RootElement;
                foreach (JsonElement t in root.GetProperty("tasks").EnumerateArray())
                {
                    TaskItem item = ParseTask(t);
                    if (TaskText.IsOpen(item)) result.Add(item);
                }
                bool last = !root.TryGetProperty("last_page", out JsonElement lp) || lp.ValueKind != JsonValueKind.False;
                if (last) break;
            }
            return result;
        }

        internal static TaskItem ParseTask(JsonElement t)
        {
            string raw = Str(t, "name") ?? "";
            (string project, string title) = TaskText.Split(raw);
            JsonElement st = t.GetProperty("status");
            return new TaskItem
            {
                Id = Str(t, "id") ?? "",
                RawName = raw,
                Project = project,
                Title = title,
                Status = Str(st, "status") ?? "",
                StatusColor = Str(st, "color"),
                StatusType = Str(st, "type") ?? "",
                StatusOrder = Int(st, "orderindex"),
                Due = Ms(Str(t, "due_date")),
                Url = Str(t, "url") ?? "",
                ListId = t.TryGetProperty("list", out JsonElement l) ? Str(l, "id") ?? "" : "",
            };
        }

        /// <summary>Статуси списку, до якого належить задача (для меню зміни статусу).</summary>
        public async Task<List<StatusOption>> GetListStatusesAsync(string listId)
        {
            using JsonDocument doc = await GetAsync("list/" + listId);
            var list = new List<StatusOption>();
            foreach (JsonElement s in doc.RootElement.GetProperty("statuses").EnumerateArray())
                list.Add(new StatusOption(Str(s, "status") ?? "", Str(s, "color"), Str(s, "type") ?? "", Int(s, "orderindex")));
            list.Sort((a, b) => a.Order.CompareTo(b.Order));
            return list;
        }

        public async Task SetStatusAsync(string taskId, string status)
        {
            string body = JsonSerializer.Serialize(new { status });
            using var req = new HttpRequestMessage(HttpMethod.Put, "task/" + taskId)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            using HttpResponseMessage res = await _http.SendAsync(req);
            await EnsureOk(res);
        }

        // ───────────────────────────── трекінг часу ─────────────────────────────

        public async Task<RunningTimer?> GetRunningAsync(string teamId)
        {
            using JsonDocument doc = await GetAsync("team/" + teamId + "/time_entries/current");
            if (!doc.RootElement.TryGetProperty("data", out JsonElement d) || d.ValueKind != JsonValueKind.Object)
                return null;
            DateTime? start = Ms(Str(d, "start"));
            if (start == null) return null;
            string taskId = "", taskName = "";
            if (d.TryGetProperty("task", out JsonElement task) && task.ValueKind == JsonValueKind.Object)
            {
                taskId = Str(task, "id") ?? "";
                taskName = Str(task, "name") ?? "";
            }
            return new RunningTimer(taskId, taskName, start.Value.ToUniversalTime());
        }

        public async Task StartAsync(string teamId, string taskId)
        {
            string body = JsonSerializer.Serialize(new { tid = taskId });
            using var req = new HttpRequestMessage(HttpMethod.Post, "team/" + teamId + "/time_entries/start")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            using HttpResponseMessage res = await _http.SendAsync(req);
            await EnsureOk(res);
        }

        public async Task StopAsync(string teamId)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "team/" + teamId + "/time_entries/stop")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            using HttpResponseMessage res = await _http.SendAsync(req);
            await EnsureOk(res);
        }

        /// <summary>Сума завершених записів часу за сьогодні (запущений таймер рахується окремо).</summary>
        public async Task<TimeSpan> GetFinishedTodayAsync(string teamId, DateTime now)
        {
            long from = ToMs(now.Date), to = ToMs(now.Date.AddDays(1));
            using JsonDocument doc = await GetAsync("team/" + teamId + "/time_entries?start_date=" + from + "&end_date=" + to);
            long total = 0;
            if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
                foreach (JsonElement e in data.EnumerateArray())
                {
                    if (long.TryParse(Str(e, "duration"), out long ms) && ms > 0) total += ms;
                }
            return TimeSpan.FromMilliseconds(total);
        }

        // ───────────────────────────── допоміжне ─────────────────────────────

        private async Task<JsonDocument> GetAsync(string url)
        {
            using HttpResponseMessage res = await _http.GetAsync(url);
            await EnsureOk(res);
            await using var stream = await res.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream);
        }

        private static async Task EnsureOk(HttpResponseMessage res)
        {
            if (res.IsSuccessStatusCode) return;
            string text = "";
            try { text = await res.Content.ReadAsStringAsync(); } catch { }
            if (text.Length > 300) text = text.Substring(0, 300);
            bool auth = res.StatusCode == HttpStatusCode.Unauthorized;
            throw new ClickUpException("ClickUp " + (int)res.StatusCode + ": " + text, auth);
        }

        private static string? Str(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out JsonElement v)) return null;
            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            };
        }

        private static int Int(JsonElement e, string name) =>
            int.TryParse(Str(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

        internal static DateTime? Ms(string? value) =>
            long.TryParse(value, out long ms) && ms > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime
                : null;

        private static long ToMs(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();
    }
}
