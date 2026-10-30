export default function handler(req, res) {
  const clientId = process.env.Client_Id;
  // Vercel'deki canlı linkini buraya yaz (Kick Developer paneline de aynısını yazmalısın)
  const redirectUri = `https://kickforge-mor-tilki.vercel.app/api/callback`;
  
  const scope = 'chat:write user:read';
  const state = 'kickforge_secure';

  const authUrl = `https://id.kick.com/oauth/authorize?client_id=${clientId}&redirect_uri=${redirectUri}&response_type=code&scope=${scope}&state=${state}`;
  
  res.redirect(authUrl);
}
