using System;
using System.Drawing;
using System.IO;
using System.Windows;

namespace KickForgeDesktop.Services
{
    /// <summary>tepsi simgesi: kapatınca uygulama arka planda kalabilsin.</summary>
    public class TrayService : IDisposable
    {
        private readonly System.Windows.Forms.NotifyIcon _icon;
        private readonly System.Windows.Forms.ToolStripMenuItem _statusItem;
        private readonly System.Windows.Forms.ToolStripMenuItem _liveItem;

        public event Action? ShowRequested;
        public event Action? ExitRequested;

        public TrayService()
        {
            _statusItem = new System.Windows.Forms.ToolStripMenuItem("kanal: bağlanıyor…") { Enabled = false };
            _liveItem = new System.Windows.Forms.ToolStripMenuItem("yayın: kapalı") { Enabled = false };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add(_statusItem);
            menu.Items.Add(_liveItem);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            var open = new System.Windows.Forms.ToolStripMenuItem("pencereyi aç");
            open.Click += (_, _) => ShowRequested?.Invoke();
            menu.Items.Add(open);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            var exit = new System.Windows.Forms.ToolStripMenuItem("çıkış");
            exit.Click += (_, _) => ExitRequested?.Invoke();
            menu.Items.Add(exit);

            _icon = new System.Windows.Forms.NotifyIcon
            {
                Text = "KickForge",
                Icon = LoadIcon(),
                Visible = true,
                ContextMenuStrip = menu
            };
            _icon.DoubleClick += (_, _) => ShowRequested?.Invoke();
        }

        private static Icon LoadIcon()
        {
            try
            {
                var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
                if (resource != null)
                {
                    using var stream = resource.Stream;
                    return new Icon(stream, 32, 32);
                }
            }
            catch (Exception ex)
            {
                Log.Write("tepsi simgesi yüklenemedi: " + ex.Message);
            }

            return SystemIcons.Application;
        }

        public void Update(string channelText, bool isLive, int viewers, string tooltip)
        {
            _statusItem.Text = channelText;
            _liveItem.Text = isLive ? "yayın: açık · " + viewers + " izleyici" : "yayın: kapalı";
            try
            {
                _icon.Text = tooltip.Length > 62 ? tooltip.Substring(0, 62) : tooltip;
            }
            catch
            {
                // balon ipucu sınırı aşılırsa sessizce geç
            }
        }

        public void Notify(string title, string message)
        {
            try
            {
                _icon.BalloonTipTitle = title;
                _icon.BalloonTipText = message;
                _icon.ShowBalloonTip(4000);
            }
            catch (Exception ex)
            {
                Log.Write("bildirim gösterilemedi: " + ex.Message);
            }
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
