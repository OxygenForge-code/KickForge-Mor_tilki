using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KickForgeDesktop.Models;

namespace KickForgeDesktop.Pages
{
    public partial class LivePage : UserControl
    {
        private readonly MainWindow _shell;
        private bool _syncing;
        private DispatcherTimer? _count;
        private double _shownViewers = -1;

        public LivePage(MainWindow shell)
        {
            _shell = shell;
            InitializeComponent();

            BtnWatch.Click += (_, _) => _shell.ShowStudio();
            BtnChat.Click += (_, _) => _shell.ShowStudio();
            BtnRefresh.Click += (_, _) => _shell.RefreshNow(true);
            SyncToggles();
        }

        public void SyncToggles()
        {
            _syncing = true;
            TopToggle.IsChecked = _shell.Settings.AlwaysOnTop;
            TrayToggle.IsChecked = _shell.Settings.MinimizeToTray;
            _syncing = false;
            TrayChip.Visibility = _shell.Settings.MinimizeToTray && !_shell.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void Show(ChannelStats? stats, string connectionText, bool connectionOk, long latencyMs, string detail)
        {
            BottomRow.Visibility = _shell.Settings.CompactMode ? Visibility.Collapsed : Visibility.Visible;
            SparkCard.Visibility = _shell.Settings.ShowSparkline ? Visibility.Visible : Visibility.Collapsed;
            TrayChip.Visibility = _shell.Settings.MinimizeToTray && !_shell.IsVisible ? Visibility.Visible : Visibility.Collapsed;

            var accent = ThemeManager.Get("Accent");
            var accentInk = ThemeManager.Get("AccentInk");
            var surface2 = ThemeManager.Get("Surface2");
            var textDim = ThemeManager.Get("TextDim");

            // bağlantı satırı
            ConnText.Text = connectionText;
            ConnDetail.Text = detail;
            ConnDot.Fill = connectionOk ? ThemeManager.Get("Okay") : ThemeManager.Get("Danger");

            if (stats == null)
            {
                Tally.Background = surface2;
                Tally.BorderBrush = ThemeManager.Get("Line");
                TallyDot.Fill = textDim;
                TallyText.Text = "OFF AIR";
                TallyText.Foreground = ThemeManager.Get("TextDim");
                ViewerText.Text = "—";
                _shownViewers = -1;
                ViewerLabel.Text = "izleyici";
                TitleText.Text = "kanal verisi bekleniyor…";
                StaleBar.Visibility = Visibility.Visible;
                StaleText.Text = "kanal verisi gelmedi";
                StaleChip.Visibility = Visibility.Collapsed;
                FollowerText.Text = "—";
                UptimeText.Text = "—";
                SourceText.Text = "—";
                LatencyText.Text = latencyMs > 0 ? latencyMs + " ms" : "—";
                ChannelIdText.Text = "—";
                UpdatedText.Text = "—";
                return;
            }

            var live = stats.IsLive;
            Tally.Background = live ? accent : surface2;
            Tally.BorderBrush = live ? accent : ThemeManager.Get("Line");
            TallyDot.Fill = live ? accentInk : textDim;
            TallyText.Text = live ? "ON AIR" : "OFF AIR";
            TallyText.Foreground = live ? accentInk : ThemeManager.Get("TextDim");

            CountViewers(live ? stats.ViewerCount : 0);
            ViewerLabel.Text = live ? "izleyici" : "yayın kapalı";
            TitleText.Text = string.IsNullOrWhiteSpace(stats.SessionTitle)
                ? (live ? "başlıksız yayın" : "tilki şu an uyuyor.")
                : stats.SessionTitle;

            FollowerText.Text = stats.FollowersCount.ToString("N0");
            UptimeText.Text = _shell.LiveDurationText;
            SourceText.Text = stats.Stale ? "önbellek" : (_shell.LastSource == "kick" ? "kick.com" : "site");
            LatencyText.Text = latencyMs > 0 ? latencyMs + " ms" : "—";
            ChannelIdText.Text = stats.BroadcasterId?.ToString() ?? "—";
            UpdatedText.Text = stats.FetchedAt.ToString("HH:mm:ss");

            StaleChip.Visibility = stats.Stale ? Visibility.Visible : Visibility.Collapsed;
            StaleBar.Visibility = stats.Stale ? Visibility.Visible : Visibility.Collapsed;
            if (stats.Stale)
            {
                StaleText.Text = "son iyi veri gösteriliyor";
            }

            DrawSpark();
        }

        /// <summary>izleyici sayısı zıplamasın: eski değerden yenisine sayarak geçsin.</summary>
        private void CountViewers(int value)
        {
            _count?.Stop();

            var from = _shownViewers < 0 ? 0 : _shownViewers;
            _shownViewers = value;

            if (!IsLoaded || value == (int)from)
            {
                ViewerText.Text = value.ToString("N0");
                return;
            }

            var watch = Stopwatch.StartNew();
            var timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };

            timer.Tick += (_, _) =>
            {
                var t = Math.Min(1, watch.Elapsed.TotalMilliseconds / 420.0);
                var eased = 1 - Math.Pow(1 - t, 3);
                ViewerText.Text = ((int)Math.Round(from + (value - from) * eased)).ToString("N0");

                if (t < 1) return;
                ViewerText.Text = value.ToString("N0");
                timer.Stop();
            };

            _count = timer;
            timer.Start();
        }

        private void DrawSpark()
        {
            var minutes = _shell.Settings.HistoryMinutes;
            var samples = _shell.History.Window(Math.Min(minutes, 180)).ToList();
            Spark.Values = samples.Select(s => (double)s.Viewers).ToList();
            Spark.Visibility = _shell.Settings.ShowSparkline ? Visibility.Visible : Visibility.Collapsed;

            var window = minutes >= 60 ? (minutes / 60) + " saat" : minutes + " dk";
            SparkSummary.Text = samples.Count == 0
                ? "veri toplanıyor…"
                : "son " + window + " · " + samples.Count + " örnek";

            PeakText.Text = _shell.History.PeakViewers(minutes).ToString("N0");
            AvgText.Text = _shell.History.AverageViewers(minutes).ToString("N0");
            SampleText.Text = samples.Count.ToString();
        }

        private void Retry_Click(object sender, RoutedEventArgs e) => _shell.RefreshNow(true);

        private void OpenSite_Click(object sender, RoutedEventArgs e) => _shell.OpenLink(_shell.Settings.ApiBase);

        private void OpenLog_Click(object sender, RoutedEventArgs e) => _shell.OpenLogFile();

        private void TestSound_Click(object sender, RoutedEventArgs e) => _shell.TestNotificationSound();

        private void TopToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            _shell.SetAlwaysOnTop(TopToggle.IsChecked == true, true);
        }

        private void TrayToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            _shell.SetTrayMode(TrayToggle.IsChecked == true);
        }
    }
}
