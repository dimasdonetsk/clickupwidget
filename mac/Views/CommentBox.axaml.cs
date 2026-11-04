using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClickUpWidget.Shared;

namespace ClickUpWidget.Mac
{
    /// <summary>
    /// Поле коментаря під рядком задачі чи події. «@» відкриває список людей:
    /// спершу учасники задачі, потім решта робочого простору. Enter надсилає.
    /// </summary>
    public partial class CommentBox : UserControl
    {
        private const string NoteText = "Enter — надіслати · Esc — скасувати";

        private ComposerModel? _model;
        private List<Person> _taskPeople = new();
        private List<Person> _allPeople = new();
        private long _myId;

        private List<(Person Person, bool InTask)> _candidates = new();
        private int _selected;

        public CommentBox()
        {
            InitializeComponent();
            // Tunnel — раніше, ніж TextBox сам обробить Enter/стрілки (як PreviewKeyDown у WPF).
            Box.AddHandler(KeyDownEvent, Box_KeyDown, RoutingStrategies.Tunnel);
            Box.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty) OnTextChanged();
                else if (e.Property == TextBox.CaretIndexProperty) UpdatePopup();
            };
            Box.LostFocus += Box_LostFocus;
            AttachedToVisualTree += (_, _) => OnLoaded();
            DetachedFromVisualTree += (_, _) => ClosePopup();
        }

        private async void OnLoaded()
        {
            _model = DataContext as ComposerModel;
            WidgetController? app = MacShell.Current?.Controller;
            if (_model == null || app == null) return;

            if (_model.ReplyTo is Person reply && _model.Text.Length == 0)
            {
                _model.Text = "@" + reply.Name + " ";
                _model.Mentions.Add(reply);
            }
            OnTextChanged();
            FocusBox();

            try
            {
                (_taskPeople, _allPeople, _myId) = await app.GetMentionPeopleAsync(_model.TaskId);
            }
            catch (Exception ex) { Log.Write("Mention people: " + ex.GetBaseException().Message); }
        }

        /// <summary>Вікна на столі лежать під іншими й не активні — робимо програму й вікно активними, щоб друкувати.</summary>
        private void FocusBox()
        {
            if (TopLevel.GetTopLevel(this) is Window window)
            {
                MacNative.ActivateApp();
                window.Activate();
            }
            Dispatcher.UIThread.Post(() =>
            {
                Box.Focus();
                Box.CaretIndex = Box.Text?.Length ?? 0;
            }, DispatcherPriority.Input);
        }

        // ───────────────────────────── введення ─────────────────────────────

        private void OnTextChanged()
        {
            Hint.IsVisible = string.IsNullOrEmpty(Box.Text);
            if (_model != null && _model.HasError) _model.Error = "";
            UpdatePopup();
        }

        private void Box_LostFocus(object? sender, RoutedEventArgs e)
        {
            // Клік по списку не повинен його закривати — перевіряємо трохи згодом.
            Dispatcher.UIThread.Post(() =>
            {
                if (!Box.IsFocused && MentionPopup.Child?.IsPointerOver != true) ClosePopup();
            }, DispatcherPriority.Background);
        }

        private void Box_KeyDown(object? sender, KeyEventArgs e)
        {
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

            if (MentionPopup.IsOpen && _candidates.Count > 0)
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
                case Key.Enter when !shift:
                    _ = SendAsync();
                    e.Handled = true;      // новий рядок — лише Shift+Enter (його TextBox вставить сам)
                    break;
                case Key.Escape:
                    MacShell.Current?.Controller.CloseComposer();
                    e.Handled = true;
                    break;
            }
        }

        private void Send_Click(object? sender, RoutedEventArgs e) => _ = SendAsync();

        private async System.Threading.Tasks.Task SendAsync()
        {
            WidgetController? app = MacShell.Current?.Controller;
            if (_model == null || app == null || _model.Busy) return;
            if ((Box.Text ?? "").Trim().Length == 0) return;

            ClosePopup();
            _model.Busy = true;
            SendBtn.IsEnabled = false;
            Note.Text = "Надсилаю…";
            string? error = await app.SendCommentAsync(_model);
            if (error != null)
            {
                _model.Busy = false;
                SendBtn.IsEnabled = true;
                Note.Text = NoteText;
                _model.Error = error;
                FocusBox();
            }
        }

        // ───────────────────────────── список @ ─────────────────────────────

        private void UpdatePopup()
        {
            string text = Box.Text ?? "";
            (int Start, string Query)? active = CommentText.ActiveMention(text, Math.Min(Box.CaretIndex, text.Length));
            if (active == null) { ClosePopup(); return; }

            _candidates = CommentText.Candidates(active.Value.Query, _taskPeople, _allPeople, _myId);
            MentionList.Children.Clear();
            if (_candidates.Count == 0)
            {
                var note = new TextBlock
                {
                    Text = _allPeople.Count == 0 ? "Завантажую людей…" : "Нікого не знайдено",
                    FontSize = 12, Margin = new Thickness(9, 7, 9, 7),
                };
                note.Bind(TextBlock.ForegroundProperty, note.GetResourceObservable("Fg2"));
                MentionList.Children.Add(note);
            }
            else
            {
                for (int i = 0; i < _candidates.Count; i++) MentionList.Children.Add(MakeRow(i));
                Select(Math.Min(_selected, _candidates.Count - 1));
            }
            MentionPopup.IsOpen = true;
        }

        private Border MakeRow(int index)
        {
            (Person p, bool inTask) = _candidates[index];
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

            var avatar = new Grid { Width = 22, Height = 22, Margin = new Thickness(0, 0, 9, 0) };
            avatar.Children.Add(new Ellipse { Fill = HexBrush.Get(Palette.IsValid(p.Color) ? p.Color : "#7AA7FF") });
            avatar.Children.Add(new TextBlock
            {
                Text = p.Initials.Length > 2 ? p.Initials.Substring(0, 2) : p.Initials,
                FontSize = 9.5, FontWeight = FontWeight.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            line.Children.Add(avatar);

            var name = new TextBlock { Text = p.Name, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("Fg"));
            Grid.SetColumn(name, 1);
            line.Children.Add(name);

            if (inTask)
            {
                var tag = new TextBlock { Text = "у задачі", FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 2, 0) };
                tag.Bind(TextBlock.ForegroundProperty, tag.GetResourceObservable("Fg3"));
                Grid.SetColumn(tag, 2);
                line.Children.Add(tag);
            }

            var row = new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 5, 7, 5), Child = line,
                Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent,
            };
            row.PointerEntered += (_, _) => Select(index);
            // PointerPressed — раніше, ніж поле втратить фокус.
            row.PointerPressed += (_, ev) => { Insert(p); ev.Handled = true; };
            return row;
        }

        private void Select(int index)
        {
            if (_candidates.Count == 0) return;
            _selected = (index % _candidates.Count + _candidates.Count) % _candidates.Count;
            IBrush hover = this.TryFindResource("Hover", out object? h) && h is IBrush b ? b : Brushes.Transparent;
            for (int i = 0; i < MentionList.Children.Count; i++)
                if (MentionList.Children[i] is Border row)
                    row.Background = i == _selected ? hover : Brushes.Transparent;
        }

        /// <summary>Замінити «@запит» на «@Ім'я » і запам'ятати, кого згадали.</summary>
        private void Insert(Person p)
        {
            string text = Box.Text ?? "";
            int caret = Math.Min(Box.CaretIndex, text.Length);
            (int Start, string Query)? active = CommentText.ActiveMention(text, caret);
            if (active == null) { ClosePopup(); return; }
            int start = active.Value.Start;
            string insert = "@" + p.Name + " ";
            Box.Text = text.Substring(0, start) + insert + text.Substring(caret);
            Box.CaretIndex = start + insert.Length;
            if (_model != null && !_model.Mentions.Contains(p)) _model.Mentions.Add(p);
            _selected = 0;
            ClosePopup();
            Box.Focus();
        }

        private void ClosePopup() => MentionPopup.IsOpen = false;
    }
}
