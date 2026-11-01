using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KickForgeDesktop
{
    /// <summary>
    /// basit günlük: hem bellekte tutulur (geçmiş sayfası) hem dosyaya yazılır.
    /// </summary>
    public static class Log
    {
        private static readonly object Sync = new object();
        private static readonly List<string> Memory = new List<string>();
        private const int MaxMemory = 400;

        public static bool ToFile { get; set; } = true;

        public static string FilePath => Path.Combine(Settings.SettingsStore.Folder, "kickforge.log");

        public static event EventHandler<string>? Added;

        public static IReadOnlyList<string> Lines
        {
            get { lock (Sync) return Memory.ToArray(); }
        }

        public static void Write(string message)
        {
            var line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
            lock (Sync)
            {
                Memory.Add(line);
                if (Memory.Count > MaxMemory) Memory.RemoveRange(0, Memory.Count - MaxMemory);
            }

            Added?.Invoke(null, line);

            if (!ToFile) return;
            try
            {
                Directory.CreateDirectory(Settings.SettingsStore.Folder);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 512 * 1024) File.Delete(FilePath);
                File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd ") + line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // günlüğe yazamıyorsak uygulamayı düşürmeyelim
            }
        }
    }
}
