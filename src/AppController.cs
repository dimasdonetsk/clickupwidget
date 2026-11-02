using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Velopack;

namespace ClickUpWidget
{
    /// <summary>Мозок програми: тягне дані з ClickUp, оновлює панель, флайаут і значок у треї.</summary>
    public sealed class AppController : IDisposable
    {
        public static AppController? Current { get; private set; }

        public Settings Settings { get; private set; } = new();
        public IReadOnlyList<(string Id, string Name)> Teams => _teams;
        public string UserName { get; private set; } = "";

        private readonly DashboardViewModel _vm = new();
        private TrayController? _tray;
        private PanelWindow? _panel;
        private FlyoutWindow? _flyout;
        private TokenWindow? _tokenWindow;
        private DateTime _lastFlyoutClosed;

        private ClickUpClient? _client;
        private long _userId;
        private List<(string Id, string Name)> _teams = new();

        private List<TaskItem> _tasks = new();
        private RunningTimer? _running;
        private TimeSpan _finishedToday;
        private DateTime _dataDay = DateTime.Today;
        private DateTime? _lastOk;
        private bool _refreshing;
        private readonly Dictionary<string, List<StatusOption>> _statusCache = new();

        private readonly DispatcherTimer _refreshTimer = new();
        private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(45) };
        private readonly DispatcherTimer _tickTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };
        private UpdateInfo? _pendingUpdate;

        public void Start()
        {
            Current = this;
            Settings = Settings.Load();
            Theme.Apply(Settings.Theme == "Light");
            if (Settings.Autostart) AutostartHelper.Apply(true);

            _vm.OpenTask = new RelayCommand(p => { if (p is TaskRow r) OpenUrl(r.Task.Url); });
            _vm.ToggleTimer = new RelayCommand(p => { if (p is TaskRow r) _ = ToggleTimerAsync(r.Task); });
            _vm.StopTimer = new RelayCommand(() => _ = StopTimerAsync());
            _vm.Refresh = new RelayCommand(RefreshNow);
            _vm.ShowMenu = new RelayCommand(ShowMenuAtCursor);
            _vm.OpenClickUp = new RelayCommand(OpenClickUp);
            _vm.AddToken = new RelayCommand(ShowTokenWindow);
            _vm.ApplyUpdate = new RelayCommand(ApplyUpdate);

            _tray = new TrayController(this);
            if (Settings.ShowPanel) ShowPanel();

            _refreshTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, Settings.RefreshMinutes));
            _refreshTimer.Tick += (_, _) => _ = RefreshAsync();
            _pollTimer.Tick += (_, _) => _ = PollTimerAsync();
            _tickTimer.Tick += (_, _) => Tick();
            _updateTimer.Tick += (_, _) => _ = CheckUpdatesAsync(false);
            _tickTimer.Start();

            string? token = Settings.Token;
            if (token == null)
            {
                _vm.NeedsToken = true;
                _vm.StateText = "Не підключено";
                ShowTokenWindow();
            }
            else
                _ = ConnectAsync(token, saveToken: false);

            // Перевірка оновлень трохи згодом, щоб не гальмувати запуск.
            var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(40) };
            first.Tick += (_, _) => { first.Stop(); _ = CheckUpdatesAsync(false); _updateTimer.Start(); };
            first.Start();
        }

        // ───────────────────────────── підключення ─────────────────────────────

        /// <summary>Перевіряє токен і підключається. Повертає текст помилки або null.</summary>
        private async Task<string?> ConnectAsync(string token, bool saveToken)
        {
            var client = new ClickUpClient(token);
            try
            {
                (long id, string name) = await client.GetUserAsync();
                List<(string, string)> teams = await client.GetTeamsAsync();
                if (teams.Count == 0) { client.Dispose(); return "У цьому акаунті немає жодного робочого простору."; }

                _client?.Dispose();
                _client = client;
                _userId = id;
                UserName = name;
                _teams = teams;
                if (Settings.TeamId == null || teams.All(t => t.Item1 != Settings.TeamId))
                    Settings.TeamId = teams[0].Item1;
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
                    var retry = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
                    retry.Tick += (_, _) => { retry.Stop(); if (_client == null && Settings.Token is { } t) _ = ConnectAsync(t, false); };
                    retry.Start();
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

        public void ShowTokenWindow()
        {
            if (_tokenWindow != null) { _tokenWindow.Activate(); return; }
            _tokenWindow = new TokenWindow(t => ConnectAsync(t, saveToken: true));
            _tokenWindow.Closed += (_, _) => _tokenWindow = null;
            _tokenWindow.Show();
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

                _tasks = tasks.Result;
                _running = running.Result;
                _finishedToday = finished.Result;
                _dataDay = DateTime.Today;
                _lastOk = DateTime.Now;
                _vm.Loaded = true;
                _vm.StateText = "Оновлено о " + _lastOk.Value.ToString("HH:mm");
                Render();
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

            _vm.DueRows.Clear();
            foreach (TaskItem t in _tasks.Where(t => TaskText.IsDueNow(t.Due, now)).OrderBy(t => t.Due))
                _vm.DueRows.Add(new TaskRow(t, now) { IsRunning = t.Id == _running?.TaskId });
            _vm.DueCount = _vm.DueRows.Count;

            _vm.Groups.Clear();
            foreach ((string status, string? color, List<TaskItem> list) in TaskText.Group(_tasks))
            {
                var g = new StatusGroup
                {
                    Name = TaskText.StatusLabel(status),
                    Brush = Theme.StatusBrush(color),
                    Hollow = string.Equals(list[0].StatusType, "open", StringComparison.OrdinalIgnoreCase),
                };
                foreach (TaskItem t in list)
                    g.Rows.Add(new TaskRow(t, now) { IsRunning = t.Id == _running?.TaskId });
                _vm.Groups.Add(g);
            }
            _vm.OpenCount = _tasks.Count;

            if (_running != null)
            {
                TaskItem? task = _tasks.FirstOrDefault(t => t.Id == _running.TaskId);
                (string project, string title) = task != null ? (task.Project, task.Title) : TaskText.Split(_running.TaskName);
                (System.Windows.Media.Brush back, System.Windows.Media.Brush fore) = Theme.Chip(project);
                _vm.TimerProject = project;
                _vm.TimerChipBack = back;
                _vm.TimerChipFore = fore;
                _vm.TimerTitle = title.Length > 0 ? title : "Без задачі";
            }
            _vm.HasTimer = _running != null;
            Tick();
        }

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
            if (_vm.Loaded) _vm.TodayText = "Сьогодні відтрековано · " + TaskText.FormatSpan(today);

            string tip = _vm.Loaded ? "ClickUp · задач: " + _tasks.Count : "ClickUp Widget";
            if (_running != null) tip += " · таймер " + _vm.TimerClock;
            _tray?.Update(_running != null, tip);
        }

        // ───────────────────────────── дії ─────────────────────────────

        private async Task ToggleTimerAsync(TaskItem task)
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

        private async Task StopTimerAsync()
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

        // ───────────────────────────── вікна ─────────────────────────────

        private void ShowPanel()
        {
            if (_panel != null) return;
            _panel = new PanelWindow(this, _vm);
            _panel.Closed += (_, _) => _panel = null;
            _panel.Show();
        }

        public void SetPanelVisible(bool visible)
        {
            Change(s => s.ShowPanel = visible);
            if (visible) ShowPanel();
            else _panel?.Close();
        }

        public void ToggleFlyout(System.Drawing.Point anchorPx)
        {
            if (_flyout != null) { _flyout.RequestClose(); return; }
            // Клік по значку спершу закриває вікно (воно втрачає фокус), тож одразу знову не відкриваємо.
            if ((DateTime.Now - _lastFlyoutClosed).TotalMilliseconds < 400) return;

            _flyout = new FlyoutWindow(_vm);
            _flyout.Closed += (_, _) =>
            {
                _flyout = null;
                _lastFlyoutClosed = DateTime.Now;
                Native.TrimMemory();
            };
            _flyout.ShowAt(anchorPx);
        }

        public void ShowMenuAtCursor() => _tray?.ShowMenuAtCursor();

        // ───────────────────────────── налаштування ─────────────────────────────

        public void Change(Action<Settings> change)
        {
            change(Settings);
            Settings.Save();
        }

        public void SetTeam(string teamId)
        {
            Change(s => s.TeamId = teamId);
            _statusCache.Clear();
            _running = null;
            _tasks = new List<TaskItem>();
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
            Theme.Apply(light);
            Render();   // кольори міток перераховуються під тему
        }

        public void SetAutostart(bool enabled)
        {
            Change(s => s.Autostart = enabled);
            AutostartHelper.Apply(enabled, userAction: true);
        }

        public void OpenClickUp() => OpenUrl("https://app.clickup.com/" + (Settings.TeamId ?? ""));

        private static void OpenUrl(string url)
        {
            if (!string.IsNullOrWhiteSpace(url)) TrayController.Open(url);
        }

        // ───────────────────────────── оновлення програми ─────────────────────────────

        public void CheckUpdatesManual() => _ = CheckUpdatesAsync(true);

        private async Task CheckUpdatesAsync(bool manual)
        {
            if (!Updater.IsInstalled)
            {
                if (manual) _vm.StateText = "Оновлення працюють лише у встановленій версії";
                return;
            }
            try
            {
                UpdateInfo? info = await Updater.CheckAsync();
                if (info == null)
                {
                    if (manual) _vm.StateText = "Встановлена остання версія " + Updater.CurrentVersion;
                    return;
                }
                await Updater.DownloadAsync(info);
                _pendingUpdate = info;
                // Тихо: без сповіщень, лише рядок унизу панелі.
                _vm.UpdateText = "Оновити до " + Updater.VersionOf(info) + " ↻";
            }
            catch (Exception ex)
            {
                Log.Write("Update: " + ex.Message);
                if (manual) _vm.StateText = "Не вдалося перевірити оновлення";
            }
        }

        private void ApplyUpdate()
        {
            if (_pendingUpdate == null) return;
            try { Updater.ApplyAndRestart(_pendingUpdate); }
            catch (Exception ex) { Log.Write("Apply update: " + ex.Message); }
        }

        public void Exit() => Application.Current.Shutdown();

        public void Dispose()
        {
            _refreshTimer.Stop();
            _pollTimer.Stop();
            _tickTimer.Stop();
            _updateTimer.Stop();
            _tray?.Dispose();
            _client?.Dispose();
            Current = null;
        }
    }
}
