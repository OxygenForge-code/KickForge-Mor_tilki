export default async function handler(req, res) {
  if (req.method !== 'POST') return res.status(405).send('İzin verilmiyor');

  // Çerezden token'ı al
  const cookies = req.headers.cookie || '';
  const tokenMatch = cookies.match(/kick_token=([^;]+)/);
  const token = tokenMatch ? tokenMatch[1] : null;

  if (!token) {
    return res.status(401).send('Giriş yapmalısın');
  }

  const { content, broadcaster_id } = req.body;

  try {
    const response = await fetch('https://api.kick.com/public/v1/chat', {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        broadcaster_user_id: parseInt(broadcaster_id),
        content: content,
        type: 'user'
      })
    });

    if (!response.ok) {
      const err = await response.text();
      return res.status(response.status).send(err);
    }

    res.status(200).json({ success: true });
  } catch (err) {
    res.status(500).send(err.message);
  }
}
