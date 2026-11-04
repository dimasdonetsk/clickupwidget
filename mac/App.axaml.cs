using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ClickUpWidget.Shared;

namespace ClickUpWidget.Mac
{
    public partial class App : Application
    {
        private FileStream? _instanceLock;

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                Dispatcher.UIThread.UnhandledException += (_, e) =>
                {
                    Log.Write("Unhandled: " + e.Exception);
                    e.Handled = true;
                };
                AppDomain.CurrentDomain.UnhandledException += (_, ev) => Log.Write("Crash: " + ev.ExceptionObject);
                System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ev) =>
                {
                    Log.Write("Task: " + ev.Exception.GetBaseException().Message);
                    ev.SetObserved();
                };

                if (Demo.IsRequested)
                {
                    Demo.Start(desktop);
                }
                // Лише одна копія програми.
                else if (!TryLockInstance())
                {
                    Log.Write("Already running, exit");
                    // Закривати можна лише після старту головного циклу.
                    Dispatcher.UIThread.Post(() => desktop.Shutdown());
                }
                else
                {
                    var shell = new MacShell(desktop);
                    desktop.Exit += (_, _) =>
                    {
                        shell.Controller.Dispose();
                        _instanceLock?.Dispose();
                    };
                    shell.Start();
                    Log.Write("Started " + Updater.CurrentVersion + " on " + Environment.OSVersion +
                              (Program.Args.Length > 0 ? " (" + string.Join(" ", Program.Args) + ")" : ""));
                }
            }
            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>Файл-замок у теці налаштувань: друга копія його не відкриє.</summary>
        private bool TryLockInstance()
        {
            try
            {
                Directory.CreateDirectory(Settings.DataDir);
                _instanceLock = new FileStream(Path.Combine(Settings.DataDir, "instance.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (IOException) { return false; }
            catch (Exception ex)
            {
                Log.Write("Instance lock: " + ex.Message);
                return true;
            }
        }

        /// <summary>Кольори теми з Palette (core) у ресурси вікон; Fluent-контроли (поле, меню) — у тій самій темі.</summary>
        public void ApplyTheme(bool light)
        {
            foreach ((string key, string hex) in Palette.Theme(light))
                Resources[key] = new SolidColorBrush(Color.Parse(hex));
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        }
    }
}
