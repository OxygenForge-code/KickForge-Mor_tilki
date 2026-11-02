using System;
using System.Windows;
using System.Windows.Media;

namespace KickForgeDesktop
{
    /// <summary>tema paletleri: dört farklı ruh hali, aynı düzen.</summary>
    public static class ThemeManager
    {
        public class Palette
        {
            public string Bg = "#0B0A10";
            public string Bg2 = "#100E17";
            public string Surface = "#16131F";
            public string Surface2 = "#1D1928";
            public string Surface3 = "#262034";
            public string Line = "#2C2539";
            public string Text = "#F3F1F8";
            public string TextDim = "#9992AD";
            public string Accent = "#C6F24E";
            public string Accent2 = "#7FD8FF";
            public string AccentInk = "#0B0A10";
        }

        public static Palette Current { get; private set; } = For(Settings.AppTheme.ControlRoom);

        public static Palette For(Settings.AppTheme theme)
        {
            switch (theme)
            {
                case Settings.AppTheme.Midnight:
                    return new Palette
                    {
                        Bg = "#07101C",
                        Bg2 = "#0A1421",
                        Surface = "#0F1D2D",
                        Surface2 = "#14263A",
                        Surface3 = "#1B3149",
                        Line = "#20374F",
                        Text = "#EAF4FF",
                        TextDim = "#8FA8C0",
                        Accent = "#63C8FF",
                        Accent2 = "#C6A8FF",
                        AccentInk = "#04111C"
                    };
                case Settings.AppTheme.Sakura:
                    return new Palette
                    {
                        Bg = "#150D12",
                        Bg2 = "#1B1017",
                        Surface = "#221419",
                        Surface2 = "#2C1A21",
                        Surface3 = "#38222B",
                        Line = "#412A33",
                        Text = "#FBEEF2",
                        TextDim = "#BC9AA6",
                        Accent = "#FF9EC4",
                        Accent2 = "#FFD59E",
                        AccentInk = "#1A0D13"
                    };
                case Settings.AppTheme.Mono:
                    return new Palette
                    {
                        Bg = "#0C0C0D",
                        Bg2 = "#111112",
                        Surface = "#171718",
                        Surface2 = "#1F1F21",
                        Surface3 = "#292A2C",
                        Line = "#323335",
                        Text = "#F2F2F3",
                        TextDim = "#95969A",
                        Accent = "#E8E8EA",
                        Accent2 = "#B9BABD",
                        AccentInk = "#0C0C0D"
                    };
                default:
                    return new Palette();
            }
        }

        public static System.Windows.Media.Brush Get(string key)
        {
            var found = Application.Current?.TryFindResource(key);
            return found as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Transparent;
        }

        public static void Apply(Settings.AppTheme theme)
        {
            var palette = For(theme);
            Current = palette;
            var resources = Application.Current.Resources;

            void Set(string key, string hex)
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                resources[key] = new SolidColorBrush(color);
            }

            Set("Bg", palette.Bg);
            Set("Bg2", palette.Bg2);
            Set("Surface", palette.Surface);
            Set("Surface2", palette.Surface2);
            Set("Surface3", palette.Surface3);
            Set("Line", palette.Line);
            Set("Text", palette.Text);
            Set("TextDim", palette.TextDim);
            Set("Accent", palette.Accent);
            Set("Accent2", palette.Accent2);
            Set("AccentInk", palette.AccentInk);

            // ---- liquid glass yuzeyler -----------------------------------
            static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
            static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

            void SetBrush(string key, System.Windows.Media.Brush brush)
            {
                if (brush.CanFreeze) brush.Freeze();
                resources[key] = brush;
            }

            LinearGradientBrush Glass(string topHex, byte topAlpha, string midHex, byte midAlpha,
                                      string endHex, byte endAlpha)
            {
                var brush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0.4, 1)
                };
                brush.GradientStops.Add(new GradientStop(Alpha(Hex(topHex), topAlpha), 0));
                brush.GradientStops.Add(new GradientStop(Alpha(Hex(midHex), midAlpha), 0.55));
                brush.GradientStops.Add(new GradientStop(Alpha(Hex(endHex), endAlpha), 1));
                return brush;
            }

            // kart yuzeyi
            SetBrush("GlassFill", Glass(palette.Surface2, 0xDC, palette.Surface, 0xCC, palette.Surface2, 0xC2));
            // rozet / ikincil yuzey
            SetBrush("GlassFill2", Glass(palette.Surface3, 0xE8, palette.Surface2, 0xD6, palette.Surface3, 0xCC));
            // daha koyu cam (yazı alanları, listeler)
            SetBrush("GlassFill3", Glass(palette.Surface, 0xF2, palette.Surface2, 0xE2, palette.Surface, 0xD8));

            // kenar isigi: ust parlak, alt yansima
            var edge = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(0x42, 255, 255, 255), 0));
            edge.GradientStops.Add(new GradientStop(Alpha(Hex(palette.Line), 0xB8), 0.5));
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(0x20, 255, 255, 255), 1));
            SetBrush("GlassEdge", edge);

            SetBrush("GlassHover", new SolidColorBrush(Color.FromArgb(0x1C, 255, 255, 255)));

            // cam arkasi ortam isigi (tema renginden)
            System.Windows.Media.Brush Ambient(string hex, byte alpha)
            {
                var c = Alpha(Hex(hex), alpha);
                var brush = new RadialGradientBrush
                {
                    MappingMode = BrushMappingMode.RelativeToBoundingBox,
                    Center = new Point(0.5, 0.5),
                    GradientOrigin = new Point(0.5, 0.5),
                    RadiusX = 0.5,
                    RadiusY = 0.5
                };
                brush.GradientStops.Add(new GradientStop(c, 0));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha / 2), c.R, c.G, c.B), 0.6));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
                return brush;
            }

            SetBrush("AmbientA", Ambient(palette.Accent, 0x2A));
            SetBrush("AmbientB", Ambient(palette.Accent2, 0x22));

            // modern ana düğme: accent'ten koyu tonuna akan gradyan
            var primaryFill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.8, 1) };
            primaryFill.GradientStops.Add(new GradientStop(Alpha(Hex(palette.Accent), 0xFF), 0));
            primaryFill.GradientStops.Add(new GradientStop(Alpha(Hex(palette.Accent), 0xC2), 1));
            SetBrush("PrimaryFill", primaryFill);

            // ince kaydırma çubuğu burnu (metin renginin duran %22'si)
            SetBrush("ScrollThumb", new SolidColorBrush(Alpha(Hex(palette.Text), 0x38)));

            // gerçek cam: dwm arkası bulanıklık açıkken pencere zemininin saydam tabanı
            SetBrush("WindowBase", new SolidColorBrush(Alpha(Hex(palette.Bg), 0xB4)));
        }
    }
}
