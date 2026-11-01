using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace KickForgeDesktop.Controls
{
    /// <summary>
    /// material design ikonları. path verisi dosyaya gömülü: çalışırken ne ağ
    /// ne de font gerekiyor, tek renkli vektör olarak istenen boyutta çiziliyor.
    /// </summary>
    public static class Mdi
    {
        public const string Broadcaster = "broadcaster";
        public const string Moderator = "moderator";
        public const string Staff = "staff";
        public const string Vip = "vip";
        public const string Founder = "founder";
        public const string Subscriber = "subscriber";
        public const string Gifter = "gifter";
        public const string Og = "og";
        public const string Verified = "verified";
        public const string Level = "level";
        public const string Bot = "bot";

        private static readonly Dictionary<string, string> Sources =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // yayıncı · mdi-broadcast
            ["broadcaster"] =
                "M12 10C10.9 10 10 10.9 10 12S10.9 14 12 14 14 13.1 14 12 13.1 10 12 10M18 12C18 8.7 15.3 6 12 6S6 8.7 6 " +
                "12C6 14.2 7.2 16.1 9 17.2L10 15.5C8.8 14.8 8 13.5 8 12.1C8 9.9 9.8 8.1 12 8.1S16 9.9 16 12.1C16 13.6 15.2 " +
                "14.9 14 15.5L15 17.2C16.8 16.2 18 14.2 18 12M12 2C6.5 2 2 6.5 2 12C2 15.7 4 18.9 7 20.6L8 18.9C5.6 17.5 4 " +
                "14.9 4 12C4 7.6 7.6 4 12 4S20 7.6 20 12C20 15 18.4 17.5 16 18.9L17 20.6C20 18.9 22 15.7 22 12C22 6.5 17.5 2 " +
                "12 2Z",
            // moderatör · mdi-shield-crown
            ["moderator"] =
                "M12 1L21 5V11C21 16.55 17.16 21.74 12 23C6.84 21.74 3 16.55 3 11V5L12 1M16 14H8V15.5C8 15.77 8.19 15.96 " +
                "8.47 16L8.57 16H15.43C15.74 16 15.95 15.84 16 15.59L16 15.5V14M17 8L17 8L14.33 10.67L12 8.34L9.67 10.67L7 " +
                "8L7 8L8 13H16L17 8Z",
            // kick ekibi · mdi-account-star
            ["staff"] =
                "M15,14C12.33,14 7,15.33 7,18V20H23V18C23,15.33 17.67,14 15,14M15,12A4,4 0 0,0 19,8A4,4 0 0,0 15,4A4,4 0 0,0 " +
                "11,8A4,4 0 0,0 15,12M5,13.28L7.45,14.77L6.8,11.96L9,10.08L6.11,9.83L5,7.19L3.87,9.83L1,10.08L3.18,11.96L2.5,1" +
                "4.77L5,13.28Z",
            // vip · mdi-crown
            ["vip"] =
                "M5 16L3 5L8.5 10L12 4L15.5 10L21 5L19 16H5M19 19C19 19.6 18.6 20 18 20H6C5.4 20 5 19.6 5 19V18H19V19Z",
            // kurucu · mdi-diamond-stone
            ["founder"] =
                "M16,9H19L14,16M10,9H14L12,17M5,9H8L10,16M15,4H17L19,7H16M11,4H13L14,7H10M7,4H9L8,7H5M6,2L2,8L12,22L22,8L18,2H" +
                "6Z",
            // abone · mdi-heart
            ["subscriber"] =
                "M12,21.35L10.55,20.03C5.4,15.36 2,12.27 2,8.5C2,5.41 4.42,3 7.5,3C9.24,3 10.91,3.81 12,5.08C13.09,3.81 " +
                "14.76,3 16.5,3C19.58,3 22,5.41 22,8.5C22,12.27 18.6,15.36 13.45,20.03L12,21.35Z",
            // hediye · mdi-gift
            ["gifter"] =
                "M9.06,1.93C7.17,1.92 5.33,3.74 6.17,6H3A2,2 0 0,0 1,8V10A1,1 0 0,0 2,11H11V8H13V11H22A1,1 0 0,0 23,10V8A2,2 " +
                "0 0,0 21,6H17.83C19,2.73 14.6,0.42 12.57,3.24L12,4L11.43,3.22C10.8,2.33 9.93,1.94 9.06,1.93M9,4C9.89,4 " +
                "10.34,5.08 9.71,5.71C9.08,6.34 8,5.89 8,5A1,1 0 0,1 9,4M15,4C15.89,4 16.34,5.08 15.71,5.71C15.08,6.34 " +
                "14,5.89 14,5A1,1 0 0,1 15,4M2,12V20A2,2 0 0,0 4,22H20A2,2 0 0,0 22,20V12H13V20H11V12H2Z",
            // og · mdi-history
            ["og"] =
                "M13.5,8H12V13L16.28,15.54L17,14.33L13.5,12.25V8M13,3A9,9 0 0,0 4,12H1L4.96,16.03L9,12H6A7,7 0 0,1 13,5A7,7 " +
                "0 0,1 20,12A7,7 0 0,1 13,19C11.07,19 9.32,18.21 8.06,16.94L6.64,18.36C8.27,20 10.5,21 13,21A9,9 0 0,0 " +
                "22,12A9,9 0 0,0 13,3",
            // onaylı · mdi-check-decagram
            ["verified"] =
                "M23,12L20.56,9.22L20.9,5.54L17.29,4.72L15.4,1.54L12,3L8.6,1.54L6.71,4.72L3.1,5.53L3.44,9.21L1,12L3.44,14.78L3" +
                ".1,18.47L6.71,19.29L8.6,22.47L12,21L15.4,22.46L17.29,19.28L20.9,18.46L20.56,14.78L23,12M10,17L6,13L7.41,11.59" +
                "L10,14.17L16.59,7.58L18,9L10,17Z",
            // seviye · mdi-star-four-points
            ["level"] =
                "M12,1L9,9L1,12L9,15L12,23L15,15L23,12L15,9L12,1Z",
            // bot · mdi-robot-outline
            ["bot"] =
                "M17.5 15.5C17.5 16.61 16.61 17.5 15.5 17.5S13.5 16.61 13.5 15.5 14.4 13.5 15.5 13.5 17.5 14.4 17.5 15.5M8.5 " +
                "13.5C7.4 13.5 6.5 14.4 6.5 15.5S7.4 17.5 8.5 17.5 10.5 16.61 10.5 15.5 9.61 13.5 8.5 13.5M23 15V18C23 18.55 " +
                "22.55 19 22 19H21V20C21 21.11 20.11 22 19 22H5C3.9 22 3 21.11 3 20V19H2C1.45 19 1 18.55 1 18V15C1 14.45 " +
                "1.45 14 2 14H3C3 10.13 6.13 7 10 7H11V5.73C10.4 5.39 10 4.74 10 4C10 2.9 10.9 2 12 2S14 2.9 14 4C14 4.74 " +
                "13.6 5.39 13 5.73V7H14C17.87 7 21 10.13 21 14H22C22.55 14 23 14.45 23 15M21 16H19V14C19 11.24 16.76 9 14 " +
                "9H10C7.24 9 5 11.24 5 14V16H3V17H5V20H19V17H21V16Z",
        };

        private static readonly Dictionary<string, Geometry> Cache =
            new Dictionary<string, Geometry>(StringComparer.OrdinalIgnoreCase);

        public static bool Has(string key) => Sources.ContainsKey(key);

        /// <summary>paylaşılan geometri: her rozet için yeniden ayrıştırılmıyor.</summary>
        public static Geometry? Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached;
                if (!Sources.TryGetValue(key, out var data)) return null;

                try
                {
                    var geometry = Geometry.Parse(data);
                    geometry.Freeze();
                    Cache[key] = geometry;
                    return geometry;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>tek renkli ikon; 24x24 ızgarasından istenen boyuta ölçekleniyor.</summary>
        public static System.Windows.Shapes.Path Shape(string key, double size, Color color, double opacity = 1)
        {
            return new System.Windows.Shapes.Path
            {
                Data = Get(key) ?? Geometry.Empty,
                Fill = new SolidColorBrush(color),
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true,
                Opacity = opacity,
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }
    }
}
