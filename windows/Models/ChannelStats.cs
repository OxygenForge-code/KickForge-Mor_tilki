using System;

namespace KickForgeDesktop.Models
{
    public class ChannelStats
    {
        public bool IsLive { get; set; }
        public int ViewerCount { get; set; }
        public string SessionTitle { get; set; } = "";
        public long FollowersCount { get; set; }
        public string? ProfilePic { get; set; }
        public bool Verified { get; set; }
        public long? BroadcasterId { get; set; }
        public bool Stale { get; set; }

        public DateTime FetchedAt { get; set; } = DateTime.Now;

        public ChannelStats Clone()
        {
            return (ChannelStats)MemberwiseClone();
        }
    }

    public class FeedEvent
    {
        public DateTime Time { get; set; }
        public string Kind { get; set; } = "";
        public string Text { get; set; } = "";

        public string Clock => Time.ToString("HH:mm:ss");
        public string Day => Time.ToString("dd.MM");
    }
}
