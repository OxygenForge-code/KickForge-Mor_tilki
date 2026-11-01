using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace KickForgeDesktop.Models
{
    /// <summary>sohbette ismin önünde duran küçük rozet (MOD, VIP, LV 11…).</summary>
    public class ChatBadge
    {
        public string Text { get; set; } = "";
        public string Kind { get; set; } = "other";
        public Color Color { get; set; } = Color.FromRgb(0xC8, 0xC2, 0xD8);
        public int Order { get; set; }
    }

    public class ChatMessage
    {
        public string Id { get; set; } = "";
        public string User { get; set; } = "";
        public string Content { get; set; } = "";
        public DateTime Time { get; set; }
        public bool IsSystem { get; set; }
        public bool IsMine { get; set; }

        public string Clock => Time.ToLocalTime().ToString("HH:mm");
        public Brush UserBrush { get; set; } = Brushes.LightGray;

        public List<ChatBadge> Badges { get; } = new List<ChatBadge>();
        public bool IsModerator { get; set; }
        public bool IsVip { get; set; }
        public bool IsBot { get; set; }
        public int Level { get; set; }

        /// <summary>fare üzerine gelince gösterilecek özet.</summary>
        public string Tooltip
        {
            get
            {
                var parts = new List<string>();
                if (IsModerator) parts.Add("moderatör");
                if (IsVip) parts.Add("vip");
                if (IsBot) parts.Add("bot");
                if (Level > 0) parts.Add("seviye " + Level);
                var tail = Time.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
                return parts.Count == 0 ? User + " · " + tail : User + " · " + string.Join(" · ", parts) + " · " + tail;
            }
        }
    }

    public class ChatFeed
    {
        public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();
    }
}
