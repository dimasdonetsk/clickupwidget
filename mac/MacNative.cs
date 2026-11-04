using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace ClickUpWidget.Mac
{
    /// <summary>
    /// Маківські дрібниці через Objective-C runtime: вікно на рівні робочого столу, активація програми,
    /// положення курсора. На інших ОС (запуск на Windows для перевірки вигляду) нічого не роблять.
    /// </summary>
    internal static class MacNative
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

        [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendNInt(IntPtr receiver, IntPtr selector, nint value);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendNUInt(IntPtr receiver, IntPtr selector, nuint value);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern CGPoint SendPoint(IntPtr receiver, IntPtr selector);

        [DllImport(CoreGraphics)] private static extern int CGWindowLevelForKey(int key);

        [StructLayout(LayoutKind.Sequential)]
        private struct CGPoint { public double X, Y; }

        private const int kCGDesktopIconWindowLevelKey = 18;
        // NSWindowCollectionBehavior: на всіх робочих столах (Spaces), не рухається в Mission Control, не в Cmd+`.
        private const nuint CanJoinAllSpaces = 1 << 0;
        private const nuint Stationary = 1 << 4;
        private const nuint IgnoresCycle = 1 << 6;

        /// <summary>
        /// Аналог DesktopPin із Windows: вікно лежить одразу над іконками робочого столу, під усіма вікнами програм,
        /// видно на всіх робочих столах і не ховається, коли Mission Control/«Показати стіл» розсуває вікна.
        /// </summary>
        public static void PinToDesktop(Window window)
        {
            if (!OperatingSystem.IsMacOS()) return;
            try
            {
                IntPtr ns = NSWindow(window);
                if (ns == IntPtr.Zero) { Log.Write("PinToDesktop: no NSWindow"); return; }
                int level = CGWindowLevelForKey(kCGDesktopIconWindowLevelKey) + 1;
                SendNInt(ns, sel_registerName("setLevel:"), level);
                SendNUInt(ns, sel_registerName("setCollectionBehavior:"), CanJoinAllSpaces | Stationary | IgnoresCycle);
            }
            catch (Exception ex) { Log.Write("PinToDesktop: " + ex.Message); }
        }

        /// <summary>Картка з рядка меню й вікно токена — на всіх робочих столах, поверх інших вікон.</summary>
        public static void FloatOnAllSpaces(Window window)
        {
            if (!OperatingSystem.IsMacOS()) return;
            try
            {
                IntPtr ns = NSWindow(window);
                if (ns != IntPtr.Zero)
                    SendNUInt(ns, sel_registerName("setCollectionBehavior:"), CanJoinAllSpaces | IgnoresCycle);
            }
            catch (Exception ex) { Log.Write("FloatOnAllSpaces: " + ex.Message); }
        }

        /// <summary>Зробити програму активною, щоб у її вікно можна було друкувати (аналог ForceForeground з Windows).</summary>
        public static void ActivateApp()
        {
            if (!OperatingSystem.IsMacOS()) return;
            try
            {
                IntPtr app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                SendBool(app, sel_registerName("activateIgnoringOtherApps:"), true);
            }
            catch (Exception ex) { Log.Write("ActivateApp: " + ex.Message); }
        }

        /// <summary>Курсор у точках екрана (початок — лівий верхній кут головного екрана), або null.</summary>
        public static Point? CursorPoints(double primaryHeightPoints)
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    // [NSEvent mouseLocation]: точки, початок — лівий НИЖНІЙ кут головного екрана.
                    CGPoint p = SendPoint(objc_getClass("NSEvent"), sel_registerName("mouseLocation"));
                    return new Point(p.X, primaryHeightPoints - p.Y);
                }
                if (OperatingSystem.IsWindows() && GetCursorPos(out WinPoint w))
                    return new Point(w.X, w.Y);
            }
            catch (Exception ex) { Log.Write("Cursor: " + ex.Message); }
            return null;
        }

        [StructLayout(LayoutKind.Sequential)] private struct WinPoint { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out WinPoint p);

        private static IntPtr NSWindow(Window window)
        {
            var handle = window.TryGetPlatformHandle();
            if (handle == null || handle.Handle == IntPtr.Zero) return IntPtr.Zero;
            return handle.HandleDescriptor switch
            {
                "NSWindow" => handle.Handle,
                "NSView" => Send(handle.Handle, sel_registerName("window")),
                _ => IntPtr.Zero,
            };
        }

        /// <summary>Відкрити посилання або теку (Finder / браузер за замовчуванням).</summary>
        public static void Open(string target)
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    var psi = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
                    psi.ArgumentList.Add(target);
                    Process.Start(psi)?.Dispose();
                }
                else
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex) { Log.Write("Open: " + ex.Message); }
        }
    }

    /// <summary>
    /// Автозапуск на Mac: LaunchAgent у ~/Library/LaunchAgents (аналог запису в реєстрі Run на Windows).
    /// Працює лише для програми з .app (встановленої), щоб випадково не прописати тестову збірку.
    /// </summary>
    internal static class LoginItem
    {
        public const string Label = "com.dimasdonetsk.clickupwidget";

        private static string PlistPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", Label + ".plist");

        /// <summary>Шлях до ClickUp Widget.app, якщо програма запущена з бандла (…/X.app/Contents/MacOS/ClickUpWidget).</summary>
        public static string? BundlePath()
        {
            string? exe = Environment.ProcessPath;
            if (exe == null) return null;
            DirectoryInfo? macos = Directory.GetParent(exe);
            DirectoryInfo? contents = macos?.Parent;
            DirectoryInfo? app = contents?.Parent;
            if (macos?.Name == "MacOS" && contents?.Name == "Contents" && app != null &&
                app.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return app.FullName;
            return null;
        }

        public static void Apply(bool enabled)
        {
            if (!OperatingSystem.IsMacOS()) return;
            try
            {
                string? bundle = BundlePath();
                if (enabled && bundle != null)
                {
                    string plist = Build(bundle);
                    string path = PlistPath;
                    if (!File.Exists(path) || File.ReadAllText(path) != plist)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllText(path, plist);
                        Log.Write("Autostart: LaunchAgent → " + bundle);
                    }
                }
                else if (!enabled && File.Exists(PlistPath))
                {
                    File.Delete(PlistPath);
                    Log.Write("Autostart: LaunchAgent removed");
                }
            }
            catch (Exception ex) { Log.Write("Autostart: " + ex.Message); }
        }

        /// <summary>LaunchAgent: при вході в систему відкрити бандл (через `open`, як із Finder) з --autostart.</summary>
        internal static string Build(string bundlePath)
        {
            static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                   "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
                   "<plist version=\"1.0\">\n<dict>\n" +
                   "  <key>Label</key>\n  <string>" + Label + "</string>\n" +
                   "  <key>ProgramArguments</key>\n  <array>\n" +
                   "    <string>/usr/bin/open</string>\n" +
                   "    <string>-a</string>\n" +
                   "    <string>" + Esc(bundlePath) + "</string>\n" +
                   "    <string>--args</string>\n" +
                   "    <string>--autostart</string>\n" +
                   "  </array>\n" +
                   "  <key>RunAtLoad</key>\n  <true/>\n" +
                   "  <key>ProcessType</key>\n  <string>Interactive</string>\n" +
                   "</dict>\n</plist>\n";
        }
    }
}
