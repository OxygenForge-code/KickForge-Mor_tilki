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
        }
    }
}
