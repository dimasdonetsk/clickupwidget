using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ClickUpWidget
{
    /// <summary>
    /// Поле коментаря під рядком задачі чи події. «@» відкриває список людей:
    /// спершу учасники задачі, потім решта робочого простору. Enter надсилає.
    /// </summary>
    public partial class CommentBox : UserControl
    {
        private ComposerModel? _model;
        private List<Person> _taskPeople = new();
        private List<Person> _allPeople = new();
        private long _myId;

        private Popup? _popup;
        private StackPanel? _list;
        private List<(Person Person, bool InTask)> _candidates = new();
        private int _selected;

        public CommentBox()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += (_, _) => ClosePopup();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _model = DataContext as ComposerModel;
            AppController? app = AppController.Current;
            if (_model == null || app == null) return;

            if (_model.ReplyTo is Person reply && _model.Text.Length == 0)
            {
                _model.Text = "@" + reply.Name + " ";
                _model.Mentions.Add(reply);
            }
            FocusBox();

            try
            {
                (_taskPeople, _allPeople, _myId) = await app.GetMentionPeopleAsync(_model.TaskId);
            }
            catch (Exception ex) { Log.Write("Mention people: " + ex.GetBaseException().Message); }
        }

        /// <summary>Вікна на столі лежать під іншими й не активні — робимо вікно активним, щоб друкувати.</summary>
        private void FocusBox()
        {
            Window? window = Window.GetWindow(this);
            if (window != null)
            {
                Native.ForceForeground(new WindowInteropHelper(window).Handle);
                window.Activate();
            }
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                Box.Focus();
                Keyboard.Focus(Box);
                Box.CaretIndex = Box.Text.Length;
            }));
        }

        // ───────────────────────────── введення ─────────────────────────────

        private void Box_TextChanged(object sender, TextChangedEventArgs e)
        {
            Hint.Visibility = Box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_model != null && _model.HasError) _model.Error = "";
            UpdatePopup();
        }

        private void Box_SelectionChanged(object sender, RoutedEventArgs e) => UpdatePopup();

        private void Box_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // Клік по списку не повинен його закривати — перевіряємо трохи згодом.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!Box.IsKeyboardFocused && _popup?.IsMouseOver != true) ClosePopup();
            }));
        }

        private void Box_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            if (_popup?.IsOpen == true && _candidates.Count > 0)
            {
                switch (e.Key)
                {
                    case Key.Down: Select(_selected + 1); e.Handled = true; return;
                    case Key.Up: Select(_selected - 1); e.Handled = true; return;
                    case Key.Enter:
                    case Key.Tab: Insert(_candidates[_selected].Person); e.Handled = true; return;
                    case Key.Escape: ClosePopup(); e.Handled = true; return;
                }
            }

            switch (e.Key)
            {
                case Key.Enter when shift:
                    int caret = Box.CaretIndex;
                    Box.Text = Box.Text.Insert(caret, Environment.NewLine);
                    Box.CaretIndex = caret + Environment.NewLine.Length;
                    e.Handled = true;
                    break;
                case Key.Enter:
                    _ = SendAsync();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    AppController.Current?.CloseComposer();
                    e.Handled = true;
                    break;
            }
        }

        private void Send_Click(object sender, RoutedEventArgs e) => _ = SendAsync();

        private async System.Threading.Tasks.Task SendAsync()
        {
            AppController? app = AppController.Current;
            if (_model == null || app == null || _model.Busy) return;
            if (Box.Text.Trim().Length == 0) return;

            ClosePopup();
            _model.Busy = true;
            SendBtn.IsEnabled = false;
            Note.Text = "Надсилаю…";
            string? error = await app.SendCommentAsync(_model);
            if (error != null)
            {
                _model.Busy = false;
                SendBtn.IsEnabled = true;
                Note.Text = "Enter — надіслати · Esc — скасувати";
                _model.Error = error;
                FocusBox();
            }
        }

        // ───────────────────────────── список @ ─────────────────────────────

        private void UpdatePopup()
        {
            (int Start, string Query)? active = CommentText.ActiveMention(Box.Text, Box.CaretIndex);
            if (active == null) { ClosePopup(); return; }

            _candidates = CommentText.Candidates(active.Value.Query, _taskPeople, _allPeople, _myId);
            EnsurePopup();
            _list!.Children.Clear();
            if (_candidates.Count == 0)
            {
                _list.Children.Add(new TextBlock
                {
                    Text = _allPeople.Count == 0 ? "Завантажую людей…" : "Нікого не знайдено",
                    FontSize = 12, Margin = new Thickness(9, 7, 9, 7), Foreground = Theme.Get("Fg2"),
                });
            }
            else
            {
                for (int i = 0; i < _candidates.Count; i++) _list.Children.Add(MakeRow(i));
                Select(Math.Min(_selected, _candidates.Count - 1));
            }
            _popup!.IsOpen = true;
        }

        private void EnsurePopup()
        {
            if (_popup != null) return;
            _list = new StackPanel();
            var card = new Border
            {
                Background = Theme.Get("Menu"),
                BorderBrush = Theme.Get("Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(5),
                MinWidth = 230,
                Child = _list,
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
            TextElement.SetForeground(root, Theme.Get("Fg"));

            _popup = new Popup
            {
                PlacementTarget = Box,
                Placement = PlacementMode.Bottom,
                HorizontalOffset = -14,
                StaysOpen = true,
                AllowsTransparency = true,
                Focusable = false,
                Child = root,
            };
        }

        private Border MakeRow(int index)
        {
            (Person p, bool inTask) = _candidates[index];
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Brush color = Theme.StatusBrush(p.Color ?? "#7AA7FF");
            var avatar = new Grid { Width = 22, Height = 22, Margin = new Thickness(0, 0, 9, 0) };
            avatar.Children.Add(new Ellipse { Fill = color });
            avatar.Children.Add(new TextBlock
            {
                Text = p.Initials.Length > 2 ? p.Initials.Substring(0, 2) : p.Initials,
                FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            line.Children.Add(avatar);

            var name = new TextBlock { Text = p.Name, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(name, 1);
            line.Children.Add(name);

            if (inTask)
            {
                var tag = new TextBlock { Text = "у задачі", FontSize = 10.5, Foreground = Theme.Get("Fg3"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 2, 0) };
                Grid.SetColumn(tag, 2);
                line.Children.Add(tag);
            }

            var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 5, 7, 5), Child = line, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            row.MouseEnter += (_, _) => Select(index);
            // PreviewMouseDown — раніше, ніж поле втратить фокус.
            row.PreviewMouseLeftButtonDown += (_, ev) => { Insert(p); ev.Handled = true; };
            return row;
        }

        private void Select(int index)
        {
            if (_list == null || _candidates.Count == 0) return;
            _selected = (index % _candidates.Count + _candidates.Count) % _candidates.Count;
            for (int i = 0; i < _list.Children.Count; i++)
                if (_list.Children[i] is Border b)
                    b.Background = i == _selected ? Theme.Get("Hover") : Brushes.Transparent;
        }

        /// <summary>Замінити «@запит» на «@Ім'я » і запам'ятати, кого згадали.</summary>
        private void Insert(Person p)
        {
            (int Start, string Query)? active = CommentText.ActiveMention(Box.Text, Box.CaretIndex);
            if (active == null) { ClosePopup(); return; }
            int start = active.Value.Start, caret = Box.CaretIndex;
            string insert = "@" + p.Name + " ";
            Box.Text = Box.Text.Substring(0, start) + insert + Box.Text.Substring(caret);
            Box.CaretIndex = start + insert.Length;
            if (_model != null && !_model.Mentions.Contains(p)) _model.Mentions.Add(p);
            _selected = 0;
            ClosePopup();
            Box.Focus();
        }

        private void ClosePopup()
        {
            if (_popup != null) _popup.IsOpen = false;
        }
    }
}
