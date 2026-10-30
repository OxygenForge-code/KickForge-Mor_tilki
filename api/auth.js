export default function handler(req, res) {
  const clientId = process.env.KICK_CLIENT_ID; // BURASI DEĞİŞTİ
  
  const redirectUri = `https://kickforge-mor-tilki.vercel.app/api/callback`; 
  const scope = 'chat:write user:read';
  const state = 'kickforge_secure';

  const authUrl = `https://id.kick.com/oauth/authorize?client_id=${clientId}&redirect_uri=${encodeURIComponent(redirectUri)}&response_type=code&scope=${encodeURIComponent(scope)}&state=${state}`;
  
  res.redirect(authUrl);
}
