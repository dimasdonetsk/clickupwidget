using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ClickUpWidget.Shared;

namespace ClickUpWidget.Mac
{
    /// <summary>
    /// Перевірка без ClickUp і без Мака під рукою:
    ///   --demo                 вікна зі зразковими задачами (налаштування в тимчасовій теці, справжні не чіпаються);
    ///   --screenshot ТЕКА      те саме, плюс PNG кожного вікна в ТЕКУ, і вихід;
    ///   --icons ТЕКА           PNG значка програми потрібних розмірів (для AppIcon.icns), і вихід;
    ///   --light                світла тема в демо;
    ///   --selftest             Keychain і автозапуск (без вікон), код виходу 0/1.
    /// </summary>
    internal static class Demo
    {
        public static bool IsRequested => Has("--demo") || Arg("--screenshot") != null || Arg("--icons") != null;

        private static bool Has(string name) => Program.Args.Contains(name);

        private static string? Arg(string name)
        {
            int i = Array.IndexOf(Program.Args, name);
            return i >= 0 && i + 1 < Program.Args.Length ? Program.Args[i + 1] : null;
        }

        public static void Start(IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Arg("--icons") is string iconDir)
            {
                Directory.CreateDirectory(iconDir);
                foreach (int size in new[] { 16, 32, 64, 128, 256, 512, 1024 })
                    Icons.SaveAppIcon(Path.Combine(iconDir, "icon_" + size + ".png"), size);
                Console.WriteLine("Icons → " + iconDir);
                // Закривати можна лише після старту головного циклу.
                Dispatcher.UIThread.Post(() => desktop.Shutdown());
                return;
            }

            // Справжні налаштування, інбокс і журнал не чіпаємо.
            string dir = Path.Combine(Path.GetTempPath(), "ClickUpWidget-demo");
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            Settings.DataDir = dir;
            var settings = new Settings { Theme = Has("--light") ? "Light" : "Dark", RowShowPriority = true };
            settings.Save();

            var shell = new MacShell(desktop, offline: true);
            desktop.Exit += (_, _) => shell.Controller.Dispose();
            shell.Start();
            Fill(shell.Controller);

            if (Arg("--screenshot") is string shots)
                _ = CaptureAndExit(shell, shots);
        }

        private static async Task CaptureAndExit(MacShell shell, string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                await Task.Delay(2500);
                shell.ToggleFlyout();
                await Task.Delay(1500);
                Save(shell.Windows, dir);

                // Вікно токена — окремо (поверх інших).
                shell.ShowTokenWindow();
                await Task.Delay(1500);
                Save(shell.Windows.Where(w => w is TokenWindow), dir);

                // Цілий екран (з рядком меню й тим, як вікна лежать на столі) — системною утилітою.
                if (OperatingSystem.IsMacOS())
                {
                    using Process? p = Process.Start("/usr/sbin/screencapture", new[] { "-x", Path.Combine(dir, "screen.png") });
                    p?.WaitForExit(10000);
                }
                Console.WriteLine("Screenshots → " + dir);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Screenshot failed: " + ex);
                Environment.ExitCode = 1;
            }
            shell.Shutdown();
        }

        private static void Save(IEnumerable<Window> windows, string dir)
        {
            foreach (Window w in windows.ToList())
            {
                double scale = w.RenderScaling;
                var size = new PixelSize((int)Math.Ceiling(w.Bounds.Width * scale), (int)Math.Ceiling(w.Bounds.Height * scale));
                if (size.Width <= 0 || size.Height <= 0) continue;
                using var bmp = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
                bmp.Render(w);
                string name = w.GetType().Name + (Palette.IsLight ? "-light" : "") + ".png";
                bmp.Save(Path.Combine(dir, name));
                Console.WriteLine("  " + name + " " + size.Width + "×" + size.Height + " @" + w.Position);
            }
        }

        /// <summary>Зразкові задачі й події — як у звичайний робочий день.</summary>
        private static void Fill(WidgetController c)
        {
            DateTime today = DateTime.Today;
            TaskItem Task(string id, string raw, string status, string color, string type, int order, DateTime? due = null, string? priority = null)
            {
                (string project, string title) = TaskText.Split(raw);
                return new TaskItem
                {
                    Id = id, RawName = raw, Project = project, Title = title, Status = status, StatusColor = color,
                    StatusType = type, StatusOrder = order, Due = due, Priority = priority, ListId = "list1",
                    Url = "https://app.clickup.com/t/" + id,
                };
            }

            var tasks = new List<TaskItem>
            {
                Task("t1", "АП: Підготувати звіт за жовтень", "in progress", "#4194F6", "custom", 2, today, "high"),
                Task("t2", "GM: Узгодити макет банера", "in progress", "#4194F6", "custom", 2, today.AddDays(-1)),
                Task("t3", "ML: Розмітка датасету — друга партія", "review", "#A875FF", "custom", 3, today.AddDays(2), "normal"),
                Task("t4", "АП: Перевірити рахунки постачальників", "to do", "#87909E", "open", 1, today.AddDays(5)),
                Task("t5", "Зідзвон з командою дизайну", "to do", "#87909E", "open", 1),
                Task("t6", "GM: Тексти для розсилки", "to do", "#87909E", "open", 1, null, "urgent"),
            };
            var running = new RunningTimer("t3", tasks[2].RawName, DateTime.UtcNow.AddMinutes(-47).AddSeconds(-12));

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            InboxEvent Event(string kind, TaskItem t, int minutesAgo, string who = "", string text = "",
                string from = "", string? fromColor = null, string to = "", string? toColor = null) => new()
            {
                Kind = kind, Key = kind + t.Id + minutesAgo, TaskId = t.Id, Project = t.Project, Title = t.Title, Url = t.Url,
                Who = who, WhoId = who.Length > 0 ? 42 : 0, Text = text, From = from, FromColor = fromColor, To = to, ToColor = toColor,
                AtMs = now - minutesAgo * 60_000L,
            };
            var events = new List<InboxEvent>
            {
                Event(Inbox.KindMention, tasks[0], 4, "Олена", "@Діма глянь, будь ласка, цифри в другому розділі — здається, там старі дані."),
                Event(Inbox.KindComment, tasks[1], 35, "Андрій", "Макет оновив, посилання в описі."),
                Event(Inbox.KindStatus, tasks[2], 80, from: "to do", fromColor: "#87909E", to: "review", toColor: "#A875FF"),
                Event(Inbox.KindDue, tasks[3], 190, from: "31 жов", to: "9 лис"),
            };
            c.LoadSnapshot("Діма", tasks, running, TimeSpan.FromMinutes(95), events, now - 60 * 60_000L);
        }

        // ───────────────────────────── самоперевірка без вікон ─────────────────────────────

        /// <summary>Keychain (запис/читання/видалення) і LaunchAgent. Для CI на Маку.</summary>
        public static int SelfTest()
        {
            int failed = 0;
            void Check(string name, bool ok, string details = "")
            {
                Console.WriteLine((ok ? "OK   " : "FAIL ") + name + (details.Length > 0 ? " — " + details : ""));
                if (!ok) failed++;
            }

            Console.WriteLine("ClickUp Widget " + Updater.CurrentVersion + " · " + Environment.OSVersion + " · " +
                              System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
            Console.WriteLine("DataDir: " + Settings.DataDir);
            Check("DataDir у ~/Library/Application Support", !OperatingSystem.IsMacOS() || Settings.DataDir.Contains("/Library/Application Support/"));

            if (OperatingSystem.IsMacOS())
            {
                const string service = "ClickUpWidget.SelfTest", account = "selftest";
                string secret = "pk_123456_SELFTEST" + Environment.ProcessId;
                bool written = Keychain.Write(service, account, secret);
                Check("Keychain: запис і читання", written && Keychain.Read(service, account) == secret);
                Keychain.Write(service, account, secret + "2");
                Check("Keychain: заміна", Keychain.Read(service, account) == secret + "2");
                Keychain.Delete(service, account);
                Check("Keychain: видалення", Keychain.Read(service, account) == null);

                string plist = Path.Combine(Path.GetTempPath(), "clickupwidget-selftest.plist");
                File.WriteAllText(plist, LoginItem.Build("/Applications/ClickUp Widget.app"));
                using Process? lint = Process.Start(new ProcessStartInfo("/usr/bin/plutil", new[] { "-lint", plist }) { RedirectStandardOutput = true });
                string output = lint?.StandardOutput.ReadToEnd().Trim() ?? "";
                lint?.WaitForExit();
                Check("LaunchAgent plist", lint?.ExitCode == 0, output);
            }

            Console.WriteLine(failed == 0 ? "Усе гаразд" : "Помилок: " + failed);
            return failed == 0 ? 0 : 1;
        }
    }
}
