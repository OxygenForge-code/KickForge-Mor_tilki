using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KickForgeDesktop.Controls
{
    /// <summary>
    /// animasyonlar tek yerde: aynı süre ve aynı yumuşatma kullanılsın diye.
    /// hepsi FillBehavior.Stop ile çalışıyor, yani biten animasyon nesneyi
    /// bırakmıyor; değerler önceden son hâline kuruluyor.
    /// </summary>
    public static class Motion
    {
        public const int Quick = 120;
        public const int Normal = 220;
        public const int Lazy = 420;

        private static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
        private static readonly IEasingFunction EaseBoth = Freeze(new CubicEase { EasingMode = EasingMode.EaseInOut });
        private static readonly IEasingFunction Overshoot = Freeze(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 });

        private static IEasingFunction Freeze(IEasingFunction easing)
        {
            if (easing is Freezable freezable && freezable.CanFreeze) freezable.Freeze();
            return easing;
        }

        // ------------------------------------------------------------------ dönüşümler

        /// <summary>ölçek + kaydırma taşıyan ortak dönüşüm; yoksa kuruluyor.</summary>
        public static TransformGroup Group(UIElement element)
        {
            if (element.RenderTransform is TransformGroup existing &&
                existing.Children.Count == 2 &&
                existing.Children[0] is ScaleTransform &&
                existing.Children[1] is TranslateTransform)
            {
                return existing;
            }

            var group = new TransformGroup();
            group.Children.Add(new ScaleTransform(1, 1));
            group.Children.Add(new TranslateTransform(0, 0));
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = group;
            return group;
        }

        public static ScaleTransform ScaleOf(UIElement element) => (ScaleTransform)Group(element).Children[0];

        public static TranslateTransform ShiftOf(UIElement element) => (TranslateTransform)Group(element).Children[1];

        // ------------------------------------------------------------------ girişler

        /// <summary>aşağıdan yukarı süzülerek belirir.</summary>
        public static void RiseIn(UIElement element, double distance = 7, int ms = Normal, int delay = 0)
        {
            var shift = ShiftOf(element);
            element.Opacity = 1;
            shift.Y = 0;

            var storyboard = new Storyboard();
            Frame(storyboard, element, UIElement.OpacityProperty, 0, 1, ms, delay, EaseOut);
            Frame(storyboard, shift, TranslateTransform.YProperty, distance, 0, ms, delay, EaseOut);
            storyboard.Begin();
        }

        /// <summary>hafif büyüyerek yerine oturur (rozetler, düğmeler).</summary>
        public static void PopIn(UIElement element, double from = 0.55, int ms = 280, int delay = 0)
        {
            var scale = ScaleOf(element);
            element.Opacity = 1;
            scale.ScaleX = 1;
            scale.ScaleY = 1;

            var storyboard = new Storyboard();
            Frame(storyboard, element, UIElement.OpacityProperty, 0, 1, Math.Min(ms, 160), delay, EaseOut);
            Frame(storyboard, scale, ScaleTransform.ScaleXProperty, from, 1, ms, delay, Overshoot);
            Frame(storyboard, scale, ScaleTransform.ScaleYProperty, from, 1, ms, delay, Overshoot);
            storyboard.Begin();
        }

        /// <summary>soldan sağa kayarak içeri girer (sayfa geçişi).</summary>
        public static void SlideIn(UIElement element, double distance = 14, int ms = Normal, int delay = 0)
        {
            var shift = ShiftOf(element);
            element.Opacity = 1;
            shift.X = 0;

            var storyboard = new Storyboard();
            Frame(storyboard, element, UIElement.OpacityProperty, 0, 1, ms, delay, EaseOut);
            Frame(storyboard, shift, TranslateTransform.XProperty, distance, 0, ms, delay, EaseOut);
            storyboard.Begin();
        }

        // ------------------------------------------------------------------ vurgular

        /// <summary>kısa bir "nabız": dikkat çeker, yerinden etmez.</summary>
        public static void Pulse(UIElement element, double peak = 1.12, int ms = 190)
        {
            var scale = ScaleOf(element);
            scale.ScaleX = 1;
            scale.ScaleY = 1;

            var storyboard = new Storyboard();
            Burst(storyboard, scale, ScaleTransform.ScaleXProperty, peak, ms);
            Burst(storyboard, scale, ScaleTransform.ScaleYProperty, peak, ms);
            storyboard.Begin();
        }

        private static void Burst(Storyboard storyboard, DependencyObject target, DependencyProperty property, double peak, int ms)
        {
            var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(peak,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * 0.35)), EaseOut));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(1,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), EaseBoth));

            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, new PropertyPath(property));
            storyboard.Children.Add(animation);
        }

        /// <summary>rozet üzerine gelince hafifçe büyür.</summary>
        public static void Hover(UIElement element, bool on)
        {
            var scale = ScaleOf(element);
            var storyboard = new Storyboard();
            Frame(storyboard, scale, ScaleTransform.ScaleXProperty, scale.ScaleX, on ? 1.16 : 1, Quick, 0, EaseOut);
            Frame(storyboard, scale, ScaleTransform.ScaleYProperty, scale.ScaleY, on ? 1.16 : 1, Quick, 0, EaseOut);
            storyboard.Begin();
        }

        // ------------------------------------------------------------------ kaydırma

        /// <summary>
        /// sohbet listesini yumuşakça sona kaydırır: anlık zıplama yerine
        /// 220 ms'lik bir yavaşlama eğrisi.
        /// </summary>
        public static void ScrollToEnd(ScrollViewer viewer, int ms = Normal)
        {
            if (viewer == null) return;

            var from = viewer.VerticalOffset;
            var to = viewer.ScrollableHeight;
            var delta = to - from;
            if (Math.Abs(delta) < 1) return;

            // çok uzun atlamalarda (ilk yükleme) animasyon yerine doğrudan git
            if (Math.Abs(delta) > 900 || ms <= 0)
            {
                viewer.ScrollToVerticalOffset(to);
                return;
            }

            var frame = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(15)
            };

            var watch = System.Diagnostics.Stopwatch.StartNew();
            frame.Tick += (_, _) =>
            {
                var t = Math.Min(1, watch.Elapsed.TotalMilliseconds / ms);
                var eased = 1 - Math.Pow(1 - t, 3);
                viewer.ScrollToVerticalOffset(from + delta * eased);
                if (t >= 1) frame.Stop();
            };
            frame.Start();
        }

        // ------------------------------------------------------------------ çekirdek

        private static void Frame(Storyboard storyboard, DependencyObject target, DependencyProperty property,
            double from, double to, int ms, int delay, IEasingFunction? easing)
        {
            if (Math.Abs(from - to) < 0.0001 && delay <= 0) return;

            var animation = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(Math.Max(1, ms))))
            {
                BeginTime = TimeSpan.FromMilliseconds(Math.Max(0, delay)),
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            };

            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, new PropertyPath(property));
            storyboard.Children.Add(animation);
        }
    }
}
