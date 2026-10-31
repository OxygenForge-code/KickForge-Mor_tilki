// Kick'in herkese açık kanal ucundan durumu çeker.
//
// Vercel'in çıkış IP'si bazı günlerde Cloudflare tarafından kısa/eksik
// User-Agent ile bloklanıyor; bu yüzden tam tarayıcı başlıkları gönderiyoruz
// ve v2 düşerse v1'e de bakıyoruz. İkisi de düşerse upstream durum kodlarını
// yanıta koyuyoruz ki sayfada/curl'de sebep görünsün.

const BASLIKLAR = {
  'Accept': 'application/json, text/plain, */*',
  'Accept-Language': 'tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7',
  'User-Agent':
    'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36',
  'Referer': 'https://kick.com/mor-tilki',
  'Origin': 'https://kick.com',
  'sec-ch-ua': '"Chromium";v="128", "Not;A=Brand";v="24", "Google Chrome";v="128"',
  'sec-ch-ua-mobile': '?0',
  'sec-ch-ua-platform': '"Windows"'
};

const KANALLAR = [
  'https://kick.com/api/v2/channels/mor-tilki',
  'https://kick.com/api/v1/channels/mor-tilki'
];

export default async function handler(req, res) {
  const hatalar = [];

  for (const url of KANALLAR) {
    const ad = url.includes('/v2/') ? 'v2' : 'v1';
    try {
      const response = await fetch(url, { headers: BASLIKLAR, cache: 'no-store' });
      if (!response.ok) {
        hatalar.push(`${ad} ${response.status}`);
        continue;
      }

      const data = await response.json();
      if (!data || typeof data !== 'object') {
        hatalar.push(`${ad} boş gövde`);
        continue;
      }

      const yayin = data.livestream || null;
      res.setHeader('Cache-Control', 's-maxage=20, stale-while-revalidate=40');
      return res.status(200).json({
        isLive: !!(yayin && yayin.is_live),
        viewerCount: yayin ? yayin.viewer_count || 0 : 0,
        sessionTitle: yayin ? yayin.session_title || '' : '',
        followersCount: data.followers_count || 0,
        profilePic: data.user ? data.user.profile_pic : null,
        verified: data.verified || (data.user && data.user.verified) || false,
        broadcasterId: data.user ? data.user.id : null
      });
    } catch (error) {
      hatalar.push(`${ad} ${error.message}`);
    }
  }

  console.error('istatistik hatası:', hatalar.join(' | '));
  res.status(502).json({ error: 'Veriler alınamadı', upstream: hatalar });
}
