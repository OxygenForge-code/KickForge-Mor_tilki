export default async function handler(req, res) {
  try {
    const response = await fetch('https://kick.com/api/v2/channels/mor-tilki', {
      headers: {
        'Accept': 'application/json',
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36'
      }
    });

    if (!response.ok) {
      throw new Error(`Kick API hatası: ${response.status}`);
    }

    const data = await response.json();
    
    // Tarayıcıya gerekli olan temiz veriyi gönder
    res.status(200).json({
      isLive: data.livestream && data.livestream.is_live,
      viewerCount: data.livestream ? data.livestream.viewer_count : 0,
      sessionTitle: data.livestream ? data.livestream.session_title : '',
      followersCount: data.followers_count || 0,
      profilePic: data.user ? data.user.profile_pic : null,
      verified: data.verified || (data.user && data.user.verified),
      broadcasterId: data.user ? data.user.id : null
    });
  } catch (error) {
    console.error('İstatistik çekme hatası:', error);
    res.status(500).json({ error: 'Veriler alınamadı' });
  }
}
