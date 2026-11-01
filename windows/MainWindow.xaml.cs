using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KickForgeDesktop.Models;
using KickForgeDesktop.Pages;
using KickForgeDesktop.Services;
using KickForgeDesktop.Settings;

namespace KickForgeDesktop
{
    public partial class MainWindow : Window
    {
        // ------------------------------------------------------------ taşmayı önleme
        // kenarlıksız pencere (WindowChrome) büyütülünce windows pencereyi çerçeve
        // payı kadar dışarı taşırıyor: sağ/alt kenar ve alt bilgi ekran dışında
        // kalıyordu. büyütme sınırlarını monitörün çalışma alanına sabitliyoruz.
        private const int WmGetMinMaxInfo = 0x0024;
        private const int MonitorDefaultToNearest = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct Point32 { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public Point32 Reserved;
            public Point32 MaxSize;
            public Point32 MaxPosition;
            public Point32 MinTrackSize;
            public Point32 MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect32 { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect32 Monitor;
            public Rect32 Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        private readonly KickClient _client = new KickClient();
        private readonly HotkeyManager _hotkeys = new HotkeyManager();
        private readonly DispatcherTimer _timer;
        private readonly List<FeedEvent> _events = new List<FeedEvent>();
        private readonly LivePage _livePage;
        private readonly StudioPage _studioPage;
        private readonly HistoryPage _historyPage;
        private readonly SettingsPage _settingsPage;
        private readonly AboutPage _aboutPage;

        private TrayService? _tray;
        private bool _pagesReady;
        private bool _refreshing;
        private bool _quitting;
        private bool _fullScreen;
        private WindowState _stateBeforeFullScreen = WindowState.Normal;
        private bool _soundMuted;
        private int _sampleCounter;
        private int _failures;
        private DateTime? _liveSince;
        private DateTime _lastSpikeNotify = DateTime.MinValue;
        private Timer? _saveTimer;

        public AppSettings Settings { get; }
        public HistoryStore History { get; } = new HistoryStore();
        public AudioService Audio { get; } = new AudioService();
        public KickAuthService Auth { get; } = new KickAuthService();
        public ChatFeed ChatFeed { get; } = new ChatFeed();
        public ChannelStats? Latest { get; private set; }
        public string LastSource => _client.LastSource;
        public string LiveDurationText => FormatDuration();
        public IReadOnlyList<FeedEvent> Events => _events;

        public event EventHandler? FeedAdded;
        public event EventHandler? StatsUpdated;

        public MainWindow()
        {
            Settings = App.Current.Settings;
            InitializeComponent();

            History.Load();
            Log.ToFile = Settings.LogToFile;

            _tray = new TrayService();
            _tray.ShowRequested += ShowFromTray;
            _tray.ExitRequested += ExitApp;
            _tray.Update("kanal: " + Settings.ChannelSlug, false, 0, "KickForge · " + Settings.ChannelSlug);

            Auth.Load();

            _livePage = new LivePage(this);
            _studioPage = new StudioPage(this);
            _historyPage = new HistoryPage(this);
            _settingsPage = new SettingsPage(this);
            _aboutPage = new AboutPage(this);
            PageHost.Content = _livePage;
            _pagesReady = true;

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = "sürüm " + (version == null ? "2.0" : version.ToString(2)) + " · native";

            _timer = new DispatcherTimer();
            _timer.Tick += async (_, _) => await RefreshAsync(false);

            SourceInitialized += (_, _) =>
            {
                _hotkeys.Attach(this);

                if (PresentationSource.FromVisual(this) is HwndSource source)
                {
                    source.AddHook(WindowProc);
                }
            };
            Loaded += OnLoaded;
            Closing += OnClosing;
            StateChanged += OnStateChanged;
            Activated += (_, _) => _livePage.SyncToggles();
            PreviewKeyDown += OnPreviewKeyDown;

            ApplySettings();
            Log.Write("uygulama açıldı · native wpf · " + Settings.ApiBase);
        }

        private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.F5:
                    RefreshNow(true);
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.F11:
                    ToggleFullScreen();
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.Escape:
                    if (_fullScreen)
                    {
                        ToggleFullScreen();
                        e.Handled = true;
                    }
                    break;
            }
        }

        /// <summary>f11: görev çubuğu dahil tüm ekranı kaplar; esc geri döndürür.</summary>
        public void ToggleFullScreen()
        {
            if (!_fullScreen)
            {
                _stateBeforeFullScreen = WindowState;
                _fullScreen = true;

                // sınırlar _fullScreen'e göre yeniden hesaplansın
                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
                WindowState = WindowState.Maximized;
                Log.Write("tam ekran açıldı");
            }
            else
            {
                _fullScreen = false;
                WindowState = WindowState.Normal;
                if (_stateBeforeFullScreen == WindowState.Maximized) WindowState = WindowState.Maximized;
                Log.Write("tam ekran kapandı");
            }
        }

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmGetMinMaxInfo) return IntPtr.Zero;

            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return IntPtr.Zero;

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

            // tam ekranda monitörün tamamı, normal büyütmede görev çubuğu hariç
            var area = _fullScreen ? info.Monitor : info.Work;
            var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);

            mmi.MaxPosition.X = area.Left;
            mmi.MaxPosition.Y = area.Top;
            mmi.MaxSize.X = area.Right - area.Left;
            mmi.MaxSize.Y = area.Bottom - area.Top;
            mmi.MaxTrackSize.X = info.Monitor.Right - info.Monitor.Left;
            mmi.MaxTrackSize.Y = info.Monitor.Bottom - info.Monitor.Top;

            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
            return IntPtr.Zero;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RestoreWindowPlacement();
            ApplySettings();
            RestartHotkeys();
            RestartTimer();

            // "açılışta küçük başla" yalnızca windows açılışında geçerli;
            // exe'ye çift tıklayan kişi pencereyi bekler (eskiden tersiydi ve
            // uygulama tepside açılıyordu).
            if (Settings.StartMinimized && App.Current.StartedWithWindows)
            {
                Hide();
                Log.Write("windows açılışı: tepside başladı");
            }

            _ = RefreshAsync(false);
        }

        // ---------------------------------------------------------------- ayarlar

        public void ApplySettings()
        {
            ThemeManager.Apply(Settings.Theme);
            Background = ThemeManager.Get("Bg");
            Opacity = Math.Max(0.4, Math.Min(1, Settings.Opacity / 100.0));
            Topmost = Settings.AlwaysOnTop;
            TitleChannel.Text = "· " + Settings.ChannelSlug;

            Log.ToFile = Settings.LogToFile;
            if (AutoStart.IsEnabled != Settings.StartWithWindows) AutoStart.Apply(Settings.StartWithWindows);

            StatusHint.Text = Settings.HotkeysEnabled
                ? "kısayol: " + Settings.HotkeyShow + " göster · " + Settings.HotkeyTop + " üstte"
                : "kısayollar kapalı";

            _livePage.SyncToggles();
            StatsUpdated?.Invoke(this, EventArgs.Empty);
        }

        public void SaveSettings()
        {
            SettingsStore.Save(Settings);
        }

        public void SaveSettingsSoon()
        {
            _saveTimer?.Dispose();
            _saveTimer = new Timer(_ => Dispatcher.BeginInvoke(new Action(SaveSettings)), null, 500, Timeout.Infinite);
        }

        public string RestartHotkeys()
        {
            _hotkeys.Clear();
            if (!Settings.HotkeysEnabled) return "kısayollar kapalı";

            _hotkeys.Register(Settings.HotkeyShow, ShowFromTray, "göster");
            _hotkeys.Register(Settings.HotkeyTop, ToggleTopmost, "üstte");
            _hotkeys.Register(Settings.HotkeyMute, ToggleMute, "sessiz");

            return _hotkeys.Failures.Count == 0
                ? "kısayollar kaydedildi"
                : string.Join(" · ", _hotkeys.Failures);
        }

        public void RestartTimer()
        {
            _timer.Stop();
            _timer.Interval = TimeSpan.FromSeconds(Math.Max(5, Settings.PollSeconds));
            _timer.Start();
        }

        public void SetAlwaysOnTop(bool value, bool save)
        {
            Settings.AlwaysOnTop = value;
            Topmost = value;
            _livePage.SyncToggles();
            if (save) SaveSettingsSoon();
        }

        public void SetTrayMode(bool value)
        {
            Settings.MinimizeToTray = value;
            _livePage.SyncToggles();
            SaveSettingsSoon();
        }

        private void ToggleTopmost() => Dispatcher.Invoke(() => SetAlwaysOnTop(!Settings.AlwaysOnTop, true));

        private void ToggleMute()
        {
            _soundMuted = !_soundMuted;
            Dispatcher.Invoke(() => AddEvent("ses", _soundMuted ? "bildirim sesleri kapatıldı" : "bildirim sesleri açıldı"));
        }

        // ---------------------------------------------------------------- veri

        public async void RefreshNow(bool manual)
        {
            if (manual) AddEvent("elle", "veri yenilendi");
            await RefreshAsync(manual);
        }

        private async System.Threading.Tasks.Task RefreshAsync(bool manual)
        {
            if (_refreshing) return;
            _refreshing = true;

            SetConnection(manual ? "yenileniyor…" : "veri çekiliyor…", true);
            var watch = Stopwatch.StartNew();

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var stats = await _client.FetchAsync(Settings.ApiBase, Settings.ChannelSlug, Settings.AutoRetry, cts.Token);
                watch.Stop();
                stats.FetchedAt = DateTime.Now;
                _failures = 0;
                ApplyStats(stats, watch.ElapsedMilliseconds);
                SetConnection("bağlı", true);
            }
            catch (Exception ex)
            {
                watch.Stop();
                _failures++;
                if (Latest != null) Latest.Stale = true;
                SetConnection("bağlantı kurulamadı", false);
                if (_failures == 1 || _failures % 10 == 0)
                {
                    Log.Write("veri alınamadı (" + _failures + "): " + ex.Message);
                    AddEvent("hata", ex.Message.Length > 140 ? ex.Message.Substring(0, 140) : ex.Message);
                }
                if (_failures == 3 && Settings.NotifyRecover)
                {
                    Notify("bağlantı sorunu", "kanal verisi alınamıyor, tekrar deniyorum", NotifySoundKind.Blip);
                }
            }
            finally
            {
                _refreshing = false;
                UpdateUi(watch.ElapsedMilliseconds);
            }
        }

        private void ApplyStats(ChannelStats stats, long latency)
        {
            var previous = Latest;
            Latest = stats;

            if (previous == null)
            {
                AddEvent("kanal", stats.IsLive
                    ? "yayın açık · " + stats.ViewerCount + " izleyici"
                    : "yayın kapalı · " + stats.FollowersCount + " takipçi");
                if (stats.IsLive) _liveSince = DateTime.Now;
            }
            else
            {
                if (!previous.IsLive && stats.IsLive)
                {
                    _liveSince = DateTime.Now;
                    AddEvent("yayın", "yayın başladı");
                    if (Settings.NotifyLive) Notify("yayın başladı", "mor tilki canlıda", NotifySoundKind.Pulse);
                }
                else if (previous.IsLive && !stats.IsLive)
                {
                    _liveSince = null;
                    AddEvent("yayın", "yayın bitti");
                    if (Settings.NotifyLive) Notify("yayın bitti", "mor tilki yayını kapattı", NotifySoundKind.Blip);
                }

                if (stats.FollowersCount > previous.FollowersCount && previous.FollowersCount > 0)
                {
                    var diff = stats.FollowersCount - previous.FollowersCount;
                    AddEvent("takipçi", "+" + diff + " · toplam " + stats.FollowersCount);
                    if (Settings.NotifyFollower) Notify("yeni takipçi", "+" + diff + " · toplam " + stats.FollowersCount);
                }

                if (stats.IsLive && previous.IsLive && Settings.NotifyViewerSpike &&
                    stats.ViewerCount > 12 && previous.ViewerCount > 0 &&
                    stats.ViewerCount > previous.ViewerCount * 1.6 &&
                    (DateTime.Now - _lastSpikeNotify).TotalMinutes > 10)
                {
                    _lastSpikeNotify = DateTime.Now;
                    AddEvent("izleyici", "sıçrama · " + previous.ViewerCount + " → " + stats.ViewerCount);
                    Notify("izleyici sıçraması", previous.ViewerCount + " → " + stats.ViewerCount);
                }
            }

            History.Add(new Sample
            {
                Time = DateTime.Now,
                Viewers = stats.ViewerCount,
                Followers = stats.FollowersCount,
                Live = stats.IsLive
            }, Settings.HistoryMinutes);

            if (++_sampleCounter % 12 == 0) History.Save();

            UpdateUi(latency);
            StatsUpdated?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateUi(long latency)
        {
            var accent = ThemeManager.Get("Accent");
            var surface2 = ThemeManager.Get("Surface2");
            var textDim = ThemeManager.Get("TextDim");
            var accentInk = ThemeManager.Get("AccentInk");

            var stats = Latest;
            if (stats == null)
            {
                TitleChannel.Text = "· " + Settings.ChannelSlug;
                ChannelMeta.Text = "veri yok";
                SideLive.Background = surface2;
                SideLiveDot.Fill = textDim;
                SideLiveText.Text = "bağlantı yok";
                SideLiveText.Foreground = ThemeManager.Get("TextDim");
                HeaderState.Background = surface2;
                HeaderDot.Fill = textDim;
                HeaderStateText.Text = _failures > 0 ? "BAĞLANTI YOK" : "BAĞLANIYOR";
                _livePage.Show(null, StatusText.Text, _failures == 0, latency, "son değişiklik: " + DateTime.Now.ToString("HH:mm:ss"));
                _studioPage.Show(null);
                return;
            }

            ChannelMeta.Text = stats.FollowersCount.ToString("N0") + " takipçi";
            VerifiedChip.Visibility = stats.Verified ? Visibility.Visible : Visibility.Collapsed;

            if (!string.IsNullOrEmpty(stats.ProfilePic)) LoadAvatar(stats.ProfilePic);

            SideLive.Background = stats.IsLive ? accent : surface2;
            SideLiveDot.Fill = stats.IsLive ? accentInk : textDim;
            SideLiveText.Text = stats.IsLive ? "canlı · " + stats.ViewerCount + " izleyici" : "yayın kapalı";
            SideLiveText.Foreground = stats.IsLive ? accentInk : ThemeManager.Get("TextDim");

            HeaderState.Background = stats.IsLive ? accent : surface2;
            HeaderDot.Fill = stats.IsLive ? accentInk : textDim;
            HeaderStateText.Text = stats.Stale ? "ESKİ VERİ" : stats.IsLive ? "CANLI" : "KAPALI";
            HeaderStateText.Foreground = stats.IsLive ? accentInk : ThemeManager.Get("TextDim");

            StatusRight.Text = "son veri " + stats.FetchedAt.ToString("HH:mm:ss") + " · " + Settings.PollSeconds + "sn";

            var detail = "kaynak: " + (stats.Stale ? "önbellek" : string.IsNullOrEmpty(LastSource) ? "—" : LastSource) +
                         " · aralık: " + Settings.PollSeconds + "sn" +
                         (stats.Stale ? " · son iyi veri" : "");

            _livePage.Show(stats, stats.Stale ? "eski veri" : "bağlı", !stats.Stale, latency, detail);
            _studioPage.Show(stats);

            if (_tray != null)
            {
                _tray.Update(
                    "kanal: " + Settings.ChannelSlug,
                    stats.IsLive,
                    stats.ViewerCount,
                    stats.IsLive
                        ? "KickForge · canlı · " + stats.ViewerCount + " izleyici"
                        : "KickForge · yayın kapalı");
            }
        }

        private string? _avatarUrl;

        private void LoadAvatar(string url)
        {
            if (url == _avatarUrl) return;
            _avatarUrl = url;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(url, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
                AvatarBrush.ImageSource = bitmap;
            }
            catch (Exception ex)
            {
                Log.Write("profil resmi yüklenemedi: " + ex.Message);
            }
        }

        private void SetConnection(string text, bool ok)
        {
            StatusText.Text = text;
            StatusDot.Fill = ok ? ThemeManager.Get("Okay") : ThemeManager.Get("Danger");
        }

        public void AddEvent(string kind, string text)
        {
            _events.Insert(0, new FeedEvent { Time = DateTime.Now, Kind = kind, Text = text });
            if (_events.Count > 200) _events.RemoveRange(150, _events.Count - 150);
            FeedAdded?.Invoke(this, EventArgs.Empty);
        }

        public void ClearEvents()
        {
            _events.Clear();
            FeedAdded?.Invoke(this, EventArgs.Empty);
            AddEvent("geçmiş", "olay akışı temizlendi");
        }

        // ---------------------------------------------------------------- bildirim

        private bool InQuietHours()
        {
            if (!Settings.QuietHours) return false;
            var hour = DateTime.Now.Hour;
            var start = Settings.QuietStart;
            var end = Settings.QuietEnd;
            return start <= end ? hour >= start && hour < end : hour >= start || hour < end;
        }

        private void Notify(string title, string message, NotifySoundKind? sound = null)
        {
            AddEvent("bildirim", title + " · " + message);
            if (InQuietHours()) return;

            _tray?.Notify(title, message);
            if (_soundMuted) return;

            var kind = sound ?? MapSound(Settings.Sound);
            Audio.PlayNotification(kind, Settings.AudioDeviceId, Settings.SoundVolume);
        }

        public void TestNotificationSound()
        {
            Audio.PlayNotification(MapSound(Settings.Sound), Settings.AudioDeviceId, Settings.SoundVolume);
            AddEvent("ses", "bildirim sesi denendi");
        }

        public static NotifySoundKind MapSound(NotifySound sound)
        {
            switch (sound)
            {
                case NotifySound.Pulse: return NotifySoundKind.Pulse;
                case NotifySound.Blip: return NotifySoundKind.Blip;
                case NotifySound.Chime: return NotifySoundKind.Chime;
                default: return NotifySoundKind.None;
            }
        }

        // ---------------------------------------------------------------- pencere

        private void RestoreWindowPlacement()
        {
            if (!Settings.RememberBounds) return;
            if (Settings.WindowLeft is not double savedLeft || Settings.WindowTop is not double savedTop) return;

            var width = Math.Max(MinWidth, Settings.WindowWidth);
            var height = Math.Max(MinHeight, Settings.WindowHeight);
            var left = savedLeft;
            var top = savedTop;

            // ekran dışında kalmasın
            var virtualLeft = SystemParameters.VirtualScreenLeft;
            var virtualTop = SystemParameters.VirtualScreenTop;
            var virtualWidth = SystemParameters.VirtualScreenWidth;
            var virtualHeight = SystemParameters.VirtualScreenHeight;

            if (left < virtualLeft - 40 || left > virtualLeft + virtualWidth - 120) left = (virtualWidth - width) / 2 + virtualLeft;
            if (top < virtualTop - 20 || top > virtualTop + virtualHeight - 120) top = (virtualHeight - height) / 2 + virtualTop;

            Width = width;
            Height = height;
            Left = left;
            Top = top;
        }

        private void SaveBounds()
        {
            if (!Settings.RememberBounds) return;
            if (WindowState != WindowState.Normal) return;
            Settings.WindowWidth = Width;
            Settings.WindowHeight = Height;
            Settings.WindowLeft = Left;
            Settings.WindowTop = Top;
        }

        private void OnStateChanged(object? sender, EventArgs e)
        {
            MaxGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_quitting && Settings.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                _livePage.SyncToggles();
                return;
            }

            SaveBounds();
            SaveSettings();
            History.Save();
            _timer.Stop();
            _studioPage.Dispose();
            _tray?.Dispose();
            _hotkeys.Dispose();
            _client.Dispose();
            Audio.Dispose();
            Application.Current.Shutdown();
        }

        public void ExitApp()
        {
            _quitting = true;
            Close();
        }

        public void ShowFromTray()
        {
            Dispatcher.Invoke(() =>
            {
                Show();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
                _livePage.SyncToggles();
            });
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void NavSettings_Click(object sender, RoutedEventArgs e) => NavSettings.IsChecked = true;

        private void OpenSite_Click(object sender, RoutedEventArgs e) => OpenLink(Settings.ApiBase);

        private void OpenGithub_Click(object sender, RoutedEventArgs e) => OpenGithub();

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (PageHost == null || !_pagesReady) return;
            if (NavLive.IsChecked == true) PageHost.Content = _livePage;
            else if (NavStudio.IsChecked == true) PageHost.Content = _studioPage;
            else if (NavHistory.IsChecked == true) PageHost.Content = _historyPage;
            else if (NavSettings.IsChecked == true) PageHost.Content = _settingsPage;
            else if (NavAbout.IsChecked == true) PageHost.Content = _aboutPage;

            // sohbet döngüsü yalnızca bu sayfa açıkken çalışsın
            _studioPage.SetActive(NavStudio.IsChecked == true);
        }

        // ---------------------------------------------------------------- dışa açılan

        public void OpenLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Write("bağlantı açılamadı: " + ex.Message);
            }
        }

        public void ShowStudio() => NavStudio.IsChecked = true;

        public void OpenChannel() => OpenLink("https://kick.com/" + Settings.ChannelSlug);

        public void OpenChat() => OpenLink("https://kick.com/" + Settings.ChannelSlug + "/chat");

        public void OpenGithub() => OpenLink("https://github.com/OxygenForge-code/KickForge-Mor_tilki");

        public void OpenLogFile()
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.Folder);
                if (!File.Exists(Log.FilePath)) File.WriteAllText(Log.FilePath, "");
                Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Write("günlük açılamadı: " + ex.Message);
            }
        }

        public void OpenDataFolder()
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.Folder);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + SettingsStore.Folder + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Write("klasör açılamadı: " + ex.Message);
            }
        }

        public void ResetSettings()
        {
            var fresh = new AppSettings();
            foreach (var property in typeof(AppSettings).GetProperties())
            {
                if (!property.CanRead || !property.CanWrite) continue;
                property.SetValue(Settings, property.GetValue(fresh));
            }

            ApplySettings();
            SaveSettings();
            RestartTimer();
            RestartHotkeys();
            AddEvent("ayarlar", "varsayılana döndürüldü");
        }

        public void ExportSettings()
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.Folder);
                var target = Path.Combine(SettingsStore.Folder, "settings-export.json");
                File.Copy(SettingsStore.FilePath, target, true);
                OpenDataFolder();
                AddEvent("ayarlar", "dışa aktarıldı: " + target);
            }
            catch (Exception ex)
            {
                Log.Write("ayarlar dışa aktarılamadı: " + ex.Message);
            }
        }

        private string FormatDuration()
        {
            if (_liveSince == null) return "—";
            var span = DateTime.Now - _liveSince.Value;
            if (span.TotalHours >= 1) return (int)span.TotalHours + "s " + span.Minutes + "dk";
            return span.Minutes + "dk " + span.Seconds + "sn";
        }
    }
}
