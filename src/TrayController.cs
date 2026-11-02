using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using WF = System.Windows.Forms;

namespace ClickUpWidget
{
    /// <summary>Значок у треї і темне меню правого кліку.</summary>
    internal sealed class TrayController : IDisposable
    {
        private readonly AppController _app;
        private readonly WF.NotifyIcon _icon;
        private readonly WF.ContextMenuStrip _menu;
        private bool? _iconRunning;

        public TrayController(AppController app)
        {
            _app = app;
            _menu = new WF.ContextMenuStrip
            {
                Renderer = new DarkMenuRenderer(),
                ShowImageMargin = false,
                ShowCheckMargin = true,
                Font = new Font("Segoe UI", 10f),
                BackColor = DarkMenuRenderer.Back,
                ForeColor = DarkMenuRenderer.Text,
            };
            _menu.Opening += (_, _) => BuildMenu();

            _icon = new WF.NotifyIcon
            {
                Text = "ClickUp Widget",
                ContextMenuStrip = _menu,
                Visible = true,
                Icon = MakeIcon(false),
            };
            _icon.MouseClick += (_, e) =>
            {
                if (e.Button == WF.MouseButtons.Left) _app.ToggleFlyout(WF.Cursor.Position);
            };
        }

        public void Update(bool timerRunning, string tooltip)
        {
            // Підказка в треї обмежена 127 символами.
            _icon.Text = tooltip.Length > 120 ? tooltip.Substring(0, 120) : tooltip;
            if (_iconRunning != timerRunning)
            {
                Icon old = _icon.Icon;
                _icon.Icon = MakeIcon(timerRunning);
                old?.Dispose();
                _iconRunning = timerRunning;
            }
        }

        public void ShowMenuAtCursor() => _menu.Show(WF.Cursor.Position);

        // ───────────────────────────── меню ─────────────────────────────

        private void BuildMenu()
        {
            Settings s = _app.Settings;

            DisposeItems(_menu.Items);
            _menu.Items.Clear();

            _menu.Items.Add(Item("Оновити зараз", () => _app.RefreshNow()));
            _menu.Items.Add(Item("Відкрити ClickUp", () => _app.OpenClickUp()));
            _menu.Items.Add(new WF.ToolStripSeparator());

            _menu.Items.Add(Toggle("Панель на робочому столі", s.ShowPanel, v => _app.SetPanelVisible(v)));
            _menu.Items.Add(Toggle("Закріпити положення панелі", s.LockPanel, v => _app.Change(x => x.LockPanel = v)));

            IReadOnlyList<(string Id, string Name)> teams = _app.Teams;
            if (teams.Count > 1)
            {
                string current = teams.FirstOrDefault(t => t.Id == s.TeamId).Name ?? "";
                var ws = Sub("Робочий простір" + (current.Length > 0 ? "   ·  " + current : ""));
                foreach ((string id, string name) in teams)
                {
                    string captured = id;
                    ws.DropDownItems.Add(Radio(name, id == s.TeamId, () => _app.SetTeam(captured)));
                }
                _menu.Items.Add(ws);
            }

            var refresh = Sub("Оновлювати задачі");
            foreach ((string label, int minutes) in new[] { ("Щохвилини", 1), ("Кожні 2 хв", 2), ("Кожні 5 хв", 5), ("Кожні 15 хв", 15) })
            {
                int m = minutes;
                refresh.DropDownItems.Add(Radio(label, s.RefreshMinutes == m, () => _app.SetRefreshMinutes(m)));
            }
            _menu.Items.Add(refresh);

            var theme = Sub("Тема");
            theme.DropDownItems.Add(Radio("Темна", s.Theme != "Light", () => _app.SetTheme(false)));
            theme.DropDownItems.Add(Radio("Світла", s.Theme == "Light", () => _app.SetTheme(true)));
            _menu.Items.Add(theme);

            _menu.Items.Add(Toggle("Запускати при вході", s.Autostart && !AutostartHelper.DisabledByWindows(), v => _app.SetAutostart(v)));
            _menu.Items.Add(new WF.ToolStripSeparator());

            string who = _app.UserName.Length > 0 ? "   ·  " + _app.UserName : "";
            _menu.Items.Add(Item((s.TokenEnc == null ? "Підключити ClickUp…" : "Змінити токен…") + who, () => _app.ShowTokenWindow()));
            _menu.Items.Add(Item("Перевірити оновлення", () => _app.CheckUpdatesManual()));
            _menu.Items.Add(Info("Версія " + Updater.CurrentVersion));
            _menu.Items.Add(Item("Відкрити папку налаштувань", () => Open(Settings.DataDir)));
            _menu.Items.Add(new WF.ToolStripSeparator());

            _menu.Items.Add(Item("Вийти", () => _app.Exit()));
        }

        private static WF.ToolStripMenuItem Item(string text, Action onClick)
        {
            var item = new WF.ToolStripMenuItem(text) { ForeColor = DarkMenuRenderer.Text };
            item.Click += (_, _) => onClick();
            return item;
        }

        private static WF.ToolStripMenuItem Radio(string text, bool isChecked, Action onClick)
        {
            WF.ToolStripMenuItem item = Item(text, onClick);
            item.Checked = isChecked;
            return item;
        }

        private static WF.ToolStripMenuItem Toggle(string text, bool isChecked, Action<bool> onChange)
        {
            var item = new WF.ToolStripMenuItem(text) { ForeColor = DarkMenuRenderer.Text, Checked = isChecked };
            item.Click += (_, _) => onChange(!isChecked);
            return item;
        }

        private static WF.ToolStripMenuItem Sub(string text)
        {
            var item = new WF.ToolStripMenuItem(text) { ForeColor = DarkMenuRenderer.Text };
            item.DropDown.BackColor = DarkMenuRenderer.Back;
            if (item.DropDown is WF.ToolStripDropDownMenu dd)
            {
                dd.ShowImageMargin = false;
                dd.ShowCheckMargin = true;
                dd.Renderer = new DarkMenuRenderer();
            }
            return item;
        }

        private static WF.ToolStripMenuItem Info(string text) =>
            new WF.ToolStripMenuItem(text) { Enabled = false, ForeColor = DarkMenuRenderer.Muted };

        private static void DisposeItems(WF.ToolStripItemCollection items)
        {
            foreach (WF.ToolStripItem item in items.Cast<WF.ToolStripItem>().ToList())
            {
                if (item is WF.ToolStripMenuItem mi) DisposeItems(mi.DropDownItems);
                item.Dispose();
            }
        }

        internal static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Open: " + ex.Message); }
        }

        // ───────────────────────────── значок ─────────────────────────────

        /// <summary>Синій квадратик з галочкою; коли йде таймер — зелена крапка в куті.</summary>
        public static Icon MakeIcon(bool timerRunning)
        {
            using var bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (GraphicsPath box = Rounded(new RectangleF(2f, 2f, 28f, 28f), 7f))
                using (var fill = new SolidBrush(ColorTranslator.FromHtml("#7AA7FF")))
                    g.FillPath(fill, box);

                using (var pen = new Pen(ColorTranslator.FromHtml("#0E0E11"), 3.4f)
                       { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] { new PointF(9f, 16.5f), new PointF(14f, 21.5f), new PointF(23f, 11.5f) });

                if (timerRunning)
                {
                    using var ring = new SolidBrush(ColorTranslator.FromHtml("#0E0E11"));
                    g.FillEllipse(ring, 19f, 19f, 13f, 13f);
                    using var dot = new SolidBrush(ColorTranslator.FromHtml("#6FD39A"));
                    g.FillEllipse(dot, 21f, 21f, 9f, 9f);
                }
            }

            IntPtr handle = bmp.GetHicon();
            try { return (Icon)Icon.FromHandle(handle).Clone(); }
            finally { Native.DestroyIcon(handle); }
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
        }
    }

    /// <summary>Чорне меню у стилі Windows 11.</summary>
    internal sealed class DarkMenuRenderer : WF.ToolStripProfessionalRenderer
    {
        public static readonly Color Back = Color.FromArgb(30, 30, 30);
        public static readonly Color Hover = Color.FromArgb(51, 51, 51);
        public static readonly Color Border = Color.FromArgb(51, 51, 51);
        public static readonly Color Text = Color.FromArgb(242, 242, 242);
        public static readonly Color Muted = Color.FromArgb(160, 160, 160);
        public static readonly Color Separator = Color.FromArgb(56, 56, 56);

        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(WF.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : Muted;
            base.OnRenderItemText(e);
        }

        // Фон і підсвітку малюємо самі: інакше Windows підставляє світло-блакитну, і білого тексту не видно.
        protected override void OnRenderToolStripBackground(WF.ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Back);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderImageMargin(WF.ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Back);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(WF.ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Border);
            Rectangle r = e.AffectedBounds;
            e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(WF.ToolStripItemRenderEventArgs e)
        {
            var bounds = new Rectangle(Point.Empty, e.Item.Size);
            using (var back = new SolidBrush(Back))
                e.Graphics.FillRectangle(back, bounds);

            bool highlighted = e.Item.Enabled && (e.Item.Selected || (e.Item is WF.ToolStripMenuItem mi && mi.DropDown.Visible));
            if (!highlighted) return;

            var r = new Rectangle(4, 1, bounds.Width - 8, bounds.Height - 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRect(r, 4);
            using var hover = new SolidBrush(Hover);
            e.Graphics.FillPath(hover, path);
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnRenderArrow(WF.ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Color.FromArgb(208, 208, 208);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(WF.ToolStripItemImageRenderEventArgs e)
        {
            // Тонка біла галочка замість стандартної рамки з квадратом.
            Rectangle r = e.ImageRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Text, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            float x = r.Left + r.Width / 2f - 6f, y = r.Top + r.Height / 2f;
            e.Graphics.DrawLines(pen, new[] { new PointF(x, y), new PointF(x + 4f, y + 4f), new PointF(x + 11f, y - 5f) });
        }

        protected override void OnRenderSeparator(WF.ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using var pen = new Pen(Separator);
            e.Graphics.DrawLine(pen, 6, y, e.Item.Width - 6, y);
        }

        private sealed class DarkColors : WF.ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Back;
            public override Color ImageMarginGradientBegin => Back;
            public override Color ImageMarginGradientMiddle => Back;
            public override Color ImageMarginGradientEnd => Back;
            public override Color MenuBorder => Border;
            public override Color MenuItemBorder => Hover;
            public override Color MenuItemSelected => Hover;
            public override Color MenuItemSelectedGradientBegin => Hover;
            public override Color MenuItemSelectedGradientEnd => Hover;
            public override Color MenuItemPressedGradientBegin => Hover;
            public override Color MenuItemPressedGradientMiddle => Hover;
            public override Color MenuItemPressedGradientEnd => Hover;
            public override Color CheckBackground => Back;
            public override Color CheckSelectedBackground => Hover;
            public override Color CheckPressedBackground => Hover;
            public override Color SeparatorDark => Separator;
            public override Color SeparatorLight => Separator;
        }
    }
}
