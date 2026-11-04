using System;
using System.IO;

namespace ClickUpWidget
{
    /// <summary>Журнал log.txt у теці налаштувань — перше, що дивитись при багах.</summary>
    internal static class Log
    {
        private static readonly object Gate = new();

        public static void Write(string text)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Settings.DataDir);
                    string path = Path.Combine(Settings.DataDir, "log.txt");
                    // Журнал не росте безкінечно: понад 256 КБ починаємо заново.
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > 256 * 1024) info.Delete();
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
