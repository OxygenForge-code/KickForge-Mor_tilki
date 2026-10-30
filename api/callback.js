export default async function handler(req, res) {
  // Vercel'in req.query'si yerine URL'den parametreleri okumak daha garantidir
  const protocol = req.headers['x-forwarded-proto'] || 'http';
  const url = new URL(req.url, `${protocol}://${req.headers.host}`);
  const code = url.searchParams.get('code');
  const error = url.searchParams.get('error');

  // Eğer Kick bir hata döndürdüyse (örn. kullanıcı izin vermediyse)
  if (error) {
    return res.status(400).send(`
      <div style="font-family: sans-serif; padding: 40px; text-align: center;">
        <h1 style="color: red;">Kick Hatası: ${error}</h1>
        <p>Yetkilendirme sırasında bir sorun oluştu.</p>
        <a href="/">Geri Dön</a>
      </div>
    `);
  }

  // Kod yoksa (kullanıcı bu sayfaya doğrudan girmişse)
  if (!code) {
    return res.status(400).send(`
      <div style="font-family: sans-serif; padding: 40px; text-align: center;">
        <h1>Yetkilendirme Kodu Bulunamadı</h1>
        <p>Bu sayfaya doğrudan giremezsiniz. Lütfen ana sayfaya dönüp "Kick ile Giriş Yap" butonuna tıklayın.</p>
        <br>
        <a href="/" style="background: #D0BCFF; color: #381E72; padding: 10px 20px; text-decoration: none; border-radius: 5px; font-weight: bold;">Ana Sayfaya Dön</a>
      </div>
    `);
  }

  // Token alma işlemi
  const clientId = process.env.Client_Id;
  const clientSecret = process.env.Client_Secret;
  // DİKKAT: Bu adres Kick panelindekiyle BİREBİR AYNI olmalı
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
      // Token başarıyla alındı, çereze kaydet ve ana sayfaya dön
      res.setHeader('Set-Cookie', `kick_token=${data.access_token}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${data.expires_in || 3600}`);
      res.redirect('/');
    } else {
      // Kick token vermeyi reddetti (genelde Client Secret veya Redirect URI yanlıştır)
      res.status(500).send(`
        <div style="font-family: sans-serif; padding: 40px; text-align: center;">
          <h1 style="color: red;">Token Alınamadı</h1>
          <p>Kick sunucusu token vermeyi reddetti. Detaylar aşağıda:</p>
          <pre style="background: #f0f0f0; padding: 20px; text-align: left; display: inline-block;">${JSON.stringify(data, null, 2)}</pre>
          <br><br>
          <a href="/">Geri Dön</a>
        </div>
      `);
    }
  } catch (err) {
    res.status(500).send('Sunucu hatası: ' + err.message);
  }
}
