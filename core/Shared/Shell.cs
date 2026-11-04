using System;
using System.Collections.Generic;
using System.Threading;

namespace ClickUpWidget.Shared
{
    /// <summary>
    /// Що «мозку» (WidgetController) потрібно від конкретної платформи: вікна, значок,
    /// меню, автозапуск. Реалізація для Mac — mac/MacShell.cs.
    /// </summary>
    public interface IWidgetShell
    {
        /// <summary>Виконати дію в потоці інтерфейсу.</summary>
        void Post(Action action);

        void ApplyTheme(bool light);

        void ShowPanel();
        void HidePanel();
        void ShowInbox();
        void HideInbox();
        void ShowTokenWindow();

        /// <summary>Відкрита картка задач чи вікно токена — тихе оновлення почекає.</summary>
        bool IsBusy { get; }

        /// <summary>Значок (трей / рядок меню): чи йде таймер і підказка.</summary>
        void UpdateIcon(bool timerRunning, string tooltip);

        /// <summary>Показати меню біля курсора.</summary>
        void ShowMenu();

        void OpenUrl(string url);
        void OpenFolder(string path);

        void SetAutostart(bool enabled, bool userAction);
        /// <summary>Автозапуск вимкнули в системі (а не в нашому меню).</summary>
        bool AutostartDisabledBySystem { get; }

        /// <summary>Закрити програму (після збереження стану).</summary>
        void Shutdown();
    }

    /// <summary>Таймер, що «тікає» в потоці інтерфейсу (як DispatcherTimer, але без WPF/Avalonia).</summary>
    public sealed class UiTimer : IDisposable
    {
        private readonly Action<Action> _post;
        private readonly Action _tick;
        private Timer? _timer;
        private TimeSpan _interval;
        private int _generation;

        public UiTimer(Action<Action> post, TimeSpan interval, Action tick)
        {
            _post = post;
            _interval = interval;
            _tick = tick;
        }

        public bool IsEnabled { get; private set; }

        public TimeSpan Interval
        {
            get => _interval;
            set { _interval = value; if (IsEnabled) Start(); }
        }

        /// <summary>Запустити (або перезапустити — відлік інтервалу заново).</summary>
        public void Start()
        {
            IsEnabled = true;
            int gen = ++_generation;
            _timer?.Dispose();
            _timer = new Timer(_ => _post(() => { if (IsEnabled && gen == _generation) _tick(); }), null, _interval, _interval);
        }

        public void Stop()
        {
            IsEnabled = false;
            _generation++;
            _timer?.Dispose();
            _timer = null;
        }

        public void Dispose() => Stop();
    }

    public enum MenuKind { Action, Toggle, Submenu, Info, Separator }

    /// <summary>
    /// Пункт меню без прив'язки до платформи. Mac показує його і як системне меню значка в рядку меню,
    /// і як меню біля курсора у вікнах.
    /// </summary>
    public sealed class MenuNode
    {
        public MenuKind Kind { get; init; }
        public string Text { get; init; } = "";
        /// <summary>Галочка (для Toggle) або позначка вибраного (для Action з радіо-вибором).</summary>
        public bool Checked { get; set; }
        /// <summary>Галочки «Що показувати»: меню не закривається, щоб поставити кілька підряд.</summary>
        public bool KeepOpen { get; init; }
        public Action? Clicked { get; init; }
        public Action<bool>? Toggled { get; init; }
        public List<MenuNode> Items { get; } = new();

        public static MenuNode Item(string text, Action onClick) => new() { Kind = MenuKind.Action, Text = text, Clicked = onClick };
        public static MenuNode Radio(string text, bool selected, Action onClick) =>
            new() { Kind = MenuKind.Action, Text = text, Checked = selected, Clicked = onClick };
        public static MenuNode Toggle(string text, bool on, Action<bool> onChange, bool keepOpen = false) =>
            new() { Kind = MenuKind.Toggle, Text = text, Checked = on, Toggled = onChange, KeepOpen = keepOpen };
        public static MenuNode Keep(string text, bool on, Action<bool> onChange) => Toggle(text, on, onChange, keepOpen: true);
        public static MenuNode Sub(string text) => new() { Kind = MenuKind.Submenu, Text = text };
        public static MenuNode Info(string text) => new() { Kind = MenuKind.Info, Text = text };
        public static MenuNode Separator() => new() { Kind = MenuKind.Separator };

        /// <summary>Клік по пункту: галочка перемикається і повідомляє нове значення.</summary>
        public void Invoke()
        {
            if (Kind == MenuKind.Toggle)
            {
                Checked = !Checked;
                Toggled?.Invoke(Checked);
            }
            else Clicked?.Invoke();
        }
    }
}
