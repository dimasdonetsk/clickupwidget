using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace ClickUpWidget
{
    /// <summary>Список задач. Повна версія — для панелі на столі, коротка (Compact) — для флайаута з трею.</summary>
    public partial class TasksView : UserControl
    {
        private DashboardViewModel? _vm;

        /// <summary>Натиснули на шапку (панель на столі перетягується за неї).</summary>
        public event Action? HeaderPressed;

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
            Loaded += (_, _) => Attach(DataContext as DashboardViewModel);
            Unloaded += (_, _) => Attach(null);
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
            bool loaded = vm?.Loaded == true && vm.NeedsToken == false;
            AllSection.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible;
            EmptyText.Visibility = !_compact && loaded && vm!.HasTasks == false ? Visibility.Visible : Visibility.Collapsed;
            CompactEmpty.Visibility = _compact && loaded && vm!.HasDue == false ? Visibility.Visible : Visibility.Collapsed;
            OpenLink.Visibility = vm?.HasUpdate == true ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Header_Down(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) HeaderPressed?.Invoke();
        }

        /// <summary>Клік по рядку відкриває задачу в ClickUp (якщо клікнули не по кнопці).</summary>
        private void Row_Click(object sender, MouseButtonEventArgs e)
        {
            for (DependencyObject? d = e.OriginalSource as DependencyObject; d != null && d != sender; d = ParentOf(d))
                if (d is ButtonBase) return;
            if ((sender as FrameworkElement)?.DataContext is TaskRow row)
                _vm?.OpenTask.Execute(row);
        }

        private static DependencyObject? ParentOf(DependencyObject d) =>
            d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

        // ───────────────────────────── меню статусів ─────────────────────────────

        private async void StatusBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not TaskRow row) return;
            AppController? app = AppController.Current;
            if (app == null) return;

            var panel = new StackPanel();
            var card = new Border
            {
                Background = Theme.Get("Menu"),
                BorderBrush = Theme.Get("Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(6),
                MinWidth = 190,
                Child = panel,
            };
            var shadow = new Border
            {
                Background = Theme.Get("Menu"),
                CornerRadius = new CornerRadius(12),
                Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = 0.35 },
            };
            var root = new Grid { Margin = new Thickness(10, 4, 10, 16) };
            root.Children.Add(shadow);
            root.Children.Add(card);
            TextElement.SetFontFamily(root, (FontFamily)FindResource("Ui"));

            var popup = new Popup
            {
                PlacementTarget = btn,
                Placement = PlacementMode.Bottom,
                HorizontalOffset = -150,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                Child = root,
            };

            panel.Children.Add(Note("Завантажую статуси…"));
            popup.IsOpen = true;

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
                Brush brush = Theme.StatusBrush(s.Color);
                bool hollow = string.Equals(s.Type, "open", StringComparison.OrdinalIgnoreCase);

                var line = new Grid();
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var dot = new Ellipse
                {
                    Width = 9, Height = 9, StrokeThickness = 2, Stroke = brush, Fill = hollow ? Brushes.Transparent : brush,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0),
                };
                var label = new TextBlock { Text = TaskText.StatusLabel(s.Name), VerticalAlignment = VerticalAlignment.Center };
                line.Children.Add(dot);
                Grid.SetColumn(label, 1);
                line.Children.Add(label);
                if (current)
                {
                    var check = new TextBlock
                    {
                        Text = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
                        Foreground = Theme.Get("Fg2"),
                    };
                    Grid.SetColumn(check, 2);
                    line.Children.Add(check);
                }

                var item = new Button { Style = (Style)FindResource("MenuRow"), Content = line };
                item.Click += async (_, _) =>
                {
                    popup.IsOpen = false;
                    if (!current) await app.SetStatusAsync(row.Task, option.Name);
                };
                panel.Children.Add(item);
            }
        }

        private TextBlock Note(string text) => new TextBlock
        {
            Text = text, FontSize = 12, Margin = new Thickness(9, 7, 9, 7), Foreground = Theme.Get("Fg2"),
        };
    }
}
