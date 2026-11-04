using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ClickUpWidget
{
    public partial class App : Application
    {
        private Mutex? _mutex;
        private AppController? _controller;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Лише одна копія програми.
            _mutex = new Mutex(true, "ClickUpWidget.SingleInstance.v1", out bool createdNew);
            if (!createdNew)
            {
                Log.Write("Already running, exit");
                Shutdown();
                return;
            }

            DispatcherUnhandledException += OnUnhandled;
            AppDomain.CurrentDomain.UnhandledException += (_, ev) => Log.Write("Crash: " + ev.ExceptionObject);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ev) =>
            {
                Log.Write("Task: " + ev.Exception.GetBaseException().Message);
                ev.SetObserved();
            };

            _controller = new AppController();
            _controller.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _controller?.Dispose();
            try { _mutex?.ReleaseMutex(); } catch { }
            _mutex?.Dispose();
            base.OnExit(e);
        }

        private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Write("Unhandled: " + e.Exception);
            e.Handled = true;
        }
    }
}
