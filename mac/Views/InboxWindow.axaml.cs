using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.VisualTree;
using ClickUpWidget.Shared;

namespace ClickUpWidget.Mac
{
    /// <summary>Маленький тихий інбокс на робочому столі: коментарі й зміни в моїх задачах.</summary>
    public partial class InboxWindow : DesktopWindow
    {
        public InboxWindow() : this(MacShell.Current!) { }

        public InboxWindow(MacShell shell) : base(shell)
        {
            InitializeComponent();
            DataContext = shell.Controller.InboxView;
        }

        protected override (double? Left, double? Top) Saved => (Settings.InboxLeft, Settings.InboxTop);

        protected override void Save(double left, double top)
        {
            Settings.InboxLeft = left;
            Settings.InboxTop = top;
        }

        protected override PixelPoint DefaultPosition(Screen screen)
        {
            // За замовчуванням — ліворуч від панелі задач.
            PixelRect area = screen.WorkingArea;
            double panelLeft = Settings.PanelLeft ?? area.Right - (392 + 8) * Unit;
            double top = Settings.PanelTop ?? area.Y + 8 * Unit;
            return new PixelPoint((int)(panelLeft - (Width - 8) * Unit), (int)top);
        }

        private void Header_Pressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && (e.Source as Visual)?.FindAncestorOfType<Button>(true) == null)
                DragFrom(e);
        }

        private void Row_Tapped(object? sender, TappedEventArgs e)
        {
            // Кліки по кнопках і по полю коментаря задачу не відкривають.
            for (Visual? v = e.Source as Visual; v != null && !ReferenceEquals(v, sender); v = v.GetVisualParent())
                if (v is Button || v is CommentBox) return;
            if ((sender as Control)?.DataContext is InboxRow row && !row.IsComposing)
                Shell.Controller.InboxView.Open.Execute(row);
        }

        private void Reply_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.Tag is InboxRow row) Shell.Controller.ToggleComposer(row, replyToAuthor: true);
        }

        private void Comment_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.Tag is InboxRow row) Shell.Controller.ToggleComposer(row);
        }
    }

    /// <summary>true → видно (1), false → прозоро (0), місце лишається.</summary>
    public static class Opacity
    {
        public static readonly IValueConverter FromBool = new FuncValueConverter<bool, double>(b => b ? 1 : 0);
    }
}
