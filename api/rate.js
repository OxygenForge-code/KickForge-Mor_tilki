// beğeni/beğenmeme sayaçları: herkeste aynı uçtan senkron
// depo: github'daki data/ratings.json (contens api, GITHUB_TOKEN gerekli)
// token yoksa uyarır; uygulama o zaman yalnızca yerel sayaç devam eder.

const REPO = 'OxygenForge-code/KickForge-Mor_tilki';
const PATH = 'data/ratings.json';

let onceki = null; // { sha, likes, dislikes, zaman } — sıcak lambda belleği

const bozuk = (s) => { try { return JSON.parse(s); } catch { return { likes: 0, dislikes: 0 }; } };

async function githubGet() {
  const r = await fetch(`https://api.github.com/repos/${REPO}/contents/${PATH}`, {
    headers: {
      Accept: 'application/vnd.github+json',
      Authorization: `Bearer ${process.env.GITHUB_TOKEN}`,
      'User-Agent': 'kickforge-rate'
    },
    signal: AbortSignal.timeout(8000),
    cache: 'no-store'
  });
  if (r.status === 404) return { sha: null, likes: 0, dislikes: 0 };
  if (!r.ok) throw new Error('github ' + r.status);
  const j = await r.json();
  const body = bozuk(Buffer.from(j.content, 'base64').toString('utf8'));
  return { sha: j.sha || null, likes: body.likes | 0, dislikes: body.dislikes | 0 };
}

async function githubPut(cur,Delta) {
  const next = {
    likes: Math.max(0, cur.likes + (Delta.likes ? 1 : 0)),
    dislikes: Math.max(0, cur.dislikes + (Delta.dislikes ? 1 : 0))
  };
  const message = `rate\u003a ${next.likes} / ${next.dislikes}`;
  const body = {
    message,
    content: Buffer.from(JSON.stringify(next)).toString('base64'),
    ...(cur.sha ? { sha: cur.sha } : {})
  };
  await fetch(`https://api.github.com/repos/${REPO}/contents/${PATH}`, {
    method: 'PUT',
    headers: {
      Accept: 'application/vnd.github+json',
      Authorization: `Bearer ${process.env.GITHUB_TOKEN}`,
      'Content-Type': 'application/json',
      'User-Agent': 'kickforge-rate'
    },
    body: JSON.stringify(body),
    signal: AbortSignal.timeout(8000)
  });
  return next;
}

export default async function handler(req, res) {
  res.setHeader('Cache-Control', 'no-store');

  if (!process.env.GITHUB_TOKEN) {
    return res.status(503).json({ ok: false, reason: 'GITHUB_TOKEN ayarlanmamış' });
  }

  try {
    if (req.method === 'GET') {
      if (onceki && Date.now() - onceki.zaman < 4000) {
        return res.status(200).json({ ok: true, ...onceki });
      }
      const cur = await githubGet();
      onceki = { ...cur, zaman: Date.now() };
      return res.status(200).json({ ok: true, likes: cur.likes, dislikes: cur.dislikes });
    }

    if (req.method === 'POST') {
      const { likes, dislikes } = req.body || {};
      if (typeof likes !== 'number' && typeof dislikes !== 'number') {
        return res.status(400).json({ ok: false, reason: 'ihmal' });
      }
      const cur = await githubGet();
      const next = await githubPut(cur, { likes: Number(likes) || 0, dislikes: Number(dislikes) || 0 });
      onceki = { ...next, zaman: Date.now() };
      return res.status(200).json({ ok: true, likes: next.likes, dislikes: next.dislikes });
    }

    return res.status(405).json({ ok: false, reason: 'method' });
  } catch (e) {
    return res.status(500).json({ ok: false, reason: String(e.message || e) });
  }
}
