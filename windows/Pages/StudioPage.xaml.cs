using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KickForgeDesktop.Models;
using KickForgeDesktop.Services;
using KickForgeDesktop.Settings;
using Microsoft.Web.WebView2.Core;

namespace KickForgeDesktop.Pages
{
    public partial class StudioPage : UserControl
    {
        private readonly MainWindow _shell;
        private readonly ChatService _chat = new ChatService();
        private readonly DispatcherTimer _chatTimer = new DispatcherTimer();
        private readonly HashSet<string> _seen = new HashSet<string>();
        private ChannelIds? _ids;
        private bool _polling;
        private bool _active;
        private bool _chatLogged;
        private int _chatFails;
        private bool _sending;
        private bool _muted = true;
        private static CoreWebView2Environment? _environment;
        private bool _playerReady;
        private bool _playerStarting;
        private bool _playerFailed;
        private bool _wasLive;
        private bool _chatVisible = true;
        private string _slug = "";

        public StudioPage(MainWindow shell)
        {
            _shell = shell;
            InitializeComponent();
            ChatList.ItemsSource = shell.ChatFeed.Messages;

            _chatTimer.Interval = TimeSpan.FromSeconds(3);
            _chatTimer.Tick += (_, _) => PollChat();

            MuteButton.Click += (_, _) => ToggleMute();
            ReloadButton.Click += (_, _) => ReloadPlayer();
            OpenButton.Click += (_, _) => _shell.OpenChannel();
            ChatToggle.Click += (_, _) => ToggleChat();

            Loaded += (_, _) => OnLoaded();
            Unloaded += (_, _) => _chatTimer.Stop();

            UpdateAuthUi();
        }

        // ---------------------------------------------------------------- oynatıcı

        private async void OnLoaded()
        {
            _slug = _shell.Settings.ChannelSlug;
            await StartPlayerAsync();
            if (_active) PollChat();
        }

        /// <summary>menü bu sayfaya geçtiğinde çağrılır.</summary>
        public void SetActive(bool active)
        {
            _active = active;
            if (active)
            {
                _slug = _shell.Settings.ChannelSlug;
                _chatTimer.Start();
                PollChat();
            }
            else
            {
                _chatTimer.Stop();
            }
        }

        private async System.Threading.Tasks.Task StartPlayerAsync()
        {
            // aynı anda iki kez çağrılırsa webview "farklı ortam" hatası veriyor
            if (_playerReady || _playerStarting) return;
            _playerStarting = true;
            Overlay.Visibility = Visibility.Visible;
            OverlayTitle.Text = "oynatıcı hazırlanıyor…";
            OverlayText.Text = "ilk açılışta birkaç saniye sürebilir";

            try
            {
                var folder = Path.Combine(SettingsStore.Folder, "webview");
                Directory.CreateDirectory(folder);

                if (_environment == null)
                {
                    var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                    _environment = await CoreWebView2Environment.CreateAsync(null, folder, options);
                }
                await Player.EnsureCoreWebView2Async(_environment);

                Player.CoreWebView2.Settings.AreDevToolsEnabled = false;
                Player.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                Player.CoreWebView2.Settings.IsStatusBarEnabled = false;
                Player.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                Player.CoreWebView2.NavigationCompleted += (_, args) =>
                {
                    if (!args.IsSuccess)
                    {
                        ShowPlayerError("sayfa açılamadı (" + args.WebErrorStatus + ")");
                        return;
                    }

                    _playerFailed = false;
                    if (_wasLive)
                    {
                        Overlay.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        ShowOffline();
                    }
                };

                _playerReady = true;
                NavigatePlayer();
            }
            catch (Exception ex)
            {
                ShowPlayerError("Edge WebView2 çalışma zamanı kurulu değil ya da başlatılamadı: " + ex.Message);
                Log.Write("webview başlatılamadı: " + ex.Message);
            }
            finally
            {
                _playerStarting = false;
            }
        }

        private void ShowOffline()
        {
            Overlay.Visibility = Visibility.Visible;
            OverlayTitle.Text = "yayın kapalı";
            OverlayText.Text = "mor tilki şu an yayında değil. yayın açılınca oynatıcı kendiliğinden başlıyor, " +
                               "bu ekranı açık bırakabilirsin.";
        }

        private void ShowPlayerError(string detail)
        {
            _playerFailed = true;
            Overlay.Visibility = Visibility.Visible;
            OverlayTitle.Text = "oynatıcı açılamadı";
            OverlayText.Text = detail;
        }

        private void OverlayOpen_Click(object sender, RoutedEventArgs e) => _shell.OpenChannel();

        private void OverlayRetry_Click(object sender, RoutedEventArgs e)
        {
            _playerFailed = false;
            ReloadPlayer();
        }

        private void NavigatePlayer()
        {
            if (!_playerReady) return;
            Overlay.Visibility = _wasLive ? Visibility.Collapsed : Visibility.Visible;
            var url = "https://player.kick.com/" + Uri.EscapeDataString(_slug) +
                      "?muted=" + (_muted ? "true" : "false") + "&autoplay=true";
            Player.Source = new Uri(url);
            Log.Write("oynatıcı: " + url);
        }

        private void ReloadPlayer()
        {
            if (_playerReady) NavigatePlayer();
            else _ = StartPlayerAsync();
        }

        private async void ToggleMute()
        {
            _muted = !_muted;
            MuteButton.Content = _muted ? "sesi aç" : "sesi kapat";
            if (!_playerReady) return;
            try
            {
                // sayfa içindeki tüm video/frame'lerde sesi ayarla
                await Player.CoreWebView2.ExecuteScriptAsync(
                    "(function(){var on=" + (_muted ? "false" : "true") + ";" +
                    "var m=function(d){try{var v=d.querySelectorAll('video');for(var i=0;i<v.length;i++){v[i].muted=!on;v[i].volume=1;if(on){v[i].play&&v[i].play()}}}catch(e){}" +
                    "var f=d.querySelectorAll('iframe');for(var j=0;j<f.length;j++){try{m(f[j].contentDocument)}catch(e){}}};m(document);})()");
            }
            catch (Exception ex)
            {
                Log.Write("ses değiştirilemedi: " + ex.Message);
            }
        }

        private void ToggleChat()
        {
            _chatVisible = !_chatVisible;
            ChatPanel.Visibility = _chatVisible ? Visibility.Visible : Visibility.Collapsed;
            ChatToggle.Content = _chatVisible ? "sohbeti gizle" : "sohbeti göster";
        }

        // ---------------------------------------------------------------- sohbet

        private async void PollChat()
        {
            if (_polling || !_active || !_chatVisible) return;

            _polling = true;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                _ids ??= await _chat.ResolveIdsAsync(_shell.Settings.ChannelSlug, cts.Token);

                var messages = await _chat.FetchHistoryAsync(_ids, cts.Token);
                var added = 0;
                foreach (var message in messages.OrderBy(m => m.Time))
                {
                    if (!_seen.Add(message.Id)) continue;
                    Append(message);
                    added++;
                }

                ChatState.Text = _ids.ChannelId == 0
                    ? "kanal bulunamadı"
                    : "bağlı · " + _seen.Count + " mesaj" + (added > 0 ? " · +" + added : "");

                if (!_chatLogged)
                {
                    _chatLogged = true;
                    Log.Write("sohbet yanıtı · " + messages.Count + " mesaj · kanal " + _ids.ChannelId);
                }
            }
            catch (Exception ex)
            {
                ChatState.Text = "sohbet alınamadı · " + Trim(ex.Message);
                if (_chatFails++ % 20 == 0) Log.Write("sohbet hatası: " + ex.Message);
            }
            finally
            {
                _polling = false;
            }
        }

        private void Append(ChatMessage message)
        {
            var list = _shell.ChatFeed.Messages;
            var atBottom = true;
            if (list.Count > 0) atBottom = true; // basit davranış: her zaman sona kaydır

            list.Add(message);
            while (list.Count > 300) list.RemoveAt(0);

            if (atBottom) ChatList.ScrollIntoView(list[list.Count - 1]);
        }

        private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

        private async void ChatInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await SendAsync();
            }
        }

        private async System.Threading.Tasks.Task SendAsync()
        {
            if (_sending) return;
            var text = ChatInput.Text.Trim();
            if (text.Length == 0) return;

            if (!_shell.Auth.LoggedIn)
            {
                ChatState.Text = "yazmak için sağ üstten giriş yap";
                return;
            }

            _sending = true;
            SendButton.IsEnabled = false;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                _ids ??= await _chat.ResolveIdsAsync(_shell.Settings.ChannelSlug, cts.Token);
                await _chat.SendAsync(_shell.Settings.ApiBase, _shell.Auth, _ids, text, cts.Token);

                ChatInput.Text = "";
                ChatState.Text = "gönderildi · " + DateTime.Now.ToString("HH:mm:ss");
                _shell.AddEvent("sohbet", "mesaj gönderildi: " + Trim(text, 60));
            }
            catch (Exception ex)
            {
                ChatState.Text = Trim(ex.Message);
                Log.Write("sohbet gönderilemedi: " + ex.Message);
            }
            finally
            {
                _sending = false;
                SendButton.IsEnabled = true;
                UpdateAuthUi();
            }
        }

        private async void Auth_Click(object sender, RoutedEventArgs e)
        {
            if (_shell.Auth.LoggedIn)
            {
                _shell.Auth.Clear();
                UpdateAuthUi();
                ChatState.Text = "çıkış yapıldı";
                return;
            }

            AuthButton.IsEnabled = false;
            AuthButton.Content = "bekleniyor…";
            ChatState.Text = "tarayıcıda kick girişini tamamla…";

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var ok = await _shell.Auth.LoginAsync(_shell.Settings.ApiBase, cts.Token);
                ChatState.Text = ok ? "giriş tamam · yazabilirsin" : "giriş tamamlanmadı";
            }
            catch (Exception ex)
            {
                ChatState.Text = "giriş başarısız · " + Trim(ex.Message);
            }
            finally
            {
                AuthButton.IsEnabled = true;
                UpdateAuthUi();
            }
        }

        private void UpdateAuthUi()
        {
            var loggedIn = _shell.Auth.LoggedIn;
            AuthButton.Content = loggedIn ? "çıkış" : "giriş yap";
            ChatInput.IsEnabled = loggedIn;
            SendButton.IsEnabled = loggedIn;
            ChatInput.ToolTip = loggedIn ? "mesaj yaz, enter ile gönder" : "yazmak için giriş yap";
            if (loggedIn)
            {
                ChatInput.Focus();
            }
        }

        // ---------------------------------------------------------------- durum

        public void Show(ChannelStats? stats)
        {
            if (stats == null)
            {
                LiveChip.Background = ThemeManager.Get("Surface2");
                LiveDot.Fill = ThemeManager.Get("TextDim");
                LiveText.Text = "YAYIN KAPALI";
                ViewerText.Text = "";
                TitleText.Text = "kanal verisi bekleniyor…";
                if (_playerReady && !_playerFailed) ShowOffline();
                return;
            }

            var wasLive = _wasLive;
            _wasLive = stats.IsLive;

            if (stats.IsLive && !wasLive)
            {
                // yayın yeni açıldı: oynatıcıyı tazelemek gerekiyor, yoksa
                // kapalı ekranında kalıyor
                if (_playerReady) NavigatePlayer();
                else _ = StartPlayerAsync();
            }
            else if (!stats.IsLive && wasLive)
            {
                if (_playerReady) ShowOffline();
            }

            var accent = ThemeManager.Get("Accent");
            LiveChip.Background = stats.IsLive ? accent : ThemeManager.Get("Surface2");
            LiveDot.Fill = stats.IsLive ? ThemeManager.Get("AccentInk") : ThemeManager.Get("TextDim");
            LiveText.Text = stats.IsLive ? "CANLI" : "YAYIN KAPALI";
            LiveText.Foreground = stats.IsLive ? ThemeManager.Get("AccentInk") : ThemeManager.Get("TextDim");
            ViewerText.Text = stats.IsLive ? stats.ViewerCount.ToString("N0") + " izleyici" : "";
            TitleText.Text = stats.SessionTitle;

            if (stats.IsLive && _playerReady && !_playerFailed) Overlay.Visibility = Visibility.Collapsed;
            if (!stats.IsLive && !_playerReady) _ = StartPlayerAsync();
        }

        public void Dispose()
        {
            _chatTimer.Stop();
            _chat.Dispose();
        }

        private static string Trim(string text, int max = 90) =>
            text.Length <= max ? text : text.Substring(0, max) + "…";
    }
}
