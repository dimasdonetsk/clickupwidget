using System;
using System.Windows;
using System.Windows.Input;

namespace ClickUpWidget
{
    /// <summary>Панель задач на робочому столі (під усіма вікнами, див. DesktopPin).</summary>
    public partial class PanelWindow : Window
    {
        private readonly AppController _app;
        private IntPtr _hwnd;

        public PanelWindow(AppController app, DashboardViewModel vm)
        {
            InitializeComponent();
            _app = app;
            DataContext = vm;
            View.HeaderPressed += OnHeaderPressed;
            Loaded += (_, _) => { PlaceInitial(); UpdateMaxHeight(); };
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
            double left = s.PanelLeft ?? area.Right - Width - 8;
            double top = s.PanelTop ?? area.Top + 8;
            if (!DesktopPin.OnScreen(left, top, Width))
            {
                left = area.Right - Width - 8;
                top = area.Top + 8;
            }
            Left = left;
            Top = top;
        }

        public void UpdateMaxHeight()
        {
            // Список прокручується, щоб панель не вилазила за низ екрана.
            System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromHandle(_hwnd);
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            double workBottom = screen.WorkingArea.Bottom / dpi.DpiScaleY;
            double room = workBottom - Top - 210;
            View.ListMaxHeight = Math.Max(160, room);
        }

        private void OnHeaderPressed()
        {
            if (_app.Settings.LockPanel) return;
            try { DragMove(); } catch { return; }
            _app.Settings.PanelLeft = Math.Round(Left);
            _app.Settings.PanelTop = Math.Round(Top);
            _app.Settings.Save();
            UpdateMaxHeight();
            DesktopPin.SendToBottom(_hwnd);
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            _app.ShowMenuAtCursor();
            e.Handled = true;
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            Dispatcher.BeginInvoke(new Action(UpdateMaxHeight));
        }
    }
}
