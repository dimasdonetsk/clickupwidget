using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace ClickUpWidget.Mac
{
    /// <summary>
    /// Коротка картка під рядком меню: таймер і задачі на сьогодні.
    /// Створюється при відкритті і закривається, щойно клікнеш деінде.
    /// </summary>
    public partial class FlyoutWindow : Window
    {
        // Прозорі відступи навколо картки (місце для тіні), див. Margin у XAML.
        private const double PadLeft = 16, PadTop = 12, PadRight = 16, PadBottom = 20;

        private Point? _cursor;
        private bool _closing;

        public FlyoutWindow() : this(MacShell.Current!) { }

        public FlyoutWindow(MacShell shell)
        {
            InitializeComponent();
            DataContext = shell.Controller.Dashboard;
            View.Compact = true;
            View.ListMaxHeight = 420;
            SizeChanged += (_, _) => Place();
            Deactivated += (_, _) => RequestClose();
            Opened += (_, _) =>
            {
                Place();
                MacNative.FloatOnAllSpaces(this);
                MacNative.ActivateApp();
                Activate();
            };
        }

        private double Unit => DesktopScaling > 0 ? DesktopScaling : 1;

        /// <summary>Показати під значком у рядку меню (біля курсора: його щойно клікнули).</summary>
        public void ShowNearMenuBar()
        {
            Screen? primary = Screens.Primary;
            _cursor = MacNative.CursorPoints(primary != null ? primary.Bounds.Height / Unit : 0);
            Show();
        }

        /// <summary>Закрити. Через диспетчер, бо часто викликається з обробника Deactivated.</summary>
        public void RequestClose()
        {
            if (_closing) return;
            _closing = true;
            Dispatcher.UIThread.Post(Close);
        }

        protected override void OnClosed(EventArgs e)
        {
            DataContext = null;
            base.OnClosed(e);
        }

        private void Place()
        {
            if (!IsVisible) return;
            double u = Unit;
            // Курсор у точках → в одиниці положення вікна.
            PixelPoint? anchor = _cursor is Point c ? new PixelPoint((int)(c.X * u), (int)(c.Y * u)) : null;
            Screen? screen = (anchor != null ? Screens.ScreenFromPoint(anchor.Value) : null) ?? Screens.Primary;
            if (screen == null) return;
            PixelRect area = screen.WorkingArea;

            double w = Bounds.Width * u, h = Bounds.Height * u;
            double padL = PadLeft * u, padT = PadTop * u, padR = PadRight * u, padB = PadBottom * u, gap = 6 * u;
            PixelPoint a = anchor ?? new PixelPoint(area.Right - (int)(w / 2), area.Y);

            double left = a.X - w / 2;
            // Рядок меню (і значок) — угорі: картка під ним. Якщо курсор унизу (панель завдань Windows) — над ним.
            double top = a.Y < area.Y + area.Height / 2 ? area.Y + gap - padT : area.Bottom - gap - h + padB;

            left = Math.Max(area.X - padL + gap, Math.Min(left, area.Right - w + padR - gap));
            top = Math.Max(area.Y - padT + gap, Math.Min(top, area.Bottom - h + padB - gap));
            Position = new PixelPoint((int)Math.Round(left), (int)Math.Round(top));
        }
    }
}
