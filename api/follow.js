export default async function handler(req, res) {
  if (req.method !== 'POST') {
    return res.status(405).json({ error: 'Yalnızca POST istekleri destekleniyor' });
  }

  const cookies = req.headers.cookie || '';
  const tokenMatch = cookies.match(/kick_token=([^;]+)/);
  const token = tokenMatch ? tokenMatch[1] : null;

  if (!token) {
    return res.status(401).json({ error: 'Takip etmek için Kick ile giriş yapmalısın.' });
  }

  try {
    const kickRes = await fetch('https://kick.com/api/v2/channels/mor-tilki/follow', {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json',
        'Accept': 'application/json',
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36'
      }
    });

    if (!kickRes.ok) {
      const errText = await kickRes.text();
      return res.status(kickRes.status).json({ error: errText || 'Kick takip işlemi reddetti' });
    }

    return res.status(200).json({ success: true });
  } catch (err) {
    return res.status(500).json({ error: err.message });
  }
}
