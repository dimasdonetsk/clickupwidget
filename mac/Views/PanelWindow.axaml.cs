using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace ClickUpWidget.Mac
{
    /// <summary>Панель задач на робочому столі (під усіма вікнами, див. MacNative.PinToDesktop).</summary>
    public partial class PanelWindow : DesktopWindow
    {
        public PanelWindow() : this(MacShell.Current!) { }

        public PanelWindow(MacShell shell) : base(shell)
        {
            InitializeComponent();
            DataContext = shell.Controller.Dashboard;
            View.HeaderPressed += DragFrom;
        }

        protected override (double? Left, double? Top) Saved => (Settings.PanelLeft, Settings.PanelTop);

        protected override void Save(double left, double top)
        {
            Settings.PanelLeft = left;
            Settings.PanelTop = top;
        }

        protected override PixelPoint DefaultPosition(Screen screen)
        {
            // Правий верхній кут робочої області (під рядком меню).
            PixelRect area = screen.WorkingArea;
            return new PixelPoint((int)(area.Right - (Width + 8) * Unit), (int)(area.Y + 8 * Unit));
        }

        protected override void OnPlaced() => UpdateMaxHeight();

        protected override void OnMoved()
        {
            base.OnMoved();
            UpdateMaxHeight();
        }

        /// <summary>Список прокручується, щоб панель не вилазила за низ екрана.</summary>
        public void UpdateMaxHeight()
        {
            Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen == null) return;
            double room = (screen.WorkingArea.Bottom - Position.Y) / Unit - 210;
            View.ListMaxHeight = Math.Max(160, room);
        }
    }
}
