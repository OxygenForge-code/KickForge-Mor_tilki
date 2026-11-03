// web.kick.com sohbet geçmişini sunucu tarafından okur.
// istemciden aynı uç Cloudflare 403 veriyor; site (vercel) çıkışı geçiyor.
// stats.js'teki gibi: birkaç kez dener, hepsi düşerse kısa süreli "eski veri".

const BASLIKLAR = {
  'Accept': 'application/json, text/plain, */*',
  'Accept-Language': 'tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7',
  'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36',
  'Referer': 'https://kick.com/mor-tilki/chat',
  'Origin': 'https://kick.com'
};

const DENEME = 3;
const ESKI_SURE = 60 * 1000; // ms

let sonIyi = null;

const bekle = (ms) => new Promise((coz) => setTimeout(coz, ms));

export default async function handler(req, res) {
  const channelId = String(req.query.channelId || '').replace(/\D/g, '');
  const slug = String(req.query.slug || 'mor-tilki').replace(/[^a-zA-Z0-9_-]/g, '');
  if (!channelId) return res.status(400).json({ ok: false, error: 'channelId gerekli' });

  const url = `https://web.kick.com/api/v1/chat/${channelId}/history`;
  const hatalar = [];

  for (let tur = 0; tur < DENEME; tur++) {
    try {
      const r = await fetch(url, {
        headers: { ...BASLIKLAR, Referer: `https://kick.com/${slug}/chat` },
        cache: 'no-store'
      });
      if (!r.ok) {
        hatalar.push(`web ${r.status}`);
      } else {
        const body = await r.json();
        sonIyi = { body, zaman: Date.now() };
        res.setHeader('Cache-Control', 's-maxage=8, stale-while-revalidate=20');
        return res.status(200).json(body);
      }
    } catch (e) {
      hatalar.push(`web ${e.message}`);
    }
    if (tur < DENEME - 1) await bekle(350 + tur * 350);
  }

  // hepsi düştü: makul tazelikteki son iyi yanıtı ver
  if (sonIyi && Date.now() - sonIyi.zaman < ESKI_SURE) {
    res.setHeader('Cache-Control', 'no-store');
    return res.status(200).json(sonIyi.body);
  }

  res.setHeader('Cache-Control', 'no-store');
  return res.status(502).json({ ok: false, error: 'sohbet upucu yanıt vermiyor', detail: hatalar.join(' | ') });
}
