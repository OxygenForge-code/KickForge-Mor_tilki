using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KickForgeDesktop.Controls;
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
        private ScrollViewer? _chatScroll;
        private bool _chatAtBottom = true;
        private int _pendingNew;
        private int _lastChatCount = -1;
        private Storyboard? _livePulse;

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

            Loaded += (_, _) =>
            {
                _chatScroll ??= FindScroll(ChatList);
                OnLoaded();
            };
            Unloaded += (_, _) => _chatTimer.Stop();

            UpdateAuthUi();
            UpdateChatChrome();
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

            if (_chatVisible) Motion.SlideIn(ChatPanel, -14, Motion.Normal);

            // sütun da kapanmalı, yoksa sohbet gizlenince sağda boşluk kalıyor
            ChatColumn.Width = _chatVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            ChatPanel.Margin = _chatVisible ? new Thickness(10, 0, 0, 0) : new Thickness(0);

            ChatToggle.Content = _chatVisible ? "sohbeti gizle" : "sohbeti göster";
        }

        private static ScrollViewer? FindScroll(DependencyObject? root)
        {
            if (root == null) return null;
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer viewer) return viewer;
                var nested = FindScroll(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private void ChatList_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            _chatScroll ??= FindScroll(ChatList);
            if (_chatScroll == null) return;

            _chatAtBottom = _chatScroll.ScrollableHeight - _chatScroll.VerticalOffset < 14;
            if (_chatAtBottom && _pendingNew > 0)
            {
                _pendingNew = 0;
                JumpButton.Visibility = Visibility.Collapsed;
            }
        }

        private void Jump_Click(object sender, RoutedEventArgs e)
        {
            _pendingNew = 0;
            JumpButton.Visibility = Visibility.Collapsed;
            _chatAtBottom = true;
            ScrollChatToEnd();
        }

        /// <summary>yeni mesaj düğmesi: yerinden süzülerek gelir, sayı değişince nabız atar.</summary>
        private void ShowJumpButton()
        {
            JumpButton.Content = _pendingNew + " yeni mesaj ↓";

            if (JumpButton.Visibility != Visibility.Visible)
            {
                JumpButton.Visibility = Visibility.Visible;
                Motion.RiseIn(JumpButton, 12, Motion.Normal);
            }
            else
            {
                Motion.Pulse(JumpButton, 1.06, 210);
            }
        }

        private void ScrollChatToEnd()
        {
            if (ChatList.Items.Count == 0) return;

            _chatScroll ??= FindScroll(ChatList);

            // ilk dolum / uzun atlama: doğrudan, sonrası yumuşak kaydırma
            if (_chatScroll == null || _chatScroll.ScrollableHeight - _chatScroll.VerticalOffset > 900)
            {
                ChatList.ScrollIntoView(ChatList.Items[ChatList.Items.Count - 1]);
                return;
            }

            Motion.ScrollToEnd(_chatScroll);
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
                    : "bağlı · " + _chatTimer.Interval.TotalSeconds.ToString("0") + "sn'de bir" +
                      (added > 0 ? " · +" + added : "");

                UpdateChatChrome();

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
            var follow = _chatAtBottom;

            list.Add(message);
            while (list.Count > 300) list.RemoveAt(0);

            if (follow)
            {
                // satır yerleşmeden kaydırma hesaplanmıyor: bir sonraki turda kaydır
                Dispatcher.BeginInvoke(new Action(ScrollChatToEnd), DispatcherPriority.Loaded);
            }
            else
            {
                // kullanıcı geçmişe bakıyor: onu aşağı sürüklemiyoruz
                _pendingNew++;
                ShowJumpButton();
            }

            UpdateChatChrome();
        }

        private void UpdateChatChrome()
        {
            var count = _shell.ChatFeed.Messages.Count;
            ChatCount.Text = count == 0 ? "—" : count.ToString();
            ChatEmpty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (count != _lastChatCount)
            {
                if (_lastChatCount >= 0) Motion.Pulse(ChatCountChip, 1.12, 200);
                _lastChatCount = count;
            }
        }

        private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

        private void ChatInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            var empty = ChatInput.Text.Length == 0;
            ChatPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

            // boşken imleç yerine ipucu görünsün: ikisi üst üste binmesin
            ChatInput.CaretBrush = empty ? Brushes.Transparent : ThemeManager.Get("Accent");
        }

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
                Motion.Pulse(SendButton, 1.12, 240);
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
            ChatPlaceholder.Text = loggedIn ? "mesaj yaz…" : "yazmak için giriş yap";
            ChatPlaceholder.Visibility = ChatInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            ChatInput.CaretBrush = ChatInput.Text.Length == 0 ? Brushes.Transparent : ThemeManager.Get("Accent");
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
                StopLivePulse();
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
            var viewers = stats.IsLive ? stats.ViewerCount.ToString("N0") + " izleyici" : "";
            if (ViewerText.Text != viewers && stats.IsLive) Motion.RiseIn(ViewerText, 4, Motion.Quick);
            ViewerText.Text = viewers;
            TitleText.Text = stats.SessionTitle;

            if (stats.IsLive) StartLivePulse();
            else StopLivePulse();

            if (stats.IsLive && _playerReady && !_playerFailed) Overlay.Visibility = Visibility.Collapsed;
            if (!stats.IsLive && !_playerReady) _ = StartPlayerAsync();
        }

        /// <summary>canlı göstergesi: nokta nefes alırken dışa doğru halka yayılıyor.</summary>
        private void StartLivePulse()
        {
            if (_livePulse != null) return;

            var ring = (ScaleTransform)LiveRing.RenderTransform;
            var ease = new SineEase { EasingMode = EasingMode.EaseOut };

            var ringOpacity = new DoubleAnimation(0.7, 0, TimeSpan.FromSeconds(1.7))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease
            };
            var ringX = new DoubleAnimation(0.7, 2.8, TimeSpan.FromSeconds(1.7))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease
            };
            var ringY = new DoubleAnimation(0.7, 2.8, TimeSpan.FromSeconds(1.7))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease
            };
            var breathe = new DoubleAnimation(1, 0.45, TimeSpan.FromSeconds(0.9))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            var storyboard = new Storyboard();

            Storyboard.SetTarget(ringOpacity, LiveRing);
            Storyboard.SetTargetProperty(ringOpacity, new PropertyPath(UIElement.OpacityProperty));

            Storyboard.SetTarget(ringX, ring);
            Storyboard.SetTargetProperty(ringX, new PropertyPath(ScaleTransform.ScaleXProperty));

            Storyboard.SetTarget(ringY, ring);
            Storyboard.SetTargetProperty(ringY, new PropertyPath(ScaleTransform.ScaleYProperty));

            Storyboard.SetTarget(breathe, LiveDot);
            Storyboard.SetTargetProperty(breathe, new PropertyPath(UIElement.OpacityProperty));

            storyboard.Children.Add(ringOpacity);
            storyboard.Children.Add(ringX);
            storyboard.Children.Add(ringY);
            storyboard.Children.Add(breathe);

            storyboard.Begin();
            _livePulse = storyboard;
        }

        private void StopLivePulse()
        {
            _livePulse?.Stop();
            _livePulse = null;

            LiveRing.Opacity = 0;
            LiveDot.Opacity = 1;
            if (LiveRing.RenderTransform is ScaleTransform ring)
            {
                ring.ScaleX = 1;
                ring.ScaleY = 1;
            }
        }

        public void Dispose()
        {
            _chatTimer.Stop();
            StopLivePulse();
            _chat.Dispose();
        }

        private static string Trim(string text, int max = 90) =>
            text.Length <= max ? text : text.Substring(0, max) + "…";
    }
}
