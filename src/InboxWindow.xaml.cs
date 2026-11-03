using System;
using System.Windows;
using System.Windows.Input;

namespace ClickUpWidget
{
    /// <summary>Маленький тихий інбокс на робочому столі: лише коментарі й зміни статусів.</summary>
    public partial class InboxWindow : Window
    {
        private readonly AppController _app;
        private readonly InboxViewModel _vm;
        private IntPtr _hwnd;

        public InboxWindow(AppController app, InboxViewModel vm)
        {
            InitializeComponent();
            _app = app;
            _vm = vm;
            DataContext = vm;
            Loaded += (_, _) => PlaceInitial();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = DesktopPin.Attach(this);
        }

        private void PlaceInitial()
        {
            Settings s = _app.Settings;
            Rect area = SystemParameters.WorkArea;
            // За замовчуванням — ліворуч від панелі задач.
            double panelLeft = s.PanelLeft ?? area.Right - 392 - 8;
            double left = s.InboxLeft ?? panelLeft - Width + 8;
            double top = s.InboxTop ?? (s.PanelTop ?? area.Top + 8);
            if (!DesktopPin.OnScreen(left, top, Width))
            {
                left = area.Right - 392 - Width;
                top = area.Top + 8;
            }
            Left = left;
            Top = top;
        }

        private void Header_Down(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || _app.Settings.LockPanel) return;
            try { DragMove(); } catch { return; }
            _app.Settings.InboxLeft = Math.Round(Left);
            _app.Settings.InboxTop = Math.Round(Top);
            _app.Settings.Save();
            DesktopPin.SendToBottom(_hwnd);
        }

        private void Row_Click(object sender, MouseButtonEventArgs e)
        {
            // Кліки по кнопках і по полю коментаря задачу не відкривають.
            for (DependencyObject? d = e.OriginalSource as DependencyObject; d != null && d != sender;
                 d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is System.Windows.Controls.Primitives.ButtonBase || d is CommentBox) return;
            if ((sender as FrameworkElement)?.DataContext is InboxRow row)
                _vm.Open.Execute(row);
        }

        private void Reply_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is InboxRow row) _app.ToggleComposer(row, replyToAuthor: true);
        }

        private void Comment_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is InboxRow row) _app.ToggleComposer(row);
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            _app.ShowMenuAtCursor();
            e.Handled = true;
        }
    }
}
