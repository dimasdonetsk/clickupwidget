using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Velopack;

namespace ClickUpWidget.Shared
{
    /// <summary>
    /// Мозок програми без прив'язки до вікон: тягне дані з ClickUp, оновлює моделі панелі, картки
    /// й інбоксу, надсилає коментарі, ставить оновлення. Вікна, значок і меню дає платформа (IWidgetShell).
    /// Зараз на ньому працює версія для Mac; Windows поки на своєму src/AppController.cs (та сама логіка).
    /// </summary>
    public sealed class WidgetController : IDisposable
    {
        public static WidgetController? Current { get; private set; }

        public Settings Settings { get; private set; } = new();
        public IReadOnlyList<(string Id, string Name)> Teams => _teams.Select(t => (t.Id, t.Name)).ToList();
        public string UserName { get; private set; } = "";
        public DashboardViewModel Dashboard { get; } = new();
        public InboxViewModel InboxView { get; } = new();

        private readonly IWidgetShell _shell;
        private readonly DashboardViewModel _vm;
        private readonly InboxViewModel _ivm;

        private ClickUpClient? _client;
        private long _userId;
        private List<(string Id, string Name, List<Person> Members)> _teams = new();

        private List<TaskItem> _tasks = new();          // те, що показуємо (без беклогу, скасованих, done)
        private List<TaskItem> _allTasks = new();       // усе незакрите на мене — для інбоксу
        private RunningTimer? _running;
        private TimeSpan _finishedToday;
        private DateTime _dataDay = DateTime.Today;
        private DateTime? _lastOk;
        private bool _refreshing;
        private readonly Dictionary<string, List<StatusOption>> _statusCache = new();

        // Тихий інбокс
        private InboxState _inbox = new();
        /// <summary>Статуси, які людина поставила сама через віджет — їх в інбокс не кладемо.</summary>
        private readonly Dictionary<string, string> _selfStatus = new();
        private int _lastMinute = -1;

        private readonly UiTimer _refreshTimer;
        private readonly UiTimer _pollTimer;
        private readonly UiTimer _tickTimer;
        // Оновлення: двічі на день, тихо (без вікон), з перезапуском.
        private readonly UiTimer _updateTimer;
        private readonly UiTimer _applyRetry;
        private readonly List<UiTimer> _oneShots = new();

        // Поле коментаря: відкрите лише одне; поки людина пише, списки не перебудовуються.
        private IComposerHost? _composerHost;
        private bool _rowsDirty, _inboxDirty;
        private readonly Dictionary<string, (DateTime At, List<Person> People)> _taskPeople = new();
        private UpdateInfo? _pendingUpdate;
        private bool _checkingUpdates;
        /// <summary>До цього моменту нижній рядок не перетирається звичайним «Оновлено о…».</summary>
        private DateTime _stateStickyUntil;

        public string? PendingUpdateVersion => _pendingUpdate != null ? Updater.VersionOf(_pendingUpdate) : null;
        public bool IsComposing => _composerHost != null;
        public RunningTimer? Running => _running;

        public WidgetController(IWidgetShell shell)
        {
            _shell = shell;
            _vm = Dashboard;
            _ivm = InboxView;
            _refreshTimer = new UiTimer(shell.Post, TimeSpan.FromMinutes(2), () => _ = RefreshAsync());
            _pollTimer = new UiTimer(shell.Post, TimeSpan.FromSeconds(45), () => _ = PollTimerAsync());
            _tickTimer = new UiTimer(shell.Post, TimeSpan.FromSeconds(1), Tick);
            _updateTimer = new UiTimer(shell.Post, TimeSpan.FromHours(12), () => _ = CheckUpdatesAsync(false));
            _applyRetry = new UiTimer(shell.Post, TimeSpan.FromMinutes(10), TryApplySilently);
        }

        /// <summary>Без мережі: не підключатися до ClickUp і не шукати оновлень (знімки екрана, перевірка вигляду).</summary>
        public bool Offline { get; init; }

        public void Start()
        {
            Current = this;
            Settings = Settings.Load();
            Palette.IsLight = Settings.Theme == "Light";
            _shell.ApplyTheme(Palette.IsLight);
            if (Settings.Autostart && !Offline) _shell.SetAutostart(true, userAction: false);

            _vm.OpenTask = new RelayCommand(p => { if (p is TaskRow r) OpenUrl(r.Task.Url); });
            _vm.ToggleTimer = new RelayCommand(p => { if (p is TaskRow r) _ = ToggleTimerAsync(r.Task); });
            _vm.StopTimer = new RelayCommand(() => _ = StopTimerAsync());
            _vm.Refresh = new RelayCommand(RefreshNow);
            _vm.ShowMenu = new RelayCommand(_shell.ShowMenu);
            _vm.OpenClickUp = new RelayCommand(OpenClickUp);
            _vm.AddToken = new RelayCommand(_shell.ShowTokenWindow);
            _vm.ApplyUpdate = new RelayCommand(() => ApplyUpdate(silent: false));

            _inbox = Inbox.Load();
            _ivm.Open = new RelayCommand(p => { if (p is InboxRow r) OpenUrl(r.Event.Url); });
            _ivm.MarkRead = new RelayCommand(MarkInboxRead);
            _ivm.ShowMenu = new RelayCommand(_shell.ShowMenu);
            RenderInbox();

            if (Settings.ShowPanel) _shell.ShowPanel();
            if (Settings.ShowInbox) _shell.ShowInbox();

            _refreshTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, Settings.RefreshMinutes));
            _tickTimer.Start();
            if (Offline) return;

            string? token = Settings.Token;
            if (token == null)
            {
                _vm.NeedsToken = true;
                _vm.StateText = "Не підключено";
                _shell.ShowTokenWindow();
            }
            else
                _ = ConnectAsync(token, saveToken: false);

            // Перевірка оновлень трохи згодом, щоб не гальмувати запуск.
            After(TimeSpan.FromSeconds(40), () => { _ = CheckUpdatesAsync(false); _updateTimer.Start(); });
        }

        /// <summary>Одноразова дія через деякий час (у потоці інтерфейсу).</summary>
        private void After(TimeSpan delay, Action action)
        {
            UiTimer? timer = null;
            timer = new UiTimer(_shell.Post, delay, () =>
            {
                timer!.Stop();
                _oneShots.Remove(timer);
                action();
            });
            _oneShots.Add(timer);
            timer.Start();
        }

        /// <summary>Показати готові дані без ClickUp (офлайн-режим: знімки екрана, перевірка вигляду).</summary>
        public void LoadSnapshot(string userName, List<TaskItem> tasks, RunningTimer? running, TimeSpan finishedToday,
            List<InboxEvent> events, long lastReadMs)
        {
            UserName = userName;
            _allTasks = tasks;
            _tasks = Visible(_allTasks);
            _running = running;
            _finishedToday = finishedToday;
            _lastOk = DateTime.Now;
            _vm.NeedsToken = false;
            _vm.Loaded = true;
            _vm.StateText = "Оновлено о " + _lastOk.Value.ToString("HH:mm");
            _inbox = new InboxState { Events = events, LastReadMs = lastReadMs };
            Render();
            RenderInbox();
        }

        // ───────────────────────────── підключення ─────────────────────────────

        /// <summary>Перевірити новий токен з вікна і підключитися. Повертає текст помилки або null.</summary>
        public Task<string?> ConnectWithNewTokenAsync(string token) => ConnectAsync(token, saveToken: true);

        /// <summary>Перевіряє токен і підключається. Повертає текст помилки або null.</summary>
        private async Task<string?> ConnectAsync(string token, bool saveToken)
        {
            var client = new ClickUpClient(token);
            try
            {
                (long id, string name) = await client.GetUserAsync();
                List<(string Id, string Name, List<Person> Members)> teams = await client.GetTeamsAsync();
                if (teams.Count == 0) { client.Dispose(); return "У цьому акаунті немає жодного робочого простору."; }

                _client?.Dispose();
                _client = client;
                _userId = id;
                UserName = name;
                _teams = teams;
                if (Settings.TeamId == null || teams.All(t => t.Id != Settings.TeamId))
                    Settings.TeamId = teams[0].Id;
                if (saveToken) Settings.Token = token;
                Settings.Save();

                _vm.NeedsToken = false;
                _statusCache.Clear();
                _refreshTimer.Start();
                _pollTimer.Start();
                await RefreshAsync();
                return null;
            }
            catch (ClickUpException ex) when (ex.Unauthorized)
            {
                client.Dispose();
                if (!saveToken) MarkTokenBad();
                return "Токен не підходить. Скопіюй його ще раз у ClickUp → Settings → Apps.";
            }
            catch (Exception ex)
            {
                Log.Write("Connect: " + ex.Message);
                client.Dispose();
                if (!saveToken)
                {
                    // Токен збережений, просто немає інтернету: пробуємо знову за розкладом.
                    _vm.StateText = "Немає зв'язку з ClickUp · спробую ще раз";
                    After(TimeSpan.FromMinutes(1), () => { if (_client == null && Settings.Token is { } t) _ = ConnectAsync(t, false); });
                }
                return "Не вдалося з'єднатися з ClickUp. Перевір інтернет і спробуй ще раз.";
            }
        }

        private void MarkTokenBad()
        {
            _refreshTimer.Stop();
            _pollTimer.Stop();
            _vm.NeedsToken = true;
            _vm.StateText = "Токен більше не діє";
        }

        // ───────────────────────────── дані ─────────────────────────────

        public void RefreshNow()
        {
            _statusCache.Clear();
            if (_client == null && Settings.Token is { } t) _ = ConnectAsync(t, false);
            else _ = RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            if (_client == null || Settings.TeamId == null || _refreshing) return;
            _refreshing = true;
            try
            {
                string team = Settings.TeamId;
                Task<List<TaskItem>> tasks = _client.GetMyTasksAsync(team, _userId);
                Task<RunningTimer?> running = _client.GetRunningAsync(team);
                Task<TimeSpan> finished = _client.GetFinishedTodayAsync(team, DateTime.Now);
                await Task.WhenAll(tasks, running, finished);

                _allTasks = tasks.Result;
                _tasks = Visible(_allTasks);
                _running = running.Result;
                _finishedToday = finished.Result;
                _dataDay = DateTime.Today;
                _lastOk = DateTime.Now;
                _vm.Loaded = true;
                if (DateTime.Now > _stateStickyUntil) _vm.StateText = "Оновлено о " + _lastOk.Value.ToString("HH:mm");
                Render();
                await UpdateInboxAsync();
            }
            catch (ClickUpException ex) when (ex.Unauthorized) { MarkTokenBad(); }
            catch (Exception ex)
            {
                Log.Write("Refresh: " + ex.GetBaseException().Message);
                _vm.StateText = "Немає зв'язку з ClickUp" + (_lastOk != null ? " · дані на " + _lastOk.Value.ToString("HH:mm") : "");
            }
            finally
            {
                _refreshing = false;
            }
        }

        /// <summary>Легка перевірка таймера (його могли запустити в самому ClickUp).</summary>
        private async Task PollTimerAsync()
        {
            if (_client == null || Settings.TeamId == null || _refreshing) return;
            try
            {
                RunningTimer? r = await _client.GetRunningAsync(Settings.TeamId);
                if (r?.TaskId != _running?.TaskId || r?.StartUtc != _running?.StartUtc)
                {
                    _running = r;
                    _finishedToday = await _client.GetFinishedTodayAsync(Settings.TeamId, DateTime.Now);
                    Render();
                }
            }
            catch (Exception ex) { Log.Write("Poll: " + ex.GetBaseException().Message); }
        }

        private void Render()
        {
            DateTime now = DateTime.Now;

            // Таймер зупинили чи перемкнули, поки писали коментар у його картці: картка зміниться, тож поле закриваємо.
            if (_composerHost is TaskRow host && ReferenceEquals(host, _vm.RunningRow) && host.TaskId != _running?.TaskId)
            {
                host.Composer = null;
                _composerHost = null;
            }

            // Задача з таймером показується один раз — зверху, тож у списках її немає.
            List<TaskItem> listed = TaskText.WithoutRunning(_tasks, _running?.TaskId);

            if (_composerHost != null)
            {
                // Людина пише коментар — рядки не перебудовуємо (інакше поле зникне), лише позначки таймера.
                _rowsDirty = true;
                foreach (TaskRow row in _vm.DueRows.Concat(_vm.Groups.SelectMany(g => g.Rows)))
                    row.IsRunning = row.Task.Id == _running?.TaskId;
            }
            else
            {
                _rowsDirty = false;
                _vm.DueRows.Clear();
                foreach (TaskItem t in listed.Where(t => TaskText.IsDueNow(t.Due, now)).OrderBy(t => t.Due))
                    _vm.DueRows.Add(MakeRow(t, now));
                _vm.DueCount = _vm.DueRows.Count;

                _vm.Groups.Clear();
                foreach ((string status, string? color, List<TaskItem> list) in TaskText.Group(listed))
                {
                    var g = new StatusGroup
                    {
                        Name = TaskText.StatusLabel(status),
                        Color = Palette.Status(color),
                        Hollow = string.Equals(list[0].StatusType, "open", StringComparison.OrdinalIgnoreCase),
                    };
                    foreach (TaskItem t in list) g.Rows.Add(MakeRow(t, now));
                    _vm.Groups.Add(g);
                }
                _vm.OpenCount = listed.Count;
            }

            if (_running != null)
            {
                // Шукаємо серед усіх моїх задач (і в схованих статусах); немає — беремо назву з таймера.
                TaskItem task = _allTasks.FirstOrDefault(t => t.Id == _running.TaskId) ?? TaskText.FromTimer(_running);
                (string back, string fore) = Palette.Chip(task.Project);
                _vm.TimerProject = task.Project;
                _vm.TimerChipBack = back;
                _vm.TimerChipFore = fore;
                _vm.TimerTitle = task.Title.Length > 0 ? task.Title : "Без задачі";

                // Рядок для дій у картці таймера (коментар, статус, відкрити). Поки в ньому пишуть — не чіпаємо.
                bool composingHere = _composerHost != null && ReferenceEquals(_composerHost, _vm.RunningRow);
                if (!composingHere) _vm.RunningRow = task.Id.Length > 0 ? MakeRow(task, now) : null;
            }
            else _vm.RunningRow = null;
            _vm.HasTimer = _running != null;
            Tick();
        }

        private TaskRow MakeRow(TaskItem t, DateTime now) => new(t, now)
        {
            IsRunning = t.Id == _running?.TaskId,
            ShowChip = Settings.RowShowProject,
            ShowDue = Settings.RowShowDue,
            ShowPriority = Settings.RowShowPriority,
        };

        /// <summary>Щосекунди: годинник таймера і час за сьогодні.</summary>
        private void Tick()
        {
            DateTime now = DateTime.Now;
            if (_client != null && now.Date != _dataDay && !_refreshing)
            {
                _dataDay = now.Date;
                _ = RefreshAsync();     // новий день: дедлайни й час за сьогодні рахуються заново
            }

            TimeSpan today = _finishedToday;
            if (_running != null)
            {
                DateTime startLocal = _running.StartUtc.ToLocalTime();
                _vm.TimerClock = TaskText.FormatClock(now - startLocal);
                today += now - (startLocal < now.Date ? now.Date : startLocal);
            }
            if (now.Minute != _lastMinute)
            {
                _lastMinute = now.Minute;
                foreach (InboxRow row in _ivm.Rows) row.TimeText = Inbox.Ago(row.Event.AtMs, now);
            }

            if (_vm.Loaded) _vm.TodayText = "Сьогодні відтрековано · " + TaskText.FormatSpan(today);

            string tip = _vm.Loaded ? "ClickUp · задач: " + _tasks.Count : "ClickUp Widget";
            if (_running != null) tip += " · таймер " + _vm.TimerClock;
            _shell.UpdateIcon(_running != null, tip);
        }

        // ───────────────────────────── дії ─────────────────────────────

        public async Task ToggleTimerAsync(TaskItem task)
        {
            if (_running?.TaskId == task.Id) { await StopTimerAsync(); return; }
            if (_client == null || Settings.TeamId == null) return;

            RunningTimer? previous = _running;
            _running = new RunningTimer(task.Id, task.RawName, DateTime.UtcNow);   // одразу показуємо, не чекаючи сервер
            Render();
            try
            {
                if (previous != null) await _client.StopAsync(Settings.TeamId);
                await _client.StartAsync(Settings.TeamId, task.Id);
                _running = await _client.GetRunningAsync(Settings.TeamId) ?? _running;
                _finishedToday = await _client.GetFinishedTodayAsync(Settings.TeamId, DateTime.Now);
            }
            catch (Exception ex)
            {
                Log.Write("Start timer: " + ex.GetBaseException().Message);
                _running = previous;
                _vm.StateText = "Не вдалося запустити таймер";
            }
            Render();
        }

        public async Task StopTimerAsync()
        {
            if (_client == null || Settings.TeamId == null || _running == null) return;
            RunningTimer previous = _running;
            _running = null;
            Render();
            try
            {
                await _client.StopAsync(Settings.TeamId);
                _finishedToday = await _client.GetFinishedTodayAsync(Settings.TeamId, DateTime.Now);
            }
            catch (Exception ex)
            {
                Log.Write("Stop timer: " + ex.GetBaseException().Message);
                _running = previous;
                _vm.StateText = "Не вдалося зупинити таймер";
            }
            Render();
        }

        public async Task<List<StatusOption>> GetStatusesAsync(string listId)
        {
            if (_statusCache.TryGetValue(listId, out List<StatusOption>? cached)) return cached;
            if (_client == null) throw new InvalidOperationException("Не підключено");
            List<StatusOption> list = await _client.GetListStatusesAsync(listId);
            _statusCache[listId] = list;
            return list;
        }

        public async Task SetStatusAsync(TaskItem task, string status)
        {
            if (_client == null) return;
            _selfStatus[task.Id] = status;
            try
            {
                await _client.SetStatusAsync(task.Id, status);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                Log.Write("Status: " + ex.GetBaseException().Message);
                _vm.StateText = "Не вдалося змінити статус";
            }
        }

        // ───────────────────────────── що показувати ─────────────────────────────

        private List<TaskItem> Visible(IEnumerable<TaskItem> tasks) =>
            tasks.Where(t => TaskText.IsVisible(t, Settings.StatusVisibility, Settings.HiddenProjects)).ToList();

        /// <summary>Статуси, які трапляються в моїх задачах (і ті, що я колись налаштовував), у порядку ClickUp.</summary>
        public List<(string Name, string? Color, bool Visible)> KnownStatuses()
        {
            var result = new List<(string, string?, bool)>();
            var seen = new HashSet<string>();
            foreach (TaskItem t in _allTasks.OrderBy(t => t.StatusOrder))
            {
                string key = t.Status.ToLowerInvariant();
                if (key.Length == 0 || !seen.Add(key)) continue;
                bool shown = Settings.StatusVisibility.TryGetValue(key, out bool v) ? v : TaskText.IsOpen(t);
                result.Add((t.Status, t.StatusColor, shown));
            }
            foreach ((string key, bool v) in Settings.StatusVisibility)
                if (seen.Add(key)) result.Add((key, null, v));
            return result;
        }

        /// <summary>Мітки проєктів з моїх задач; порожній рядок — задачі без мітки.</summary>
        public List<(string Project, bool Visible)> KnownProjects()
        {
            var all = _allTasks.Select(t => t.Project).Concat(Settings.HiddenProjects).Distinct()
                .OrderBy(p => p.Length == 0 ? 1 : 0).ThenBy(p => p, StringComparer.CurrentCulture);
            return all.Select(p => (p, !Settings.HiddenProjects.Contains(p))).ToList();
        }

        public void SetStatusVisible(string status, bool visible)
        {
            Change(s => s.StatusVisibility[status.ToLowerInvariant()] = visible);
            ApplyDisplay();
        }

        public void SetProjectVisible(string project, bool visible)
        {
            Change(s =>
            {
                s.HiddenProjects.Remove(project);
                if (!visible) s.HiddenProjects.Add(project);
            });
            ApplyDisplay();
        }

        public void SetDisplay(Action<Settings> change)
        {
            Change(change);
            ApplyDisplay();
        }

        /// <summary>Перемалювати все після зміни «Що показувати».</summary>
        private void ApplyDisplay()
        {
            _tasks = Visible(_allTasks);
            Render();
            RenderInbox();
            _vm.Layout++;
        }

        // ───────────────────────────── тихий інбокс ─────────────────────────────

        private async Task UpdateInboxAsync()
        {
            if (_client == null || Settings.TeamId == null) return;
            try
            {
                if (_inbox.TeamId != Settings.TeamId) _inbox = new InboxState { TeamId = Settings.TeamId };

                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                InboxDiff diff = Inbox.Diff(_inbox, _allTasks, now, _selfStatus, _userId);
                List<InboxEvent> events = diff.Events;
                List<TaskItem> check = diff.CheckComments;

                // Задачі, що зникли з мого списку: дізнаємося, закрили їх, прибрали мене чи видалили.
                foreach (string id in diff.Gone.Take(10))
                {
                    try
                    {
                        TaskItem? fetched = await _client.GetTaskAsync(id);
                        if (Inbox.ResolveGone(_inbox, id, fetched, _userId, now, _selfStatus) is InboxEvent gone) events.Add(gone);
                    }
                    catch (ClickUpException ex) when (ex.Status is 401 or 403)
                    {
                        // Доступу до задачі більше немає (наприклад, забрали зі списку) — тихо забуваємо,
                        // інакше вона перепитувалась би щоразу й затуляла інші зниклі задачі.
                        _inbox.Tasks.Remove(id);
                        Log.Write("Gone " + id + ": no access, forgotten");
                    }
                    catch (Exception ex) { Log.Write("Gone " + id + ": " + ex.GetBaseException().Message); }
                }

                // Коментарі тягнемо лише для задач, які змінювались; за раз не більше 15, решта — наступного разу.
                foreach (TaskItem t in check.Take(15))
                {
                    try
                    {
                        List<CommentInfo> comments = await _client.GetCommentsAsync(t.Id);
                        events.AddRange(Inbox.ApplyComments(_inbox, t, comments, _userId));
                    }
                    catch (Exception ex) { Log.Write("Comments " + t.Id + ": " + ex.GetBaseException().Message); }
                }

                Inbox.Add(_inbox, events);
                Inbox.Save(_inbox);
                RenderInbox();
            }
            catch (Exception ex) { Log.Write("Inbox: " + ex.GetBaseException().Message); }
        }

        private void RenderInbox()
        {
            if (_composerHost is InboxRow)
            {
                _inboxDirty = true;   // у віконці оновлень пишуть коментар — не перебудовуємо
                return;
            }
            _inboxDirty = false;
            DateTime now = DateTime.Now;
            List<InboxEvent> shown = _inbox.Events.Where(e =>
                    Settings.InboxShows(e) && !Settings.HiddenProjects.Contains(e.Project))
                .ToList();
            _ivm.Rows.Clear();
            foreach (InboxEvent e in shown)
                _ivm.Rows.Add(new InboxRow(e) { TimeText = Inbox.Ago(e.AtMs, now), IsUnread = e.AtMs > _inbox.LastReadMs });
            _ivm.Unread = shown.Count(e => e.AtMs > _inbox.LastReadMs);
            _ivm.IsEmpty = shown.Count == 0;
        }

        private void MarkInboxRead()
        {
            long newest = _inbox.Events.Count > 0 ? _inbox.Events.Max(e => e.AtMs) : 0;
            _inbox.LastReadMs = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), newest);
            Inbox.Save(_inbox);
            foreach (InboxRow row in _ivm.Rows) row.IsUnread = false;
            _ivm.Unread = 0;
        }

        // ───────────────────────────── коментарі ─────────────────────────────

        /// <summary>Відкрити поле коментаря під рядком (або закрити, якщо вже відкрите там).</summary>
        public void ToggleComposer(IComposerHost host, bool replyToAuthor = false)
        {
            bool same = ReferenceEquals(_composerHost, host);
            CloseComposer();
            if (same) return;

            Person? reply = null;
            if (replyToAuthor && host is InboxRow ir && ir.Event.WhoId > 0 && ir.Event.WhoId != _userId)
            {
                string name = ir.Event.Who;
                reply = AllPeople().FirstOrDefault(p => p.Id == ir.Event.WhoId)
                        ?? new Person(ir.Event.WhoId, name, "", null, name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?");
            }
            _composerHost = host;
            host.Composer = new ComposerModel { TaskId = host.TaskId, TaskTitle = host.TaskTitle, ReplyTo = reply };
        }

        public void CloseComposer()
        {
            if (_composerHost == null) return;
            _composerHost.Composer = null;
            _composerHost = null;
            if (_rowsDirty) Render();
            if (_inboxDirty) RenderInbox();
        }

        private List<Person> AllPeople() =>
            _teams.FirstOrDefault(t => t.Id == Settings.TeamId).Members ?? new List<Person>();

        /// <summary>Люди для @: учасники задачі (кеш 10 хв), усі в робочому просторі, мій id.</summary>
        public async Task<(List<Person> Task, List<Person> All, long Me)> GetMentionPeopleAsync(string taskId)
        {
            List<Person> all = AllPeople();
            if (_client == null) return (new List<Person>(), all, _userId);
            if (!_taskPeople.TryGetValue(taskId, out var cached) || DateTime.Now - cached.At > TimeSpan.FromMinutes(10))
            {
                try { cached = (DateTime.Now, await _client.GetTaskMembersAsync(taskId)); }
                catch (Exception ex)
                {
                    Log.Write("Task members: " + ex.GetBaseException().Message);
                    cached = (DateTime.Now, new List<Person>());
                }
                _taskPeople[taskId] = cached;
            }
            return (cached.People, all, _userId);
        }

        /// <summary>Надіслати коментар. Повертає текст помилки або null.</summary>
        public async Task<string?> SendCommentAsync(ComposerModel model)
        {
            if (_client == null) return "Немає підключення до ClickUp";
            string text = model.Text.Trim();
            if (text.Length == 0) return null;
            try
            {
                await _client.PostCommentAsync(model.TaskId, CommentText.Build(text, model.Mentions));
                Log.Write("Comment sent to " + model.TaskId);
                CloseComposer();
                SetState("Коментар надіслано", 10);
                return null;
            }
            catch (Exception ex)
            {
                Log.Write("Comment: " + ex.GetBaseException().Message);
                return "Не вдалося надіслати. Спробуй ще раз.";
            }
        }

        // ───────────────────────────── вікна й налаштування ─────────────────────────────

        public void SetInboxVisible(bool visible)
        {
            Change(s => s.ShowInbox = visible);
            if (visible) _shell.ShowInbox();
            else _shell.HideInbox();
        }

        public void SetPanelVisible(bool visible)
        {
            Change(s => s.ShowPanel = visible);
            if (visible) _shell.ShowPanel();
            else _shell.HidePanel();
        }

        public void Change(Action<Settings> change)
        {
            change(Settings);
            Settings.Save();
        }

        public void SetTeam(string teamId)
        {
            CloseComposer();
            Change(s => s.TeamId = teamId);
            _statusCache.Clear();
            _running = null;
            _tasks = new List<TaskItem>();
            _allTasks = new List<TaskItem>();
            Render();
            _ = RefreshAsync();
        }

        public void SetRefreshMinutes(int minutes)
        {
            Change(s => s.RefreshMinutes = minutes);
            _refreshTimer.Interval = TimeSpan.FromMinutes(minutes);
        }

        public void SetTheme(bool light)
        {
            Change(s => s.Theme = light ? "Light" : "Dark");
            Palette.IsLight = light;
            _shell.ApplyTheme(light);
            Render();   // кольори міток перераховуються під тему
            RenderInbox();
            _vm.Layout++;
        }

        public void SetAutostart(bool enabled)
        {
            Change(s => s.Autostart = enabled);
            _shell.SetAutostart(enabled, userAction: true);
        }

        public void OpenClickUp() => OpenUrl("https://app.clickup.com/" + (Settings.TeamId ?? ""));

        public void OpenUrl(string url)
        {
            if (!string.IsNullOrWhiteSpace(url)) _shell.OpenUrl(url);
        }

        // ───────────────────────────── меню ─────────────────────────────

        /// <summary>Меню (правий клік, ⋯, значок): ті самі пункти, що й у Windows-версії.</summary>
        public List<MenuNode> BuildMenu()
        {
            Settings s = Settings;
            var menu = new List<MenuNode>
            {
                MenuNode.Item("Оновити зараз", RefreshNow),
                MenuNode.Item("Відкрити ClickUp", OpenClickUp),
                MenuNode.Separator(),
                MenuNode.Toggle("Панель на робочому столі", s.ShowPanel, SetPanelVisible),
                MenuNode.Toggle("Віконце оновлень", s.ShowInbox, SetInboxVisible),
                BuildDisplayMenu(s),
                MenuNode.Toggle("Закріпити положення віконець", s.LockPanel, v => Change(x => x.LockPanel = v)),
            };

            IReadOnlyList<(string Id, string Name)> teams = Teams;
            if (teams.Count > 1)
            {
                string current = teams.FirstOrDefault(t => t.Id == s.TeamId).Name ?? "";
                MenuNode ws = MenuNode.Sub("Робочий простір" + (current.Length > 0 ? "   ·  " + current : ""));
                foreach ((string id, string name) in teams)
                {
                    string captured = id;
                    ws.Items.Add(MenuNode.Radio(name, id == s.TeamId, () => SetTeam(captured)));
                }
                menu.Add(ws);
            }

            MenuNode refresh = MenuNode.Sub("Оновлювати задачі");
            foreach ((string label, int minutes) in new[] { ("Щохвилини", 1), ("Кожні 2 хв", 2), ("Кожні 5 хв", 5), ("Кожні 15 хв", 15) })
            {
                int m = minutes;
                refresh.Items.Add(MenuNode.Radio(label, s.RefreshMinutes == m, () => SetRefreshMinutes(m)));
            }
            menu.Add(refresh);

            MenuNode theme = MenuNode.Sub("Тема");
            theme.Items.Add(MenuNode.Radio("Темна", s.Theme != "Light", () => SetTheme(false)));
            theme.Items.Add(MenuNode.Radio("Світла", s.Theme == "Light", () => SetTheme(true)));
            menu.Add(theme);

            menu.Add(MenuNode.Toggle("Запускати при вході", s.Autostart && !_shell.AutostartDisabledBySystem, SetAutostart));
            menu.Add(MenuNode.Separator());

            string who = UserName.Length > 0 ? "   ·  " + UserName : "";
            menu.Add(MenuNode.Item((s.HasToken ? "Змінити токен…" : "Підключити ClickUp…") + who, _shell.ShowTokenWindow));
            string? pending = PendingUpdateVersion;
            menu.Add(MenuNode.Item(pending != null ? "Оновити до " + pending + " і перезапустити" : "Перевірити оновлення", CheckUpdatesManual));
            menu.Add(MenuNode.Info("Версія " + Updater.CurrentVersion));
            menu.Add(MenuNode.Item("Відкрити папку налаштувань", () => _shell.OpenFolder(Settings.DataDir)));
            menu.Add(MenuNode.Separator());
            menu.Add(MenuNode.Item("Вийти", Exit));
            return menu;
        }

        /// <summary>Підменю «Що показувати»: розділи панелі, картка, інбокс, статуси, проєкти.</summary>
        private MenuNode BuildDisplayMenu(Settings s)
        {
            MenuNode root = MenuNode.Sub("Що показувати");

            MenuNode panel = MenuNode.Sub("Панель на столі");
            panel.Items.Add(MenuNode.Keep("Час за сьогодні", s.ShowTodayTime, v => SetDisplay(x => x.ShowTodayTime = v)));
            panel.Items.Add(MenuNode.Keep("Таймер", s.ShowTimer, v => SetDisplay(x => x.ShowTimer = v)));
            panel.Items.Add(MenuNode.Keep("Сьогодні (дедлайни)", s.ShowDueSection, v => SetDisplay(x => x.ShowDueSection = v)));
            panel.Items.Add(MenuNode.Keep("Усі відкриті задачі", s.ShowAllSection, v => SetDisplay(x => x.ShowAllSection = v)));
            panel.Items.Add(MenuNode.Separator());
            panel.Items.Add(MenuNode.Info("У рядку задачі"));
            panel.Items.Add(MenuNode.Keep("Мітка проєкту", s.RowShowProject, v => SetDisplay(x => x.RowShowProject = v)));
            panel.Items.Add(MenuNode.Keep("Дедлайн", s.RowShowDue, v => SetDisplay(x => x.RowShowDue = v)));
            panel.Items.Add(MenuNode.Keep("Пріоритет (прапорець)", s.RowShowPriority, v => SetDisplay(x => x.RowShowPriority = v)));
            root.Items.Add(panel);

            MenuNode flyout = MenuNode.Sub(OperatingSystem.IsMacOS() ? "Картка з рядка меню" : "Картка з трею");
            flyout.Items.Add(MenuNode.Keep("Таймер", s.FlyoutShowTimer, v => SetDisplay(x => x.FlyoutShowTimer = v)));
            flyout.Items.Add(MenuNode.Keep("Сьогодні (дедлайни)", s.FlyoutShowDue, v => SetDisplay(x => x.FlyoutShowDue = v)));
            root.Items.Add(flyout);

            MenuNode inbox = MenuNode.Sub("Віконце оновлень");
            inbox.Items.Add(MenuNode.Keep("Коментарі", s.InboxComments, v => SetDisplay(x => x.InboxComments = v)));
            inbox.Items.Add(MenuNode.Keep("Згадки мене (@)", s.InboxMentions, v => SetDisplay(x => x.InboxMentions = v)));
            inbox.Items.Add(MenuNode.Keep("Мої власні коментарі", s.InboxOwnComments, v => SetDisplay(x => x.InboxOwnComments = v)));
            inbox.Items.Add(MenuNode.Separator());
            inbox.Items.Add(MenuNode.Keep("Зміни статусів", s.InboxStatuses, v => SetDisplay(x => x.InboxStatuses = v)));
            inbox.Items.Add(MenuNode.Keep("Закриті й видалені задачі", s.InboxClosed, v => SetDisplay(x => x.InboxClosed = v)));
            inbox.Items.Add(MenuNode.Keep("Зміни дедлайну", s.InboxDue, v => SetDisplay(x => x.InboxDue = v)));
            inbox.Items.Add(MenuNode.Keep("Зміни пріоритету", s.InboxPriority, v => SetDisplay(x => x.InboxPriority = v)));
            inbox.Items.Add(MenuNode.Keep("Перейменування", s.InboxRenamed, v => SetDisplay(x => x.InboxRenamed = v)));
            inbox.Items.Add(MenuNode.Separator());
            inbox.Items.Add(MenuNode.Keep("Нові задачі на мене (призначення)", s.InboxAssigned, v => SetDisplay(x => x.InboxAssigned = v)));
            inbox.Items.Add(MenuNode.Keep("Мене прибрали з задачі", s.InboxUnassigned, v => SetDisplay(x => x.InboxUnassigned = v)));
            inbox.Items.Add(MenuNode.Keep("Зміни інших виконавців", s.InboxAssignees, v => SetDisplay(x => x.InboxAssignees = v)));
            root.Items.Add(inbox);
            root.Items.Add(MenuNode.Separator());

            MenuNode statuses = MenuNode.Sub("Статуси");
            List<(string Name, string? Color, bool Visible)> known = KnownStatuses();
            if (known.Count == 0) statuses.Items.Add(MenuNode.Info("Задачі ще завантажуються…"));
            foreach (var (name, _, visible) in known)
            {
                string captured = name;
                statuses.Items.Add(MenuNode.Keep(TaskText.StatusLabel(name), visible, v => SetStatusVisible(captured, v)));
            }
            root.Items.Add(statuses);

            MenuNode projects = MenuNode.Sub("Проєкти");
            List<(string Project, bool Visible)> projectList = KnownProjects();
            if (projectList.Count == 0) projects.Items.Add(MenuNode.Info("Задачі ще завантажуються…"));
            foreach ((string project, bool visible) in projectList)
            {
                string captured = project;
                projects.Items.Add(MenuNode.Keep(project.Length > 0 ? project : "Без мітки", visible, v => SetProjectVisible(captured, v)));
            }
            root.Items.Add(projects);

            return root;
        }

        // ───────────────────────────── оновлення програми ─────────────────────────────

        /// <summary>Повідомлення в нижньому рядку, яке якийсь час не перетирається.</summary>
        private void SetState(string text, int stickySeconds)
        {
            _vm.StateText = text;
            _stateStickyUntil = DateTime.Now.AddSeconds(stickySeconds);
        }

        /// <summary>«Перевірити оновлення» з меню: показуємо кожен крок і одразу ставимо.</summary>
        public void CheckUpdatesManual()
        {
            if (_pendingUpdate != null) { ApplyUpdate(silent: false); return; }
            _ = CheckUpdatesAsync(true);
        }

        /// <summary>
        /// Перевірка оновлень. Фонова (двічі на день) — повністю тиха: знайшли → завантажили →
        /// встановили без вікон і перезапустилися. Якщо людина саме пише коментар — відкладаємо.
        /// </summary>
        private async Task CheckUpdatesAsync(bool manual)
        {
            if (_checkingUpdates)
            {
                if (manual) SetState("Вже перевіряю оновлення…", 60);
                return;
            }
            if (!Updater.IsInstalled)
            {
                if (manual) SetState("Оновлення працюють лише у встановленій версії", 60);
                return;
            }

            _checkingUpdates = true;
            try
            {
                if (manual) SetState("Перевіряю оновлення…", 120);
                UpdateInfo? info = await Updater.CheckAsync();
                if (info == null)
                {
                    Log.Write("Update: up to date (" + Updater.CurrentVersion + ")");
                    if (manual) SetState("У тебе остання версія " + Updater.CurrentVersion, 60);
                    return;
                }

                string version = Updater.VersionOf(info);
                Log.Write("Update: found " + version);
                if (manual) SetState("Завантажую " + version + "…", 600);
                await Updater.DownloadAsync(info);
                _pendingUpdate = info;
                Log.Write("Update: downloaded " + version);

                if (manual)
                {
                    SetState("Встановлюю " + version + "…", 60);
                    ApplyUpdate(silent: false);
                }
                else
                    TryApplySilently();
            }
            catch (Exception ex)
            {
                Log.Write("Update: " + ex);
                if (manual) SetState("Не вдалося перевірити оновлення", 60);
            }
            finally
            {
                _checkingUpdates = false;
            }
        }

        /// <summary>Тихо встановити завантажене оновлення, якщо людина зараз нічим не зайнята у віджеті.</summary>
        private void TryApplySilently()
        {
            if (_pendingUpdate == null) return;
            if (_composerHost != null || _shell.IsBusy)
            {
                Log.Write("Update: postponed (user is busy)");
                _applyRetry.Start();   // спробуємо через 10 хв
                return;
            }
            _applyRetry.Stop();
            ApplyUpdate(silent: true);
        }

        private void ApplyUpdate(bool silent)
        {
            if (_pendingUpdate == null) return;
            try
            {
                Inbox.Save(_inbox);
                Settings.Save();
                Log.Write("Update: applying " + Updater.VersionOf(_pendingUpdate) + (silent ? " silently" : ""));
                if (silent)
                {
                    // Оновлювач чекає, поки програма закриється, ставить нову версію без вікон і запускає її знову.
                    Updater.ApplySilentlyOnExit(_pendingUpdate);
                    _shell.Shutdown();
                }
                else
                    Updater.ApplyAndRestart(_pendingUpdate);
            }
            catch (Exception ex)
            {
                Log.Write("Apply update: " + ex);
                _vm.UpdateText = "Оновити до " + Updater.VersionOf(_pendingUpdate) + " ↻";
                if (!silent) SetState("Не вдалося встановити оновлення", 60);
            }
        }

        public void Exit() => _shell.Shutdown();

        public void Dispose()
        {
            _refreshTimer.Stop();
            _pollTimer.Stop();
            _tickTimer.Stop();
            _updateTimer.Stop();
            _applyRetry.Stop();
            foreach (UiTimer t in _oneShots.ToList()) t.Stop();
            _client?.Dispose();
            Inbox.Save(_inbox);
            Current = null;
        }
    }
}
