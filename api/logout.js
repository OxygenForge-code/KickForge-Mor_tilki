// api/logout.js
export default function handler(req, res) {
  res.setHeader('Set-Cookie', `kick_token=; Path=/; HttpOnly; Secure; Max-Age=0`);
  res.status(200).json({ success: true });
}
