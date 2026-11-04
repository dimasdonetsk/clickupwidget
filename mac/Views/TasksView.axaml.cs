using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClickUpWidget.Shared;

namespace ClickUpWidget.Mac
{
    /// <summary>Список задач. Повна версія — для панелі на столі, коротка (Compact) — для картки з рядка меню.</summary>
    public partial class TasksView : UserControl
    {
        private DashboardViewModel? _vm;

        /// <summary>Натиснули на шапку (панель на столі перетягується за неї).</summary>
        public event Action<PointerPressedEventArgs>? HeaderPressed;

        private bool _compact;
        public bool Compact
        {
            get => _compact;
            set { _compact = value; HeaderTitle.Text = value ? "Зараз" : "Мої задачі"; ApplyVisibility(); }
        }

        public double ListMaxHeight
        {
            get => Scroll.MaxHeight;
            set => Scroll.MaxHeight = value;
        }

        public TasksView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => Attach(DataContext as DashboardViewModel);
            AttachedToVisualTree += (_, _) => Attach(DataContext as DashboardViewModel);
            DetachedFromVisualTree += (_, _) => Attach(null);
        }

        private void Attach(DashboardViewModel? vm)
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = vm;
            if (_vm != null) _vm.PropertyChanged += OnVmChanged;
            ApplyVisibility();
        }

        private void OnVmChanged(object? sender, PropertyChangedEventArgs e) => ApplyVisibility();

        private void ApplyVisibility()
        {
            DashboardViewModel? vm = _vm;
            Settings s = MacShell.Current?.Controller.Settings ?? new Settings();
            bool loaded = vm?.Loaded == true && vm.NeedsToken == false;

            // Що показувати — налаштовується в меню («Що показувати»), окремо для панелі й картки.
            bool showTime = _compact || s.ShowTodayTime;
            bool showTimer = _compact ? s.FlyoutShowTimer : s.ShowTimer;
            bool showDue = _compact ? s.FlyoutShowDue : s.ShowDueSection;
            bool showAll = !_compact && s.ShowAllSection;

            TodayLine.IsVisible = showTime;
            TimerCard.IsVisible = showTimer && vm?.HasTimer == true;
            // Задачі з таймером у списках немає — якщо картку сховано, показуємо її першим рядком.
            RunningHost.IsVisible = !showTimer && vm?.RunningRow != null;
            DueSection.IsVisible = showDue && vm?.HasDue == true;
            AllSection.IsVisible = showAll;
            EmptyText.IsVisible = showAll && loaded && vm!.HasTasks == false && vm.HasTimer == false;
            CompactEmpty.IsVisible = _compact && showDue && loaded && vm!.HasDue == false;
            OpenLink.IsVisible = vm?.HasUpdate != true;
        }

        private void Header_Pressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && (e.Source as Visual)?.FindAncestorOfType<Button>(true) == null)
                HeaderPressed?.Invoke(e);
        }

        /// <summary>Клік по кнопці чи по полю коментаря не відкриває задачу.</summary>
        private static bool FromControl(object? source, object? stopAt)
        {
            for (Visual? v = source as Visual; v != null && !ReferenceEquals(v, stopAt); v = v.GetVisualParent())
                if (v is Button || v is CommentBox) return true;
            return false;
        }

        /// <summary>Клік по рядку відкриває задачу в ClickUp (якщо клікнули не по кнопці).</summary>
        private void Row_Tapped(object? sender, TappedEventArgs e)
        {
            if (FromControl(e.Source, sender)) return;
            if ((sender as Control)?.DataContext is TaskRow row && !row.IsComposing)
                _vm?.OpenTask.Execute(row);
        }

        /// <summary>Клік по картці таймера відкриває задачу в ClickUp (якщо не по кнопці й не по полю коментаря).</summary>
        private void TimerCard_Tapped(object? sender, TappedEventArgs e)
        {
            if (FromControl(e.Source, sender)) return;
            if (_vm?.RunningRow is TaskRow row && row.Task.Url.Length > 0 && !row.IsComposing)
                _vm.OpenTask.Execute(row);
        }

        private void TimerBtn_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is TaskRow row) _vm?.ToggleTimer.Execute(row);
        }

        private void CommentBtn_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is IComposerHost host) MacShell.Current?.Controller.ToggleComposer(host);
        }

        // ───────────────────────────── меню статусів ─────────────────────────────

        private async void StatusBtn_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not TaskRow row) return;
            WidgetController? app = MacShell.Current?.Controller;
            if (app == null) return;

            var panel = new StackPanel { MinWidth = 190 };
            var flyout = new Flyout { Content = panel, Placement = PlacementMode.BottomEdgeAlignedRight };
            flyout.FlyoutPresenterClasses.Add("card");
            panel.Children.Add(Note("Завантажую статуси…"));
            flyout.ShowAt(btn);

            List<StatusOption> statuses;
            try { statuses = await app.GetStatusesAsync(row.Task.ListId); }
            catch (Exception ex)
            {
                Log.Write("Statuses: " + ex.Message);
                panel.Children.Clear();
                panel.Children.Add(Note("Не вдалося завантажити статуси"));
                return;
            }

            panel.Children.Clear();
            foreach (StatusOption s in statuses)
            {
                StatusOption option = s;
                bool current = string.Equals(s.Name, row.Task.Status, StringComparison.OrdinalIgnoreCase);
                IBrush brush = HexBrush.Get(Palette.Status(s.Color));
                bool hollow = string.Equals(s.Type, "open", StringComparison.OrdinalIgnoreCase);

                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
                line.Children.Add(new Ellipse
                {
                    Width = 9, Height = 9, StrokeThickness = 2, Stroke = brush, Fill = hollow ? Brushes.Transparent : brush,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0),
                });
                var label = new TextBlock { Text = TaskText.StatusLabel(s.Name), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(label, 1);
                line.Children.Add(label);
                if (current)
                {
                    var check = new Glyph
                    {
                        Data = (Geometry)Application.Current!.FindResource("IconCheck")!,
                        Width = 11, Height = 11, Margin = new Thickness(10, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    check.Bind(Glyph.ForegroundProperty, check.GetResourceObservable("Fg2"));
                    Grid.SetColumn(check, 2);
                    line.Children.Add(check);
                }

                var item = new Button { Classes = { "menurow" }, Content = line };
                item.Click += async (_, _) =>
                {
                    flyout.Hide();
                    if (!current) await app.SetStatusAsync(row.Task, option.Name);
                };
                panel.Children.Add(item);
            }
        }

        private TextBlock Note(string text)
        {
            var note = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(9, 7, 9, 7) };
            note.Bind(TextBlock.ForegroundProperty, note.GetResourceObservable("Fg2"));
            return note;
        }
    }
}
