using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace KickForgeDesktop.Controls
{
    /// <summary>
    /// izleyici eğrisi: kendi çizdiğimiz basit grafik, hazır grafik kütüphanesi yok.
    /// </summary>
    public class Sparkline : FrameworkElement
    {
        public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
            nameof(Values), typeof(IEnumerable<double>), typeof(Sparkline),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
            nameof(LineBrush), typeof(Brush), typeof(Sparkline),
            new FrameworkPropertyMetadata(Brushes.Lime, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
            nameof(FillBrush), typeof(Brush), typeof(Sparkline),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LineThicknessProperty = DependencyProperty.Register(
            nameof(LineThickness), typeof(double), typeof(Sparkline),
            new FrameworkPropertyMetadata(1.8, FrameworkPropertyMetadataOptions.AffectsRender));

        public IEnumerable<double>? Values
        {
            get => (IEnumerable<double>?)GetValue(ValuesProperty);
            set => SetValue(ValuesProperty, value);
        }

        public Brush LineBrush
        {
            get => (Brush)GetValue(LineBrushProperty);
            set => SetValue(LineBrushProperty, value);
        }

        public Brush? FillBrush
        {
            get => (Brush?)GetValue(FillBrushProperty);
            set => SetValue(FillBrushProperty, value);
        }

        public double LineThickness
        {
            get => (double)GetValue(LineThicknessProperty);
            set => SetValue(LineThicknessProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var width = ActualWidth;
            var height = ActualHeight;
            if (width <= 2 || height <= 2) return;

            var points = (Values ?? Enumerable.Empty<double>()).Where(v => !double.IsNaN(v)).ToList();

            // hafif ızgara: sıfır çizgileri yerine tek bir taban çizgisi
            var basePen = new Pen(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), 1) { DashStyle = DashStyles.Dash };
            basePen.Freeze();
            dc.DrawLine(basePen, new Point(0, height - 0.5), new Point(width, height - 0.5));

            if (points.Count < 2)
            {
                var text = new FormattedText("veri toplanıyor…", System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11,
                    new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(text, new Point(4, height / 2 - 8));
                return;
            }

            var max = points.Max();
            var min = points.Min();
            if (Math.Abs(max - min) < 0.0001)
            {
                max = min + 1;
            }
            else
            {
                var padding = (max - min) * 0.12;
                max += padding;
                min = Math.Max(0, min - padding);
            }

            var stepX = width / (points.Count - 1);
            var usable = Math.Max(1, height - 4);

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                var first = new Point(0, height - 2 - (points[0] - min) / (max - min) * usable);
                ctx.BeginFigure(new Point(0, height), true, true);
                ctx.LineTo(first, false, false);
                for (var i = 1; i < points.Count; i++)
                {
                    var x = i * stepX;
                    var y = height - 2 - (points[i] - min) / (max - min) * usable;
                    ctx.LineTo(new Point(x, y), true, false);
                }
                ctx.LineTo(new Point(width, height), true, false);
            }
            geometry.Freeze();

            if (FillBrush != null)
            {
                dc.DrawGeometry(FillBrush, null, geometry);
            }

            var lineGeometry = new StreamGeometry();
            using (var ctx = lineGeometry.Open())
            {
                ctx.BeginFigure(new Point(0, height - 2 - (points[0] - min) / (max - min) * usable), false, false);
                for (var i = 1; i < points.Count; i++)
                {
                    var x = i * stepX;
                    var y = height - 2 - (points[i] - min) / (max - min) * usable;
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
            lineGeometry.Freeze();

            var pen = new Pen(LineBrush, LineThickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, lineGeometry);

            // son nokta
            var lastX = (points.Count - 1) * stepX;
            var lastY = height - 2 - (points[^1] - min) / (max - min) * usable;
            var dot = new SolidColorBrush(GetAccentColor(LineBrush));
            dot.Freeze();
            dc.DrawEllipse(dot, null, new Point(lastX, lastY), 2.6, 2.6);
        }

        private static Color GetAccentColor(Brush brush)
        {
            return brush is SolidColorBrush solid ? solid.Color : Colors.Lime;
        }
    }
}
