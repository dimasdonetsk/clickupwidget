using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;

namespace ClickUpWidget.Mac
{
    /// <summary>
    /// Спільне для панелі й інбоксу: вікно лежить на робочому столі (під усіма вікнами, MacNative.PinToDesktop),
    /// перетягується за шапку, положення запам'ятовується, правий клік (або Ctrl+клік) — меню.
    /// </summary>
    public abstract class DesktopWindow : Window
    {
        private DispatcherTimer? _saveTimer;
        private bool _placed;

        protected MacShell Shell { get; }
        protected Settings Settings => Shell.Controller.Settings;

        protected DesktopWindow(MacShell shell)
        {
            Shell = shell;
            Opened += (_, _) =>
            {
                PlaceInitial();
                _placed = true;
                MacNative.PinToDesktop(this);
                OnPlaced();
            };
            // Avalonia може повернути звичайний рівень вікна після активації — ставимо знову.
            Activated += (_, _) => MacNative.PinToDesktop(this);
            Deactivated += (_, _) => MacNative.PinToDesktop(this);
            PositionChanged += (_, _) => { if (_placed) ScheduleSave(); };
        }

        /// <summary>Початкове місце (у пікселях екрана), якщо збереженого немає.</summary>
        protected abstract PixelPoint DefaultPosition(Screen screen);

        protected abstract (double? Left, double? Top) Saved { get; }
        protected abstract void Save(double left, double top);

        protected virtual void OnPlaced() { }

        /// <summary>Одиниці положення вікна на одну одиницю розмітки (Windows — пікселі, Mac — точки).</summary>
        protected double Unit => DesktopScaling > 0 ? DesktopScaling : 1;

        private void PlaceInitial()
        {
            Screen? primary = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
            if (primary == null) return;
            (double? left, double? top) = Saved;
            var saved = left != null && top != null ? new PixelPoint((int)left.Value, (int)top.Value) : (PixelPoint?)null;
            Position = saved != null && OnScreen(saved.Value) ? saved.Value : DefaultPosition(primary);
        }

        /// <summary>Чи збережене місце досі на якомусь екрані (монітор могли від'єднати).</summary>
        private bool OnScreen(PixelPoint p)
        {
            double w = Width * Unit;
            foreach (Screen s in Screens.All)
            {
                PixelRect b = s.Bounds;
                if (p.X <= b.Right - 80 && p.X + w >= b.X + 80 && p.Y <= b.Bottom - 80 && p.Y >= b.Y - 20) return true;
            }
            return false;
        }

        /// <summary>Перетягування за шапку (якщо положення не закріплене в меню).</summary>
        protected void DragFrom(PointerPressedEventArgs e)
        {
            if (Settings.LockPanel) return;
            BeginMoveDrag(e);
        }

        private void ScheduleSave()
        {
            // Положення зберігаємо, коли перетягування закінчилось (вікно пів секунди не рухається).
            _saveTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) =>
            {
                _saveTimer!.Stop();
                Save(Position.X, Position.Y);
                Settings.Save();
                OnMoved();
            });
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        protected virtual void OnMoved() => MacNative.PinToDesktop(this);

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            bool right = e.InitialPressMouseButton == MouseButton.Right;
            bool ctrlClick = e.InitialPressMouseButton == MouseButton.Left && (e.KeyModifiers & KeyModifiers.Control) != 0;
            if (!e.Handled && (right || ctrlClick))
            {
                Shell.ShowMenu();
                e.Handled = true;
            }
        }
    }
}
