import crypto from 'crypto';

// Masaüstü uygulaması da aynı OAuth uygulamasını kullanıyor: tarayıcıda
// giriş yapılıyor, callback token'ları 127.0.0.1'deki uygulamaya devrediyor.
// (Kick'e yeni bir redirect_uri kaydettirmeye gerek kalmasın diye.)
const APP_ORIGIN = process.env.APP_ORIGIN || 'https://kickforge-mor-tilki.vercel.app';

export default function handler(req, res) {
  const clientId = process.env.KICK_CLIENT_ID;
  if (!clientId) return res.status(500).send('KICK_CLIENT_ID tanımlı değil');
  const redirectUri = `${APP_ORIGIN}/api/callback`;

  // masaüstü modu: uygulamanın dinlediği yerel port
  const port = parseInt(String(req.query.desktop || ''), 10);
  const desktop = Number.isInteger(port) && port > 1023 && port < 65536;

  const state = String(req.query.state || crypto.randomBytes(16).toString('hex')).slice(0, 96);
  const codeVerifier = crypto.randomBytes(32).toString('base64url');
  const codeChallenge = crypto.createHash('sha256').update(codeVerifier).digest('base64url');

  const cookies = [
    `pkce_verifier=${codeVerifier}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=600`
  ];
  if (desktop) {
    cookies.push(`kickforge_desktop=${port}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=600`);
  }
  res.setHeader('Set-Cookie', cookies);

  const scope = 'chat:write user:read';
  const authUrl =
    `https://id.kick.com/oauth/authorize?client_id=${clientId}` +
    `&redirect_uri=${encodeURIComponent(redirectUri)}` +
    `&response_type=code&scope=${encodeURIComponent(scope)}` +
    `&state=${encodeURIComponent(state)}` +
    `&code_challenge=${codeChallenge}&code_challenge_method=S256`;

  res.redirect(authUrl);
}
