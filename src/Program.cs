using System;
using Velopack;

namespace ClickUpWidget
{
    public static class Program
    {
        /// <summary>Перший запуск після встановлення: одразу показати вікно з токеном.</summary>
        public static bool FirstRun { get; private set; }

        [STAThread]
        public static void Main(string[] args)
        {
            // Має бути першим: інсталятор і оновлювач запускають програму з особливими аргументами.
            VelopackApp.Build()
                .OnBeforeUninstallFastCallback(_ => AutostartHelper.Apply(false))
                .OnFirstRun(_ => FirstRun = true)
                .Run();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
