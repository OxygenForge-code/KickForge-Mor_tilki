export default async function handler(req, res) {
  const code = req.query.code;

  if (!code) {
    return res.status(400).send("Yetkilendirme kodu bulunamadı.");
  }

  const clientId = process.env.KICK_CLIENT_ID;         // BURASI DEĞİŞTİ
  const clientSecret = process.env.KICK_CLIENT_SECRET; // BURASI DEĞİŞTİ
  const redirectUri = `https://kickforge-mor-tilki.vercel.app/api/callback`;

  const params = new URLSearchParams();
  params.append('grant_type', 'authorization_code');
  params.append('client_id', clientId);
  params.append('client_secret', clientSecret);
  params.append('redirect_uri', redirectUri);
  params.append('code', code);

  try {
    const response = await fetch('https://id.kick.com/oauth/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: params
    });

    const data = await response.json();

    if (data.access_token) {
      res.setHeader('Set-Cookie', `kick_token=${data.access_token}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${data.expires_in || 3600}`);
      res.redirect('/');
    } else {
      res.status(500).json(data); 
    }
  } catch (err) {
    res.status(500).send('Sunucu hatası: ' + err.message);
  }
}
