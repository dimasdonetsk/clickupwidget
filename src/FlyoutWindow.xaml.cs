using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClickUpWidget
{
    /// <summary>
    /// Коротка картка над значком у треї: таймер і задачі на сьогодні.
    /// Створюється при відкритті і закривається, щойно клікнеш деінде.
    /// </summary>
    public partial class FlyoutWindow : Window
    {
        // Прозорі відступи навколо картки (місце для тіні), див. Grid Margin у XAML.
        private const double PadLeft = 16, PadTop = 12, PadRight = 16, PadBottom = 20;

        private System.Drawing.Point _anchorPx;
        private bool _closing;

        public FlyoutWindow(DashboardViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            View.Compact = true;
            View.ListMaxHeight = 420;
            SizeChanged += (_, _) => Place();
            Deactivated += (_, _) => RequestClose();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Native.MakeToolWindow(new WindowInteropHelper(this).Handle);
        }

        /// <summary>Показати над точкою на екрані (у фізичних пікселях), зазвичай над значком у треї.</summary>
        public void ShowAt(System.Drawing.Point anchorPx)
        {
            _anchorPx = anchorPx;
            if (!IsVisible) Show();
            Place();
            Activate();
            Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        }

        /// <summary>Закрити. Через диспетчер, бо часто викликається з обробника Deactivated.</summary>
        public void RequestClose()
        {
            if (_closing) return;
            _closing = true;
            Dispatcher.BeginInvoke(new Action(Close));
        }

        protected override void OnClosed(EventArgs e)
        {
            DataContext = null;
            base.OnClosed(e);
        }

        private void Place()
        {
            if (!IsVisible) return;

            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
            System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromPoint(_anchorPx);
            System.Drawing.Rectangle area = screen.WorkingArea;
            System.Drawing.Rectangle bounds = screen.Bounds;

            double w = ActualWidth * sx, h = ActualHeight * sy;
            double padL = PadLeft * sx, padT = PadTop * sy, padR = PadRight * sx, padB = PadBottom * sy;
            double gap = 8 * sx;

            double left, top;
            if (area.Top > bounds.Top)
            {
                top = area.Top + gap - padT;            // панель завдань угорі
                left = _anchorPx.X - w / 2;
            }
            else if (area.Left > bounds.Left)
            {
                left = area.Left + gap - padL;          // панель зліва
                top = _anchorPx.Y - h / 2;
            }
            else if (area.Right < bounds.Right)
            {
                left = area.Right - gap - w + padR;     // панель справа
                top = _anchorPx.Y - h / 2;
            }
            else
            {
                top = area.Bottom - gap - h + padB;     // звичайно: панель знизу
                left = _anchorPx.X - w / 2;
            }

            left = Math.Max(area.Left - padL + gap, Math.Min(left, area.Right - w + padR - gap));
            top = Math.Max(area.Top - padT + gap, Math.Min(top, area.Bottom - h + padB - gap));

            Left = Math.Round(left) / sx;
            Top = Math.Round(top) / sy;
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            Dispatcher.BeginInvoke(new Action(Place));
        }
    }
}
