using System;
using Microsoft.Win32;

namespace KickForgeDesktop.Services
{
    /// <summary>Windows ile birlikte açılma (HKCU, yönetici gerekmez).</summary>
    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "KickForge";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                    return key?.GetValue(ValueName) != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void Apply(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (key == null) return;
                if (enabled)
                {
                    var path = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(path)) return;
                    key.SetValue(ValueName, "\"" + path + "\" --startup");
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
            catch (Exception ex)
            {
                Log.Write("otomatik başlatma ayarlanamadı: " + ex.Message);
            }
        }
    }
}
