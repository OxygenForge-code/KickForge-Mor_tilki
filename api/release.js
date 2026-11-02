// api/release.js
// masaüstü uygulamasının son sürümü. github'ın herkese açık release ucunu
// sunucu tarafında okuyoruz: tarayıcıda cors ve saatlik istek sınırı derdi
// olmuyor, cevap da beş dakika cache'leniyor.

const REPO = 'OxygenForge-code/KickForge-Mor_tilki';

export default async function handler(req, res) {
  res.setHeader('Cache-Control', 'public, s-maxage=300, stale-while-revalidate=3600');

  try {
    const response = await fetch(`https://api.github.com/repos/${REPO}/releases/latest`, {
      headers: {
        Accept: 'application/vnd.github+json',
        'User-Agent': 'kickforge-site',
        ...(process.env.GITHUB_TOKEN ? { Authorization: `Bearer ${process.env.GITHUB_TOKEN}` } : {})
      },
      signal: AbortSignal.timeout(8000)
    });

    if (!response.ok) throw new Error('github ' + response.status);

    const release = await response.json();
    const assets = Array.isArray(release.assets) ? release.assets : [];
    const asset = assets.find((item) => item.name === 'KickForge.exe') || assets[0];

    res.status(200).json({
      ok: true,
      version: readVersion(release),
      build: release.tag_name || '',
      published: release.published_at || release.created_at || null,
      size: asset && typeof asset.size === 'number' ? asset.size : null,
      url: (asset && asset.browser_download_url) || release.html_url || '',
      title: release.name || ''
    });
  } catch (error) {
    // github yanıt vermediyse site eski bilgiyi göstermeye devam etsin
    res.status(200).json({ ok: false, reason: String((error && error.message) || error) });
  }
}

// release adı "KickForge 2.1.0 · r16" biçiminde: sürümü oradan çekiyoruz
function readVersion(release) {
  const sources = [release.name, release.body, release.tag_name];
  for (const source of sources) {
    if (typeof source !== 'string') continue;
    const match = source.match(/\b(\d+\.\d+\.\d+)\b/);
    if (match) return match[1];
  }
  return String(release.tag_name || '').replace(/^windows-/, '');
}
