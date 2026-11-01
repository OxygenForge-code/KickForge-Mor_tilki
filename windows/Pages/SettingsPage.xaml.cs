using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KickForgeDesktop.Services;
using KickForgeDesktop.Settings;

namespace KickForgeDesktop.Pages
{
    public partial class SettingsPage : UserControl
    {
        private readonly MainWindow _shell;
        private bool _loading = true;
        private List<OutputDevice> _devices = new List<OutputDevice>();

        public SettingsPage(MainWindow shell)
        {
            _shell = shell;
            InitializeComponent();
            DataContext = shell.Settings;

            Loaded += (_, _) => LoadFromSettings();
            shell.Settings.PropertyChanged += OnSettingChanged;
            shell.StatsUpdated += (_, _) =>
            {
                if (TestResult.Text.StartsWith("test ediliyor", StringComparison.Ordinal))
                {
                    TestResult.Text = _shell.Latest == null ? "veri yok" : "tamam · " + _shell.Latest.FollowersCount + " takipçi";
                }
            };
        }

        private AppSettings Settings => _shell.Settings;

        public void LoadFromSettings()
        {
            _loading = true;

            ThemeRoom.IsChecked = Settings.Theme == AppTheme.ControlRoom;
            ThemeMidnight.IsChecked = Settings.Theme == AppTheme.Midnight;
            ThemeSakura.IsChecked = Settings.Theme == AppTheme.Sakura;
            ThemeMono.IsChecked = Settings.Theme == AppTheme.Mono;

            SoundNone.IsChecked = Settings.Sound == NotifySound.None;
            SoundPulse.IsChecked = Settings.Sound == NotifySound.Pulse;
            SoundBlip.IsChecked = Settings.Sound == NotifySound.Blip;
            SoundChime.IsChecked = Settings.Sound == NotifySound.Chime;

            _loading = false;

            RefreshDevices(false);
            DataPath.Text = "ayarlar: " + SettingsStore.FilePath;
            AutoStartHint.Text = AutoStart.IsEnabled
                ? "şu an kayıtlı · windows açılınca başlıyor"
                : "kayıt defterine yazılır (yönetici gerekmez)";
        }

        private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_loading) return;

            switch (e.PropertyName)
            {
                case nameof(AppSettings.PollSeconds):
                    _shell.RestartTimer();
                    break;
                case nameof(AppSettings.StartWithWindows):
                    AutoStart.Apply(Settings.StartWithWindows);
                    AutoStartHint.Text = Settings.StartWithWindows
                        ? "şu an kayıtlı · windows açılınca başlıyor"
                        : "kayıt defterine yazılır (yönetici gerekmez)";
                    break;
                case nameof(AppSettings.LogToFile):
                    Log.ToFile = Settings.LogToFile;
                    break;
                case nameof(AppSettings.HotkeysEnabled):
                    HotkeyResult.Text = _shell.RestartHotkeys();
                    break;
            }

            _shell.ApplySettings();
            _shell.SaveSettingsSoon();
        }

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Settings.Theme = sender == ThemeMidnight ? AppTheme.Midnight
                : sender == ThemeSakura ? AppTheme.Sakura
                : sender == ThemeMono ? AppTheme.Mono
                : AppTheme.ControlRoom;
        }

        private void Sound_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Settings.Sound = sender == SoundPulse ? NotifySound.Pulse
                : sender == SoundBlip ? NotifySound.Blip
                : sender == SoundChime ? NotifySound.Chime
                : NotifySound.None;
        }

        private void RefreshDevices(bool announce)
        {
            var selectedId = Settings.AudioDeviceId;
            _devices = new List<OutputDevice> { OutputDevice.SystemDefault };
            _devices.AddRange(_shell.Audio.Devices());

            DeviceList.ItemsSource = _devices;
            DeviceList.DisplayMemberPath = "Name";

            var selected = _devices.FirstOrDefault(d => d.Id == selectedId) ?? _devices[0];
            DeviceList.SelectedItem = selected;

            DeviceHint.Text = _devices.Count > 1
                ? (_devices.Count - 1) + " ses çıkışı bulundu"
                : "ses çıkışı bulunamadı (sürücü yok mu?)";

            if (announce) Log.Write("ses cihazları tazelendi: " + (_devices.Count - 1));
        }

        private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (DeviceList.SelectedItem is not OutputDevice device) return;

            Settings.AudioDeviceId = device.Id;
            _shell.Audio.PlayTone(device.Id, 660, 170, Settings.SoundVolume);
            DeviceHint.Text = device.Index < 0
                ? "sistem varsayılanı kullanılıyor"
                : device.Name + " seçildi · kısa bir test sesi çalındı";
        }

        private void RefreshDevices_Click(object sender, RoutedEventArgs e) => RefreshDevices(true);

        private void TestDevice_Click(object sender, RoutedEventArgs e)
        {
            var device = DeviceList.SelectedItem as OutputDevice;
            if (device == null) return;
            _shell.Audio.PlayTone(device.Id, 528, 900, Settings.SoundVolume);
            DeviceHint.Text = "528 Hz test sesi · " + device.Name;
        }

        private void PreviewSound_Click(object sender, RoutedEventArgs e) => _shell.TestNotificationSound();

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            TestResult.Text = "test ediliyor…";
            _shell.RefreshNow(true);
            await System.Threading.Tasks.Task.Delay(4000);
            if (_shell.Latest == null)
            {
                TestResult.Text = "veri alınamadı · site düşükse 'kick.com'dan doğrudan oku' açık olsun";
            }
        }

        private void ApplyHotkeys_Click(object sender, RoutedEventArgs e)
        {
            HotkeyResult.Text = _shell.RestartHotkeys();
            _shell.SaveSettingsSoon();
        }

        private void OpenData_Click(object sender, RoutedEventArgs e) => _shell.OpenDataFolder();

        private void OpenLog_Click(object sender, RoutedEventArgs e) => _shell.OpenLogFile();

        private void Export_Click(object sender, RoutedEventArgs e) => _shell.ExportSettings();

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            var answer = MessageBox.Show("Tüm ayarlar varsayılana dönsün mü?", "KickForge",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            _shell.ResetSettings();
            LoadFromSettings();
        }
    }
}
