// Kick'in herkese açık kanal ucundan durumu çeker.
//
// Vercel'in çıkış IP'leri Cloudflare tarafından ara ara 403 yiyor; aynı
// saniyede başka bir istek geçebiliyor. O yüzden:
//   - v2 düşerse v1'e bakıyoruz,
//   - toplamda birkaç kez deniyoruz,
//   - hepsi düşerse son başarılı yanıtı (sıcak lambda belleğinde) kısa süre
//     "eski veri" olarak servis ediyoruz; sayfa boş kalmasın,
//   - yine de veri yoksa upstream durum kodlarını yanıta koyuyoruz.

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

const DENEME = 3;
const ESKI_VERI_SURESI = 10 * 60 * 1000; // ms

// sıcak lambda'nın belleğinde durur; soğuk başlangıçta boş olur
let sonIyi = null;

const bekle = (ms) => new Promise((coz) => setTimeout(coz, ms));

function temizle(data) {
  const yayin = data.livestream || null;
  return {
    isLive: !!(yayin && yayin.is_live),
    viewerCount: yayin ? yayin.viewer_count || 0 : 0,
    sessionTitle: yayin ? yayin.session_title || '' : '',
    followersCount: data.followers_count || 0,
    profilePic: data.user ? data.user.profile_pic : null,
    verified: data.verified || (data.user && data.user.verified) || false,
    broadcasterId: data.user ? data.user.id : null
  };
}

export default async function handler(req, res) {
  const hatalar = [];

  for (let tur = 0; tur < DENEME; tur++) {
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

        const govde = temizle(data);
        sonIyi = { govde, zaman: Date.now() };
        res.setHeader('Cache-Control', 's-maxage=25, stale-while-revalidate=60');
        return res.status(200).json(govde);
      } catch (error) {
        hatalar.push(`${ad} ${error.message}`);
      }
    }
    if (tur < DENEME - 1) await bekle(400 + tur * 400);
  }

  console.error('istatistik hatası:', hatalar.join(' | '));

  // kısa süre önce elimizde veri varsa onu ver: "canlı mı" bilgisi birkaç
  // dakika eski olsun, ama ekran bomboş kalmasın
  if (sonIyi && Date.now() - sonIyi.zaman < ESKI_VERI_SURESI) {
    res.setHeader('Cache-Control', 'no-store');
    return res.status(200).json({ ...sonIyi.govde, stale: true });
  }

  res.status(502).json({ error: 'Veriler alınamadı', upstream: hatalar.slice(-4) });
}
