// Masaüstü uygulaması token'ı kendi saklıyor; süresi dolunca buradan
// yeniliyor. client_secret yalnızca sunucuda durur.
export default async function handler(req, res) {
  if (req.method !== 'POST') return res.status(405).json({ error: 'POST kullan' });

  const body = typeof req.body === 'string' ? safeParse(req.body) : req.body || {};
  const refreshToken = body.refresh_token;
  if (!refreshToken) return res.status(400).json({ error: 'refresh_token gerekli' });

  const params = new URLSearchParams();
  params.append('grant_type', 'refresh_token');
  params.append('client_id', process.env.KICK_CLIENT_ID);
  if (process.env.KICK_CLIENT_SECRET) params.append('client_secret', process.env.KICK_CLIENT_SECRET);
  params.append('refresh_token', refreshToken);

  try {
    const response = await fetch('https://id.kick.com/oauth/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: params
    });
    const data = await response.json();
    if (!data.access_token) return res.status(401).json({ error: 'yenilenemedi', details: data });
    res.setHeader('Cache-Control', 'no-store');
    res.status(200).json({
      access_token: data.access_token,
      refresh_token: data.refresh_token || refreshToken,
      expires_in: data.expires_in || 3600
    });
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
}

function safeParse(text) {
  try {
    return JSON.parse(text);
  } catch {
    return {};
  }
}
