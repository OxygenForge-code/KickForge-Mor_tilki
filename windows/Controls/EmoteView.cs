using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KickForgeDesktop.Settings;

namespace KickForgeDesktop.Controls
{
    /// <summary>
    /// kick ifadesi: sohbette [emote:id:ad] yerine gerçek görsel. gif ise
    /// kendi karelerimizle oynatıyoruz (tek zamanlayıcı, tüm ifadeler aynı
    /// döngüde). indirilemezse adı yazıyla kalıyor, satır bozulmuyor.
    /// </summary>
    public sealed class EmoteView : Grid
    {
        private static readonly HttpClient Http = CreateHttp();
        private static readonly ConcurrentDictionary<string, Task<EmoteAsset?>> Library =
            new ConcurrentDictionary<string, Task<EmoteAsset?>>();

        private static readonly List<EmoteView> Playing = new List<EmoteView>();
        private static DispatcherTimer? _ticker;

        private readonly Image _image;
        private BitmapSource[]? _frames;
        private int[]? _delays;
        private int _frame;
        private double _elapsed;

        public EmoteView(string id, string name, double size = 19)
        {
            VerticalAlignment = VerticalAlignment.Center;
            Margin = new Thickness(1, 0, 1, 0);
            ToolTip = name;

            var label = new TextBlock
            {
                Text = name,
                FontSize = 9.5,
                Foreground = ThemeManager.Get("TextDim"),
                VerticalAlignment = VerticalAlignment.Center
            };

            _image = new Image
            {
                Width = size,
                Height = size,
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true
            };

            Children.Add(label);
            Children.Add(_image);

            Loaded += (_, _) => { if (IsAnimated) Start(); };
            Unloaded += (_, _) => Stop();

            _ = LoadAsync(id);
        }

        private bool IsAnimated => _frames != null && _frames.Length > 1;

        private async Task LoadAsync(string id)
        {
            var asset = await Asset(id).ConfigureAwait(true);
            if (asset == null || asset.Frames.Length == 0) return;

            _frames = asset.Frames;
            _delays = asset.Delays;
            _frame = 0;
            _image.Source = _frames[0];
            _image.Visibility = Visibility.Visible;

            if (Children.Count > 1 && Children[0] is TextBlock label) label.Visibility = Visibility.Collapsed;

            if (IsAnimated && IsLoaded) Start();
        }

        // ------------------------------------------------------------------ döngü

        private void Start()
        {
            if (Playing.Contains(this)) return;

            Playing.Add(this);
            if (_ticker == null)
            {
                _ticker = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(40)
                };
                _ticker.Tick += (_, _) => Step();
            }

            _ticker.Start();
        }

        private void Stop()
        {
            Playing.Remove(this);
            if (Playing.Count == 0) _ticker?.Stop();
        }

        private static void Step()
        {
            for (var i = Playing.Count - 1; i >= 0; i--)
            {
                Playing[i].Advance();
            }
        }

        private void Advance()
        {
            if (_frames == null || _delays == null || _frames.Length < 2) return;

            _elapsed += 40;
            var delay = _delays[_frame];
            if (delay <= 0) delay = 80;
            if (_elapsed < delay) return;

            _elapsed = 0;
            _frame = (_frame + 1) % _frames.Length;
            _image.Source = _frames[_frame];
        }

        // ------------------------------------------------------------------ indirme

        private static Task<EmoteAsset?> Asset(string id) =>
            Library.GetOrAdd(id, key => FetchAsync(key));

        private static async Task<EmoteAsset?> FetchAsync(string id)
        {
            try
            {
                var bytes = await Http
                    .GetByteArrayAsync("https://files.kick.com/emotes/" + id + "/fullsize")
                    .ConfigureAwait(false);

                if (bytes.Length == 0) return null;

                using var stream = new MemoryStream(bytes);
                var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);

                var frames = new List<BitmapSource>();
                var delays = new List<int>();
                foreach (var frame in decoder.Frames)
                {
                    frames.Add(Freeze(frame));
                    delays.Add(DelayOf(frame));
                }

                return new EmoteAsset(frames.ToArray(), delays.ToArray());
            }
            catch
            {
                // bozuk/başka biçim: yazıyla kalır
                return null;
            }
        }

        private static BitmapSource Freeze(BitmapSource source)
        {
            try
            {
                if (source.CanFreeze) source.Freeze();
            }
            catch
            {
                // dondurulamazsa olduğu gibi kullanılır
            }

            return source;
        }

        /// <summary>gif kare gecikmesi: 1/100 saniye.</summary>
        private static int DelayOf(BitmapFrame frame)
        {
            try
            {
                if (frame.Metadata is BitmapMetadata metadata)
                {
                    var raw = metadata.GetQuery("/grctlext/Delay");
                    if (raw is ushort us && us > 0) return us * 10;
                    if (raw is byte b && b > 0) return b * 10;
                }
            }
            catch
            {
                // bilgi yoksa varsayılan
            }

            return 100;
        }

        private static HttpClient CreateHttp()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };

            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
            return client;
        }

        private sealed class EmoteAsset
        {
            public EmoteAsset(BitmapSource[] frames, int[] delays)
            {
                Frames = frames;
                Delays = delays;
            }

            public BitmapSource[] Frames { get; }
            public int[] Delays { get; }
        }
    }
}
