export default async function handler(req, res) {
  if (req.method !== 'POST') return res.status(405).send('İzin verilmiyor');

  const cookies = req.headers.cookie || '';
  const tokenMatch = cookies.match(/kick_token=([^;]+)/);
  let token = tokenMatch ? tokenMatch[1] : null;

  if (!token) {
    return res.status(401).send('Giriş yapmalısın');
  }

  const { content, broadcaster_id } = req.body;

  // Token ile mesaj göndermeyi dene
  let response = await fetch('https://api.kick.com/public/v1/chat', {
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

  // Eğer token süresi dolmuşsa (401), refresh yapıp tekrar dene
  if (response.status === 401) {
    const refreshRes = await fetch('https://kickforge-mor-tilki.vercel.app/api/refresh', {
      method: 'POST',
      headers: { Cookie: req.headers.cookie || '' }
    });

    if (refreshRes.ok) {
      const newCookies = refreshRes.headers.get('set-cookie');
      if (newCookies) {
        // Yeni token'ı al
        const newTokenMatch = newCookies.match(/kick_token=([^;]+)/);
        if (newTokenMatch) {
          token = newTokenMatch[1];
          // Yeni çerezleri tarayıcıya gönder ve isteği tekrarla
          res.setHeader('Set-Cookie', newCookies.split(', ')); // Vercel'de birden fazla çerez için
          response = await fetch('https://api.kick.com/public/v1/chat', {
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
        }
      }
    }
  }

  if (!response.ok) {
    const err = await response.text();
    return res.status(response.status).send(err);
  }

  res.status(200).json({ success: true });
}
