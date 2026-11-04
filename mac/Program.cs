using System;
using Avalonia;
using Velopack;

namespace ClickUpWidget.Mac
{
    public static class Program
    {
        /// <summary>Перший запуск після встановлення: одразу показати вікно з токеном.</summary>
        public static bool FirstRun { get; private set; }

        /// <summary>Аргументи запуску (--demo, --screenshot тека, --autostart).</summary>
        public static string[] Args { get; private set; } = Array.Empty<string>();

        [STAThread]
        public static void Main(string[] args)
        {
            // Має бути першим: інсталятор і оновлювач запускають програму з особливими аргументами.
            VelopackApp.Build()
                .OnFirstRun(_ => FirstRun = true)
                .Run();

            Args = args;
            if (Array.IndexOf(args, "--selftest") >= 0)
            {
                Environment.Exit(Demo.SelfTest());
                return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                // Без значка в Dock і в Cmd+Tab: віджет живе на столі й у рядку меню.
                .With(new MacOSPlatformOptions { ShowInDock = false, DisableDefaultApplicationMenuItems = true })
                .LogToTrace();
    }
}
