export default async function handler(req, res) {
  const cookies = req.headers.cookie || '';
  const refreshMatch = cookies.match(/kick_refresh=([^;]+)/);
  const refreshToken = refreshMatch ? refreshMatch[1] : null;

  if (!refreshToken) {
    return res.status(401).json({ error: 'Refresh token bulunamadı, lütfen tekrar giriş yapın.' });
  }

  const clientId = process.env.KICK_CLIENT_ID;
  const clientSecret = process.env.KICK_CLIENT_SECRET;

  const params = new URLSearchParams();
  params.append('grant_type', 'refresh_token');
  params.append('client_id', clientId);
  params.append('client_secret', clientSecret);
  params.append('refresh_token', refreshToken);

  try {
    const response = await fetch('https://id.kick.com/oauth/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: params
    });

    const data = await response.json();

    if (data.access_token) {
      res.setHeader('Set-Cookie', [
        `kick_token=${data.access_token}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${data.expires_in || 3600}`,
        `kick_refresh=${data.refresh_token}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${60 * 60 * 24 * 30}`
      ]);
      res.status(200).json({ success: true });
    } else {
      res.status(401).json({ error: 'Refresh başarısız', details: data });
    }
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
}
