using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using ClickUpWidget.Shared;

namespace ClickUpWidget.Mac
{
    /// <summary>
    /// Mac-«оболонка» для спільного мозку (WidgetController): вікна, значок у рядку меню, меню, автозапуск.
    /// Аналог AppController + TrayController з Windows-версії.
    /// </summary>
    public sealed class MacShell : IWidgetShell
    {
        public static MacShell? Current { get; private set; }

        private readonly IClassicDesktopStyleApplicationLifetime _life;
        public WidgetController Controller { get; }

        private PanelWindow? _panel;
        private InboxWindow? _inbox;
        private FlyoutWindow? _flyout;
        private TokenWindow? _token;
        private DateTime _lastFlyoutClosed;
        /// <summary>Вікно, де востаннє клацнули, — біля нього відкривається меню (⋯ або правий клік).</summary>
        private Window? _menuHost;

        private TrayIcon? _tray;
        private NativeMenu? _trayMenu;
        private bool? _iconRunning;
        private bool _shutDown;

        public MacShell(IClassicDesktopStyleApplicationLifetime life, bool offline = false)
        {
            _life = life;
            Current = this;
            Controller = new WidgetController(this) { Offline = offline };
        }

        /// <summary>Відкриті вікна (для знімків екрана в демо).</summary>
        public IEnumerable<Window> Windows =>
            new Window?[] { _panel, _inbox, _flyout, _token }.Where(w => w != null).Cast<Window>();

        public void Start()
        {
            Controller.Start();
            CreateTray();
            Controller.Dashboard.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(DashboardViewModel.Loaded) or nameof(DashboardViewModel.NeedsToken)
                    or nameof(DashboardViewModel.Layout) or nameof(DashboardViewModel.HasTimer) or nameof(DashboardViewModel.TimerTitle))
                    RebuildTrayMenu();
            };
        }

        public void Post(Action action) => Dispatcher.UIThread.Post(action);

        public void ApplyTheme(bool light) => (Application.Current as App)?.ApplyTheme(light);

        // ───────────────────────────── вікна ─────────────────────────────

        public void ShowPanel()
        {
            if (_panel != null) return;
            _panel = new PanelWindow(this);
            _panel.Closed += (_, _) => _panel = null;
            Track(_panel);
            _panel.Show();
        }

        public void HidePanel() => _panel?.Close();

        public void ShowInbox()
        {
            if (_inbox != null) return;
            _inbox = new InboxWindow(this);
            _inbox.Closed += (_, _) => _inbox = null;
            Track(_inbox);
            _inbox.Show();
        }

        public void HideInbox() => _inbox?.Close();

        public void ShowTokenWindow()
        {
            if (_token != null) { _token.Activate(); MacNative.ActivateApp(); return; }
            _token = new TokenWindow(Controller.ConnectWithNewTokenAsync);
            _token.Closed += (_, _) => _token = null;
            _token.Show();
            _token.Activate();
            MacNative.ActivateApp();
        }

        public bool IsBusy => _flyout != null || _token != null;

        /// <summary>Картка задач (таймер і «Сьогодні») під рядком меню — з пункту меню значка.</summary>
        public void ToggleFlyout()
        {
            if (_flyout != null) { _flyout.RequestClose(); return; }
            if ((DateTime.Now - _lastFlyoutClosed).TotalMilliseconds < 400) return;

            _flyout = new FlyoutWindow(this);
            _flyout.Closed += (_, _) =>
            {
                _flyout = null;
                _lastFlyoutClosed = DateTime.Now;
            };
            Track(_flyout);
            _flyout.ShowNearMenuBar();
        }

        /// <summary>Запам'ятовуємо вікно, в якому клацнули: меню відкриється біля курсора в ньому.</summary>
        private void Track(Window window)
        {
            window.AddHandler(InputElement.PointerPressedEvent, (_, _) => _menuHost = window,
                Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            window.Closed += (_, _) => { if (_menuHost == window) _menuHost = null; };
        }

        // ───────────────────────────── меню ─────────────────────────────

        /// <summary>Меню біля курсора (⋯ або правий клік у вікні). Не виходить за межі екрана — Avalonia зсуває його сама.</summary>
        public void ShowMenu()
        {
            Window? host = _menuHost ?? _flyout ?? _panel ?? (Window?)_inbox;
            if (host == null) return;
            var menu = new ContextMenu
            {
                ItemsSource = Controller.BuildMenu().Select(ToMenuItem).ToList(),
                Placement = PlacementMode.Pointer,
            };
            host.ContextMenu = menu;
            menu.Closed += (_, _) => { if (host.ContextMenu == menu) host.ContextMenu = null; };
            menu.Open(host);
        }

        private Control ToMenuItem(MenuNode node)
        {
            switch (node.Kind)
            {
                case MenuKind.Separator:
                    return new Separator();
                case MenuKind.Info:
                    return new MenuItem { Header = node.Text, IsEnabled = false };
                case MenuKind.Submenu:
                    return new MenuItem { Header = node.Text, ItemsSource = node.Items.Select(ToMenuItem).ToList() };
                case MenuKind.Toggle:
                {
                    var item = new MenuItem
                    {
                        Header = node.Text,
                        ToggleType = MenuItemToggleType.CheckBox,
                        IsChecked = node.Checked,
                        // Галочки «Що показувати» не закривають меню — можна поставити кілька підряд.
                        StaysOpenOnClick = node.KeepOpen,
                    };
                    item.Click += (_, _) =>
                    {
                        node.Invoke();
                        item.IsChecked = node.Checked;
                        RebuildTrayMenu();
                    };
                    return item;
                }
                default:
                {
                    var item = new MenuItem
                    {
                        Header = node.Text,
                        ToggleType = node.Checked ? MenuItemToggleType.Radio : MenuItemToggleType.None,
                        IsChecked = node.Checked,
                    };
                    item.Click += (_, _) => { node.Invoke(); RebuildTrayMenu(); };
                    return item;
                }
            }
        }

        // ───────────────────────────── значок у рядку меню ─────────────────────────────

        private void CreateTray()
        {
            _trayMenu = new NativeMenu();
            _trayMenu.NeedsUpdate += (_, _) => FillTrayMenu();
            FillTrayMenu();
            _tray = new TrayIcon
            {
                Icon = Icons.Tray(false),
                ToolTipText = "ClickUp Widget",
                Menu = _trayMenu,
                IsVisible = true,
            };
            // Шаблонний значок: macOS сам фарбує його під світлий/темний рядок меню.
            if (OperatingSystem.IsMacOS()) MacOSProperties.SetIsTemplateIcon(_tray, true);
            // На Windows (перевірка вигляду) клік по значку теж відкриває картку.
            _tray.Clicked += (_, _) => ToggleFlyout();
            TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
            _iconRunning = false;
        }

        private void RebuildTrayMenu() => Post(FillTrayMenu);

        /// <summary>
        /// Меню значка. На Mac клік по значку в рядку меню завжди відкриває меню (Avalonia не дає окремого кліку),
        /// тож картка задач — першим пунктом, а таймер видно й зупиняється просто звідси.
        /// </summary>
        private void FillTrayMenu()
        {
            if (_trayMenu == null) return;
            _trayMenu.Items.Clear();

            var card = new NativeMenuItem("Задачі зараз…");
            card.Click += (_, _) => Post(ToggleFlyout);
            _trayMenu.Items.Add(card);

            DashboardViewModel vm = Controller.Dashboard;
            if (vm.HasTimer)
            {
                string title = vm.TimerTitle.Length > 48 ? vm.TimerTitle.Substring(0, 47) + "…" : vm.TimerTitle;
                var timer = new NativeMenuItem("Таймер · " + (vm.HasTimerProject ? vm.TimerProject + " · " : "") + title);
                timer.Click += (_, _) => { if (vm.RunningRow != null) vm.OpenTask.Execute(vm.RunningRow); };
                _trayMenu.Items.Add(timer);
                var stop = new NativeMenuItem("Зупинити таймер");
                stop.Click += (_, _) => vm.StopTimer.Execute(null);
                _trayMenu.Items.Add(stop);
            }
            _trayMenu.Items.Add(new NativeMenuItemSeparator());

            foreach (MenuNode node in Controller.BuildMenu())
                _trayMenu.Items.Add(ToNative(node));
        }

        private NativeMenuItemBase ToNative(MenuNode node)
        {
            switch (node.Kind)
            {
                case MenuKind.Separator:
                    return new NativeMenuItemSeparator();
                case MenuKind.Info:
                    return new NativeMenuItem(node.Text) { IsEnabled = false };
                case MenuKind.Submenu:
                {
                    var sub = new NativeMenu();
                    foreach (MenuNode child in node.Items) sub.Items.Add(ToNative(child));
                    return new NativeMenuItem(node.Text) { Menu = sub };
                }
                case MenuKind.Toggle:
                {
                    var item = new NativeMenuItem(node.Text) { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = node.Checked };
                    item.Click += (_, _) => { node.Invoke(); RebuildTrayMenu(); };
                    return item;
                }
                default:
                {
                    var item = new NativeMenuItem(node.Text);
                    if (node.Checked)
                    {
                        item.ToggleType = NativeMenuItemToggleType.Radio;
                        item.IsChecked = true;
                    }
                    item.Click += (_, _) => { node.Invoke(); RebuildTrayMenu(); };
                    return item;
                }
            }
        }

        public void UpdateIcon(bool timerRunning, string tooltip)
        {
            if (_tray == null) return;
            if (_tray.ToolTipText != tooltip) _tray.ToolTipText = tooltip;
            if (_iconRunning != timerRunning)
            {
                _tray.Icon = Icons.Tray(timerRunning);
                _iconRunning = timerRunning;
                RebuildTrayMenu();
            }
        }

        // ───────────────────────────── система ─────────────────────────────

        public void OpenUrl(string url) => MacNative.Open(url);

        public void OpenFolder(string path)
        {
            try { Directory.CreateDirectory(path); } catch { }
            MacNative.Open(path);
        }

        public void SetAutostart(bool enabled, bool userAction) => LoginItem.Apply(enabled);

        public bool AutostartDisabledBySystem => false;

        public void Shutdown()
        {
            if (_shutDown) return;
            _shutDown = true;
            Controller.Dispose();
            if (_tray != null) _tray.IsVisible = false;
            _life.Shutdown();
        }
    }
}
