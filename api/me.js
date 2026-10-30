// api/me.js
export default function handler(req, res) {
  const cookies = req.headers.cookie || '';
  const hasToken = cookies.includes('kick_token=');
  res.status(200).json({ loggedIn: hasToken });
}
