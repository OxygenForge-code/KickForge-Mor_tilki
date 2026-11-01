// Kick'ten dönen kodu token'a çevirir. Tarayıcı akışında çerez, masaüstü
// akışında 127.0.0.1'deki uygulamaya yönlendirme ile teslim edilir.
const APP_ORIGIN = process.env.APP_ORIGIN || 'https://kickforge-mor-tilki.vercel.app';

export default async function handler(req, res) {
  const code = req.query.code;
  if (!code) return res.status(400).send('Yetkilendirme kodu bulunamadı.');

  const cookies = req.headers.cookie || '';
  const verifierMatch = cookies.match(/pkce_verifier=([^;]+)/);
  const codeVerifier = verifierMatch ? verifierMatch[1] : null;
  if (!codeVerifier) return res.status(400).send('PKCE doğrulama kodu bulunamadı.');

  const desktopMatch = cookies.match(/kickforge_desktop=(\d+)/);
  const desktopPort = desktopMatch ? Number(desktopMatch[1]) : 0;

  const clientId = process.env.KICK_CLIENT_ID;
  const clientSecret = process.env.KICK_CLIENT_SECRET;
  const redirectUri = `${APP_ORIGIN}/api/callback`;

  const params = new URLSearchParams();
  params.append('grant_type', 'authorization_code');
  params.append('client_id', clientId);
  if (clientSecret) params.append('client_secret', clientSecret);
  params.append('redirect_uri', redirectUri);
  params.append('code', code);
  params.append('code_verifier', codeVerifier);

  try {
    const response = await fetch('https://id.kick.com/oauth/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: params
    });

    const data = await response.json();

    if (!data.access_token) {
      return res.status(500).json(data);
    }

    if (desktopPort) {
      // token'lar fragment içinde gider: sunucuya geri dönmez, günlüğe düşmez
      const payload = new URLSearchParams({
        access_token: data.access_token,
        refresh_token: data.refresh_token || '',
        expires_in: String(data.expires_in || 3600),
        state: String(req.query.state || '')
      });
      res.setHeader('Set-Cookie', [
        'kickforge_desktop=; Path=/; HttpOnly; Secure; Max-Age=0',
        'pkce_verifier=; Path=/; HttpOnly; Secure; Max-Age=0'
      ]);
      return res.redirect(`http://127.0.0.1:${desktopPort}/auth#${payload.toString()}`);
    }

    res.setHeader('Set-Cookie', [
      `kick_token=${data.access_token}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${data.expires_in || 3600}`,
      `kick_refresh=${data.refresh_token}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${60 * 60 * 24 * 30}`
    ]);
    res.redirect('/');
  } catch (err) {
    res.status(500).send('Sunucu hatası: ' + err.message);
  }
}
