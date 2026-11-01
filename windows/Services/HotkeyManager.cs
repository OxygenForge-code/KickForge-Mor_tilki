using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace KickForgeDesktop.Services
{
    /// <summary>sistem geneli kısayollar (uygulama arkada olsa da çalışır).</summary>
    public class HotkeyManager : IDisposable
    {
        private const int WmHotkey = 0x0312;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModWin = 0x0008;
        private const uint ModNoRepeat = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private readonly Dictionary<int, Action> _actions = new Dictionary<int, Action>();
        private readonly List<int> _registered = new List<int>();
        private HwndSource? _source;
        private IntPtr _handle;
        private int _nextId = 0x4B46;

        public List<string> Failures { get; } = new List<string>();

        public void Attach(Window window)
        {
            var helper = new WindowInteropHelper(window);
            _handle = helper.EnsureHandle();
            _source = HwndSource.FromHwnd(_handle);
            _source?.AddHook(Hook);
        }

        public bool Register(string gesture, Action action, string label)
        {
            if (_handle == IntPtr.Zero) return false;
            if (!TryParse(gesture, out var modifiers, out var key))
            {
                Failures.Add(label + ": geçersiz kısayol (" + gesture + ")");
                return false;
            }

            var id = _nextId++;
            if (!RegisterHotKey(_handle, id, modifiers | ModNoRepeat, (uint)KeyInterop.VirtualKeyFromKey(key)))
            {
                Failures.Add(label + ": " + gesture + " başka bir uygulamada kullanılıyor");
                return false;
            }

            _actions[id] = action;
            _registered.Add(id);
            return true;
        }

        public void Clear()
        {
            foreach (var id in _registered)
            {
                UnregisterHotKey(_handle, id);
            }
            _registered.Clear();
            _actions.Clear();
            Failures.Clear();
        }

        private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
            {
                handled = true;
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Log.Write("kısayol hatası: " + ex.Message);
                }
            }
            return IntPtr.Zero;
        }

        public static bool TryParse(string? gesture, out uint modifiers, out Key key)
        {
            modifiers = 0;
            key = Key.None;
            if (string.IsNullOrWhiteSpace(gesture)) return false;

            var parts = gesture.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            var keyName = "";
            foreach (var raw in parts)
            {
                var part = raw.Trim();
                switch (part.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= ModControl;
                        break;
                    case "shift":
                        modifiers |= ModShift;
                        break;
                    case "alt":
                        modifiers |= ModAlt;
                        break;
                    case "win":
                    case "windows":
                        modifiers |= ModWin;
                        break;
                    default:
                        keyName = part;
                        break;
                }
            }

            if (keyName.Length == 0) return false;
            if (!Enum.TryParse<Key>(keyName, true, out key) || key == Key.None) return false;
            return true;
        }

        public void Dispose()
        {
            Clear();
            _source?.RemoveHook(Hook);
            _source = null;
        }
    }
}
