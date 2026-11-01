using System;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace KickForgeDesktop.Models
{
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
    }

    public class ChatFeed
    {
        public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();
    }
}
