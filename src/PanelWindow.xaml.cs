using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ClickUpWidget
{
    /// <summary>
    /// Панель на робочому столі: завжди під іншими вікнами, не з'являється на панелі завдань і в Alt+Tab.
    /// Власник — вікно робочого столу (Progman), тож Win+D її не ховає.
    /// </summary>
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
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeToolWindow(_hwnd);

            IntPtr progman = Native.FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
                Native.SetWindowLongPtr(_hwnd, Native.GWLP_HWNDPARENT, progman);

            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
            SendToBottom();
        }

        private IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Хоч би що відбувалося (клік, активація) — лишаємося під усіма вікнами.
            if (msg == Native.WM_WINDOWPOSCHANGING)
            {
                var wp = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
                if ((wp.flags & Native.SWP_NOZORDER) == 0)
                {
                    wp.hwndInsertAfter = Native.HWND_BOTTOM;
                    Marshal.StructureToPtr(wp, lParam, false);
                }
            }
            return IntPtr.Zero;
        }

        private void SendToBottom()
        {
            if (_hwnd == IntPtr.Zero) return;
            Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        private void PlaceInitial()
        {
            Settings s = _app.Settings;
            Rect area = SystemParameters.WorkArea;
            double left = s.PanelLeft ?? area.Right - Width - 8;
            double top = s.PanelTop ?? area.Top + 8;

            // Якщо монітор від'єднали і збережене місце поза екранами — повертаємо на основний.
            if (left > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80
                || left + Width < SystemParameters.VirtualScreenLeft + 80
                || top > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80
                || top < SystemParameters.VirtualScreenTop - 20)
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
            SendToBottom();
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
