import crypto from 'crypto';

export default function handler(req, res) {
  const clientId = process.env.KICK_CLIENT_ID;
  const redirectUri = `https://kickforge-mor-tilki.vercel.app/api/callback`;

  // 1. Rastgele bir code_verifier oluştur
  const codeVerifier = crypto.randomBytes(32).toString('base64url');

  // 2. code_verifier'ın SHA-256 hash'ini al (code_challenge)
  const codeChallenge = crypto
    .createHash('sha256')
    .update(codeVerifier)
    .digest('base64url');

  // 3. code_verifier'ı güvenli bir şekilde sakla (örn. HttpOnly cookie)
  res.setHeader('Set-Cookie', `pkce_verifier=${codeVerifier}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=600`);

  const scope = 'chat:write user:read';
  const state = 'kickforge_secure';

  // 4. PKCE parametrelerini de ekleyerek yetkilendirme URL'sini oluştur
  const authUrl = `https://id.kick.com/oauth/authorize?client_id=${clientId}&redirect_uri=${encodeURIComponent(redirectUri)}&response_type=code&scope=${encodeURIComponent(scope)}&state=${state}&code_challenge=${codeChallenge}&code_challenge_method=S256`;

  res.redirect(authUrl);
}
