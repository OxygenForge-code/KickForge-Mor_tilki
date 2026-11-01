using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KickForgeDesktop.Settings
{
    public static class SettingsStore
    {
        private static readonly object Sync = new object();
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        // eski electron sürümü %APPDATA%\KickForge klasörünü kendi önbelleğiyle
        // doldurmuştu; native uygulama kendi alt klasöründe dursun.
        public static string Folder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KickForge", "native");

        public static string FilePath => Path.Combine(Folder, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new AppSettings();
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
                return loaded ?? new AppSettings();
            }
            catch (Exception ex)
            {
                Log.Write("ayarlar okunamadı: " + ex.Message);
                return new AppSettings();
            }
        }

        public static void Save(AppSettings settings)
        {
            lock (Sync)
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    var json = JsonSerializer.Serialize(settings, Options);
                    // yarım yazılmış dosya kalmasın: önce yanına yaz, sonra taşı
                    var tmp = FilePath + ".tmp";
                    File.WriteAllText(tmp, json);
                    File.Copy(tmp, FilePath, true);
                    File.Delete(tmp);
                }
                catch (Exception ex)
                {
                    Log.Write("ayarlar yazılamadı: " + ex.Message);
                }
            }
        }
    }
}
