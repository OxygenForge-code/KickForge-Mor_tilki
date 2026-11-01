using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KickForgeDesktop.Settings
{
    public enum AppTheme
    {
        ControlRoom, // varsayılan: koyu, lime
        Midnight,    // mavi-mor
        Sakura,      // sıcak pembe
        Mono         // sadece gri
    }

    public enum NotifySound
    {
        None,
        Pulse,
        Blip,
        Chime
    }

    [Serializable]
    public class AppSettings : INotifyPropertyChanged
    {
        private AppTheme _theme = AppTheme.ControlRoom;
        private double _uiScale = 100;
        private bool _alwaysOnTop;
        private bool _minimizeToTray = true;
        private bool _startMinimized;
        private bool _startWithWindows;
        private bool _compactMode;
        private double _opacity = 100;
        private double _windowWidth = 1180;
        private double _windowHeight = 760;
        private double _windowLeft = double.NaN;
        private double _windowTop = double.NaN;
        private bool _rememberBounds = true;

        private bool _notifyLive = true;
        private bool _notifyFollower = true;
        private bool _notifyViewerSpike = true;
        private bool _notifyRecover = true;
        private NotifySound _sound = NotifySound.Pulse;
        private int _soundVolume = 60;
        private bool _quietHours;
        private int _quietStart = 23;
        private int _quietEnd = 9;

        private int _pollSeconds = 20;
        private string _apiBase = "https://kickforge-mor-tilki.vercel.app";
        private string _channelSlug = "mor-tilki";
        private bool _autoRetry = true;
        private bool _showSparkline = true;
        private int _historyMinutes = 180;

        private string _audioDeviceId = "";
        private bool _duckOthers;
        private bool _followDefaultDevice = true;

        private bool _hotkeysEnabled = true;
        private string _hotkeyShow = "Ctrl+Shift+K";
        private string _hotkeyTop = "Ctrl+Shift+T";
        private string _hotkeyMute = "Ctrl+Shift+M";

        private bool _logToFile = true;

        public AppTheme Theme { get => _theme; set => Set(ref _theme, value); }
        public double UiScale
        {
            get => _uiScale;
            set => Set(ref _uiScale, Clamp(value, 80, 150));
        }

        public bool AlwaysOnTop { get => _alwaysOnTop; set => Set(ref _alwaysOnTop, value); }
        public bool MinimizeToTray { get => _minimizeToTray; set => Set(ref _minimizeToTray, value); }
        public bool StartMinimized { get => _startMinimized; set => Set(ref _startMinimized, value); }
        public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
        public bool CompactMode { get => _compactMode; set => Set(ref _compactMode, value); }
        public double Opacity
        {
            get => _opacity;
            set => Set(ref _opacity, Clamp(value, 40, 100));
        }

        public double WindowWidth { get => _windowWidth; set => Set(ref _windowWidth, value); }
        public double WindowHeight { get => _windowHeight; set => Set(ref _windowHeight, value); }
        public double WindowLeft { get => _windowLeft; set => Set(ref _windowLeft, value); }
        public double WindowTop { get => _windowTop; set => Set(ref _windowTop, value); }
        public bool RememberBounds { get => _rememberBounds; set => Set(ref _rememberBounds, value); }

        public bool NotifyLive { get => _notifyLive; set => Set(ref _notifyLive, value); }
        public bool NotifyFollower { get => _notifyFollower; set => Set(ref _notifyFollower, value); }
        public bool NotifyViewerSpike { get => _notifyViewerSpike; set => Set(ref _notifyViewerSpike, value); }
        public bool NotifyRecover { get => _notifyRecover; set => Set(ref _notifyRecover, value); }
        public NotifySound Sound { get => _sound; set => Set(ref _sound, value); }
        public int SoundVolume
        {
            get => _soundVolume;
            set => Set(ref _soundVolume, (int)Clamp(value, 0, 100));
        }

        public bool QuietHours { get => _quietHours; set => Set(ref _quietHours, value); }
        public int QuietStart
        {
            get => _quietStart;
            set => Set(ref _quietStart, (int)Clamp(value, 0, 23));
        }

        public int QuietEnd
        {
            get => _quietEnd;
            set => Set(ref _quietEnd, (int)Clamp(value, 0, 23));
        }

        public int PollSeconds
        {
            get => _pollSeconds;
            set => Set(ref _pollSeconds, (int)Clamp(value, 5, 300));
        }

        public string ApiBase { get => _apiBase; set => Set(ref _apiBase, (value ?? "").TrimEnd('/')); }
        public string ChannelSlug { get => _channelSlug; set => Set(ref _channelSlug, (value ?? "").Trim()); }
        public bool AutoRetry { get => _autoRetry; set => Set(ref _autoRetry, value); }
        public bool ShowSparkline { get => _showSparkline; set => Set(ref _showSparkline, value); }
        public int HistoryMinutes
        {
            get => _historyMinutes;
            set => Set(ref _historyMinutes, (int)Clamp(value, 30, 720));
        }

        public string AudioDeviceId { get => _audioDeviceId; set => Set(ref _audioDeviceId, value ?? ""); }
        public bool DuckOthers { get => _duckOthers; set => Set(ref _duckOthers, value); }
        public bool FollowDefaultDevice { get => _followDefaultDevice; set => Set(ref _followDefaultDevice, value); }

        public bool HotkeysEnabled { get => _hotkeysEnabled; set => Set(ref _hotkeysEnabled, value); }
        public string HotkeyShow { get => _hotkeyShow; set => Set(ref _hotkeyShow, value); }
        public string HotkeyTop { get => _hotkeyTop; set => Set(ref _hotkeyTop, value); }
        public string HotkeyMute { get => _hotkeyMute; set => Set(ref _hotkeyMute, value); }

        public bool LogToFile { get => _logToFile; set => Set(ref _logToFile, value); }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value)) return min;
            return value < min ? min : value > max ? max : value;
        }

        public AppSettings Clone()
        {
            var copy = new AppSettings();
            foreach (var p in typeof(AppSettings).GetProperties())
            {
                if (p.CanWrite && p.CanRead && p.PropertyType.IsValueType || p.PropertyType == typeof(string))
                {
                    p.SetValue(copy, p.GetValue(this));
                }
            }
            return copy;
        }
    }
}
