using System.Reflection;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace ClickUpWidget
{
    /// <summary>Оновлення з релізів GitHub (github.com/dimasdonetsk/clickupwidget/releases).</summary>
    internal static class Updater
    {
        public const string RepoUrl = "https://github.com/dimasdonetsk/clickupwidget";

        private static UpdateManager? _manager;

        private static UpdateManager Manager =>
            _manager ??= new UpdateManager(new GithubSource(RepoUrl, null!, false));

        /// <summary>Чи встановлена програма через Setup (лише тоді працює оновлення).</summary>
        public static bool IsInstalled
        {
            get
            {
                try { return Manager.IsInstalled; }
                catch { return false; }
            }
        }

        public static string CurrentVersion
        {
            get
            {
                try
                {
                    if (Manager.CurrentVersion is { } v) return v.ToString();
                }
                catch { }
                System.Version? asm = Assembly.GetExecutingAssembly().GetName().Version;
                return asm == null ? "?" : asm.Major + "." + asm.Minor + "." + asm.Build;
            }
        }

        public static Task<UpdateInfo?> CheckAsync() => Manager.CheckForUpdatesAsync();

        public static Task DownloadAsync(UpdateInfo info) => Manager.DownloadUpdatesAsync(info);

        /// <summary>Застосувати оновлення і перезапустити програму. Поточний процес завершиться.</summary>
        public static void ApplyAndRestart(UpdateInfo info) => Manager.ApplyUpdatesAndRestart(info.TargetFullRelease);

        /// <summary>
        /// Тихе оновлення: оновлювач чекає, поки програма закриється, ставить нову версію без жодних вікон
        /// і запускає її знову. Після виклику програму треба закрити.
        /// </summary>
        public static void ApplySilentlyOnExit(UpdateInfo info) =>
            Manager.WaitExitThenApplyUpdates(info.TargetFullRelease, silent: true, restart: true, restartArgs: new[] { "--updated" });

        public static string VersionOf(UpdateInfo info) => info.TargetFullRelease.Version.ToString();
    }
}
