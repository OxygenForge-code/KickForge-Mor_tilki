using System;
using System.Linq;
using System.Threading;
using System.Windows;
using KickForgeDesktop.Settings;

namespace KickForgeDesktop
{
    public partial class App : Application
    {
        private const string ShowEventName = "KickForge.Desktop.Show";

        private Mutex? _singleInstance;
        private EventWaitHandle? _showSignal;
        private System.Threading.Thread? _showListener;

        public new static App Current => (App)Application.Current;

        public AppSettings Settings { get; private set; } = new AppSettings();

        public bool StartedWithWindows { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // iki kopya açılmasın: ikinci tıklama mevcut pencereyi öne getirsin
            _singleInstance = new Mutex(true, "KickForge.Desktop.SingleInstance", out var isFirst);
            if (!isFirst)
            {
                // ikinci tıklama: açık olan kopyaya "pencereyi göster" de
                try
                {
                    EventWaitHandle.OpenExisting(ShowEventName).Set();
                }
                catch
                {
                    // ilk kopya sinyali dinlemiyorsa yapacak bir şey yok
                }
                Shutdown();
                return;
            }

            ListenForShowSignal();

            base.OnStartup(e);

            foreach (var arg in e.Args)
            {
                if (string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase)) StartedWithWindows = true;
            }

            Settings = SettingsStore.Load();
            Log.ToFile = Settings.LogToFile;

            // ilk açılışta dosya oluşsun: kullanıcı neyin nerede olduğunu görsün
            if (!System.IO.File.Exists(SettingsStore.FilePath)) SettingsStore.Save(Settings);
            ThemeManager.Apply(Settings.Theme);

            DispatcherUnhandledException += (_, args) =>
            {
                Log.Write("beklenmeyen hata: " + args.Exception.Message);
                MessageBox.Show("Beklenmeyen bir hata oldu:\n\n" + args.Exception.Message,
                    "KickForge", MessageBoxButton.OK, MessageBoxImage.Warning);
                args.Handled = true;
            };

            // tanılama: pencere açmadan kanal/sohbet zincirini dener (kickforge.exe --diag)
            foreach (var arg in e.Args)
            {
                if (string.Equals(arg, "--diag", StringComparison.OrdinalIgnoreCase))
                {
                    RunDiagnostics(e.Args);
                    return;
                }
            }

            var window = new MainWindow();
            MainWindow = window;
            window.Show();

            if (e.Args.Contains("--studio")) window.ShowStudio();

            // ekranda gerçekten ne göründüğünü ölç (kickforge.exe --check)
            if (e.Args.Contains("--check"))
            {
                window.Dispatcher.InvokeAsync(async () =>
                {
                    // "açılışta küçük başla" açık olsa da kontrol için pencereyi göster
                    await System.Threading.Tasks.Task.Delay(800);
                    window.Show();
                    window.WindowState = WindowState.Normal;
                    window.Topmost = true;
                    window.Activate();
                    await System.Threading.Tasks.Task.Delay(3000);
                    CheckOnScreen(window);
                    window.Close();
                    Shutdown();
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }

            // arayüzü içeriden PNG'ye çiz (kickforge.exe --shot)
            if (e.Args.Contains("--shot"))
            {
                window.Dispatcher.InvokeAsync(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(9000);
                    SaveShot(window);
                    window.Close();
                    Shutdown();
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        /// <summary>pencerenin ekrandaki gerçek görüntüsünü yakalar ve parlaklığını yazar.</summary>
        private static void CheckOnScreen(MainWindow window)
        {
            try
            {
                var topLeft = window.PointToScreen(new Point(0, 0));
                var bottomRight = window.PointToScreen(new Point(window.ActualWidth, window.ActualHeight));
                var width = (int)Math.Max(1, bottomRight.X - topLeft.X);
                var height = (int)Math.Max(1, bottomRight.Y - topLeft.Y);

                Log.Write("kontrol: görünür=" + window.IsVisible + " durum=" + window.WindowState +
                          " boyut=" + width + "x" + height + " konum=" + (int)topLeft.X + "," + (int)topLeft.Y +
                          " dpi=" + window.Dispatcher.Thread.CurrentCulture.Name);

                using var bitmap = new System.Drawing.Bitmap(width, height);
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen((int)topLeft.X, (int)topLeft.Y, 0, 0, new System.Drawing.Size(width, height));
                }

                long total = 0;
                var samples = 0;
                var bright = 0;
                for (var y = 0; y < height; y += 7)
                {
                    for (var x = 0; x < width; x += 7)
                    {
                        var c = bitmap.GetPixel(x, y);
                        var luma = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                        total += luma;
                        samples++;
                        if (luma > 40) bright++;
                    }
                }

                var path = System.IO.Path.Combine(SettingsStore.Folder, "ekran.png");
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                Log.Write("kontrol: ortalama parlaklık=" + (samples == 0 ? 0 : total / samples) +
                          " · aydınlık piksel=%" + (samples == 0 ? 0 : bright * 100 / samples) +
                          " · " + path);
            }
            catch (Exception ex)
            {
                Log.Write("kontrol hatası: " + ex.Message);
            }
        }

        private void ListenForShowSignal()
        {
            try
            {
                _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            }
            catch (Exception ex)
            {
                Log.Write("göster sinyali kurulamadı: " + ex.Message);
                return;
            }

            _showListener = new System.Threading.Thread(() =>
            {
                while (_showSignal.WaitOne())
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (MainWindow is MainWindow window)
                        {
                            window.ShowFromTray();
                            Log.Write("ikinci tıklama: pencere öne getirildi");
                        }
                    }));
                }
            })
            {
                IsBackground = true,
                Name = "kickforge-show-listener"
            };
            _showListener.Start();
        }

        private static void SaveShot(MainWindow window)
        {
            try
            {
                var root = window.Content as FrameworkElement;
                if (root == null) return;
                var width = (int)Math.Max(1, root.ActualWidth);
                var height = (int)Math.Max(1, root.ActualHeight);

                var target = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                target.Render(root);

                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(target));
                var path = System.IO.Path.Combine(SettingsStore.Folder, "arayuz.png");
                using (var stream = System.IO.File.Create(path)) encoder.Save(stream);
                Log.Write("tanılama: arayüz görüntüsü " + path);
            }
            catch (Exception ex)
            {
                Log.Write("tanılama: görüntü alınamadı · " + ex.Message);
            }
        }

        private async void RunDiagnostics(string[] args)
        {
            try
            {
                var settings = SettingsStore.Load();

                // yerel giriş köprüsünü (tarayıcı açmadan) dinlemeye al
                if (args.Contains("--listen"))
                {
                    try
                    {
                        using var listenCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(70));
                        var bridge = await new Services.KickAuthService()
                            .LoginAsync(settings.ApiBase, listenCts.Token, openBrowser: false, forcedState: "teststate");
                        Log.Write("tanılama: yerel köprü " + (bridge ? "token aldı" : "tamamlanmadı"));
                    }
                    catch (Exception ex)
                    {
                        Log.Write("tanılama: yerel köprü hata · " + ex.Message);
                    }
                    Shutdown();
                    return;
                }

                var chat = new Services.ChatService();
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));

                Log.Write("tanılama: kanal " + settings.ChannelSlug);
                var ids = await chat.ResolveIdsAsync(settings.ChannelSlug, cts.Token);
                Log.Write("tanılama: kanal no " + ids.ChannelId + " · yayıncı " + ids.BroadcasterId);

                var messages = await chat.FetchHistoryAsync(ids, cts.Token);
                Log.Write("tanılama: sohbet " + messages.Count + " mesaj");
                foreach (var message in messages.Take(3))
                {
                    Log.Write("tanılama: " + message.Clock + " <" + message.User + "> " +
                              (message.Content.Length > 60 ? message.Content.Substring(0, 60) : message.Content));
                }

                var webview = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
                Log.Write("tanılama: webview2 " + (string.IsNullOrEmpty(webview) ? "yok" : webview));

                // tek dosyaya gömülü yerel yükleyicinin gerçekten çalıştığını görmek için
                var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                    null, System.IO.Path.Combine(SettingsStore.Folder, "webview-check"), null);
                Log.Write("tanılama: webview ortamı hazır · " + environment.BrowserVersionString);

                chat.Dispose();
            }
            catch (Exception ex)
            {
                Log.Write("tanılama hatası: " + ex.Message);
            }
            finally
            {
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                ThemeManager.Apply(Settings.Theme);
            }
            catch
            {
                // kapanışta hata yutulur
            }

            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
