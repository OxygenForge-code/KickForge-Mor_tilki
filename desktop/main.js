const { app, BrowserWindow, Menu, Tray, nativeImage, shell, dialog, session, systemPreferences } = require('electron');
const path = require('path');
const zlib = require('zlib');

/* Site normalde canlı çalışıyor; internet yoksa paketin içine gömdüğümüz
   index.html kopyasına düşüyor (o da güzelden uyarıyı gösterir). */
const SITE = 'https://kickforge-mor-tilki.vercel.app/';
const OFFLINE_COPY = path.join(__dirname, 'index.html');

app.setName('KickForge');

/* Bu dosya yalnızca masaüstünde var olan şeyler: menü çubuğu, simgeye
   küçültme, her zaman üstte, kulaklık/ses çıkışı seçimi ve mikrofon
   izinleri. Sayfaya bunları hiç karıştırmıyoruz — sayfa birebir web'deki
   gibi kalıyor, kabuk ekliyor. */
const state = {
  alwaysOnTop: false,
  trayMode: false, // kapatınca pencere simgede kalsın
  muted: false,
  sinkId: '', // seçili ses çıkışı ('' = sistem varsayılanı)
  outputs: [],
  quitting: false
};

let win = null;
let tray = null;

// Aynı anda iki kopya açılmasın; ikinci tıklama mevcut pencereye gitsin.
const gotLock = app.requestSingleInstanceLock();
if (!gotLock) app.quit();
app.on('second-instance', () => showWindow());

/* --- simge (tray) için PNG üretiyoruz ---------------------------------
   Dosya taşımıyoruz; küçük bir PNG'yi kodun içinde çizip data URL
   olarak veriyoruz: koyu yuvarlak kare üstünde lime oynat üçgeni. */
function crc32(buf) {
  if (!crc32.table) {
    crc32.table = new Int32Array(256);
    for (let n = 0; n < 256; n++) {
      let c = n;
      for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
      crc32.table[n] = c;
    }
  }
  let crc = -1;
  for (let i = 0; i < buf.length; i++) crc = crc32.table[(crc ^ buf[i]) & 0xff] ^ (crc >>> 8);
  return (crc ^ -1) >>> 0;
}

function pngChunk(type, data) {
  const buf = Buffer.alloc(12 + data.length);
  buf.writeUInt32BE(data.length, 0);
  buf.write(type, 4, 'ascii');
  data.copy(buf, 8);
  buf.writeUInt32BE(crc32(buf.subarray(4, 8 + data.length)), 8 + data.length);
  return buf;
}

function inRoundedBox(x, y, w, h, r) {
  if (x < 0 || y < 0 || x >= w || y >= h) return false;
  const cx = x < r ? r : x >= w - r ? w - r - 1 : x;
  const cy = y < r ? r : y >= h - r ? h - r - 1 : y;
  const dx = x - cx;
  const dy = y - cy;
  return dx * dx + dy * dy <= r * r;
}

function inTriangle(px, py, ax, ay, bx, by, cx, cy) {
  const d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
  if (!d) return false;
  const a = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / d;
  const b = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / d;
  const c = 1 - a - b;
  return a >= 0 && b >= 0 && c >= 0;
}

function iconImage(size) {
  const stride = size * 4 + 1;
  const raw = Buffer.alloc(stride * size);
  const r = Math.max(2, Math.round(size * 0.24));
  for (let y = 0; y < size; y++) {
    const row = y * stride;
    raw[row] = 0; // filtre: yok
    for (let x = 0; x < size; x++) {
      const o = row + 1 + x * 4;
      const pad = size * 0.08;
      const square = inRoundedBox(x - pad, y - pad, size - pad * 2, size - pad * 2, r);
      const tri = inTriangle(
        x + 0.5, y + 0.5,
        size * 0.40, size * 0.28,
        size * 0.40, size * 0.72,
        size * 0.76, size * 0.50
      );
      if (square && !tri) { // lime kare, ortasında delik üçgen
        raw[o] = 198; raw[o + 1] = 242; raw[o + 2] = 78; raw[o + 3] = 255;
      } else if (square) {
        raw[o] = 10; raw[o + 1] = 8; raw[o + 2] = 16; raw[o + 3] = 255;
      } else {
        raw[o] = 0; raw[o + 1] = 0; raw[o + 2] = 0; raw[o + 3] = 0;
      }
    }
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(size, 0);
  ihdr.writeUInt32BE(size, 4);
  ihdr[8] = 8; // bit derinliği
  ihdr[9] = 6; // RGBA
  const png = Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    pngChunk('IHDR', ihdr),
    pngChunk('IDAT', zlib.deflateSync(raw)),
    pngChunk('IEND', Buffer.alloc(0))
  ]);
  return nativeImage.createFromDataURL('data:image/png;base64,' + png.toString('base64'));
}

/* --- sayfa içi çalıştırılan küçük parçalar -----------------------------
   Cihaz listesi ve test sesi tarayıcı tarafında; sayfaya script olarak
   gönderiyoruz. Node bağlamı yok, hepsi kendi içinde try/catch. */
const DEVICES_SNIPPET = `(async () => {
  try {
    if (navigator.mediaDevices && navigator.mediaDevices.getUserMedia) {
      try { await navigator.mediaDevices.getUserMedia({ audio: true }); } catch (e) {}
    }
    const list = await navigator.mediaDevices.enumerateDevices();
    return list.filter((d) => d.kind === 'audiooutput').map((d) => ({ id: d.deviceId || '', label: d.label || '' }));
  } catch (e) { return []; }
})()`;

function toneSnippet(sinkId) {
  return `(async () => {
    try {
      const sr = 44100, freq = 528, dur = 0.9;
      const n = Math.round(sr * dur);
      const buf = new Uint8Array(44 + n * 2);
      const dv = new DataView(buf.buffer);
      const str = (o, s) => { for (let i = 0; i < s.length; i++) dv.setUint8(o + i, s.charCodeAt(i)); };
      str(0, 'RIFF'); dv.setUint32(4, 36 + n * 2, true); str(8, 'WAVE'); str(12, 'fmt ');
      dv.setUint32(16, 16, true); dv.setUint16(20, 1, true); dv.setUint16(22, 1, true);
      dv.setUint32(24, sr, true); dv.setUint32(28, sr * 2, true);
      dv.setUint16(32, 2, true); dv.setUint16(34, 16, true);
      str(36, 'data'); dv.setUint32(40, n * 2, true);
      for (let i = 0; i < n; i++) {
        const t = i / sr;
        const env = Math.min(1, i / (sr * 0.02), (n - i) / (sr * 0.25));
        dv.setInt16(44 + i * 2, Math.round(Math.sin(2 * Math.PI * freq * t) * 0.35 * env * 32767), true);
      }
      let bin = '';
      for (let i = 0; i < buf.length; i++) bin += String.fromCharCode(buf[i]);
      const el = document.createElement('audio');
      el.src = 'data:audio/wav;base64,' + btoa(bin);
      (document.body || document.documentElement).appendChild(el);
      const sink = ${JSON.stringify(sinkId)};
      if (sink && el.setSinkId) { try { await el.setSinkId(sink); } catch (e) {} }
      document.querySelectorAll('audio, video').forEach((m) => {
        if (sink && m.setSinkId) { try { m.setSinkId(sink); } catch (e) {} }
      });
      await el.play();
      el.addEventListener('ended', () => el.remove());
      return true;
    } catch (e) { return false; }
  })()`;
}

function run(pageCode) {
  if (!win || win.isDestroyed()) return Promise.resolve(null);
  return win.webContents.executeJavaScript(pageCode, true).catch(() => null);
}

/* --- ses çıkışı (kulaklık) ------------------------------------------- */
async function refreshDevices(quiet) {
  const list = (await run(DEVICES_SNIPPET)) || [];
  state.outputs = list.map((d, i) => ({ id: d.id, label: d.label || 'çıkış ' + (i + 1) }));
  if (!state.outputs.some((d) => d.id === state.sinkId)) state.sinkId = '';
  setMenu();
  if (!quiet) {
    const n = state.outputs.length;
    toast('Ses çıkışı', n ? n + ' cihaz bulundu.' : 'Cihaz listesi boş geldi (izin verilmemiş olabilir).');
  }
  return state.outputs;
}

async function testTone() {
  const ok = await run(toneSnippet(state.sinkId));
  if (!ok) toast('Ses çalınamadı', 'Tarayıcı ses oynatmayı engelledi.', 'warning');
}

function setSink(id) {
  state.sinkId = id;
  const bulunan = state.outputs.find((d) => d.id === id);
  setMenu();
  // seçince hemen denesin: kulaklığın gerçekten o çıkış olduğunu anlasın
  run(toneSnippet(id)).then((ok) => {
    if (!ok) toast('Ses çalınamadı', 'Cihaz seçilmedi mi yoksa tarayıcı engelledi mi?', 'warning');
    else if (bulunan) toast('Ses çıkışı', bulunan.label);
  });
}

function setMuted(muted) {
  state.muted = muted;
  if (win) win.webContents.setAudioMuted(muted);
  setMenu();
}

/* --- pencere / simge -------------------------------------------------- */
function showWindow() {
  if (!win) return;
  if (win.isMinimized()) win.restore();
  win.show();
  win.focus();
}

function ensureTray() {
  if (tray || !win) return;
  tray = new Tray(iconImage(32));
  tray.setToolTip('KickForge · yayın kontrol');
  tray.setContextMenu(
    Menu.buildFromTemplate([
      { label: 'pencereyi göster', click: showWindow },
      { label: 'pencereyi gizle', click: () => win && win.hide() },
      { type: 'separator' },
      { label: 'çıkış', click: () => { state.quitting = true; app.quit(); } }
    ])
  );
  tray.on('click', () => (win && win.isVisible() ? win.hide() : showWindow()));
}

function setTrayMode(on) {
  state.trayMode = on;
  if (on) ensureTray();
  else {
    if (tray) { tray.destroy(); tray = null; }
    showWindow();
  }
  setMenu();
}

function mediaStatus(kind) {
  try { return systemPreferences.getMediaAccessStatus(kind); } catch (e) { return 'unknown'; }
}

const STATUS_TR = {
  granted: 'izinli',
  denied: 'engelli',
  'not-determined': 'sorulmadı',
  restricted: 'kısıtlı',
  unknown: 'bilinmiyor'
};

function askPermission(kind) {
  const kod = kind === 'microphone' ? 'audio' : 'video';
  run(
    `(async () => {
      try {
        const s = await navigator.mediaDevices.getUserMedia({ ${kod}: true });
        s.getTracks().forEach((t) => t.stop());
        return 'ok';
      } catch (e) { return (e && e.name) || 'hata'; }
    })()`
  ).then((sonuc) => {
    const etiket = kind === 'microphone' ? 'Mikrofon' : 'Kamera';
    if (sonuc === 'ok') toast(etiket, 'izin verildi · durum: ' + (STATUS_TR[mediaStatus(kind)] || mediaStatus(kind)));
    else toast(etiket, 'verilmedi (' + (sonuc || 'tarayıcı engelledi') + ')', 'warning');
    setMenu();
  });
}

function toast(baslik, mesaj, tip) {
  const opts = { type: tip || 'info', title: baslik, message: baslik, detail: mesaj, buttons: ['tamam'] };
  if (win && !win.isDestroyed()) dialog.showMessageBox(win, opts);
  else dialog.showMessageBox(opts);
}

function showAbout() {
  dialog.showMessageBox(win, {
    type: 'info',
    title: 'KickForge masaüstü',
    message: 'KickForge · masaüstü sürümü',
    detail: [
      'sürüm ' + app.getVersion(),
      'kaynak ' + SITE,
      '',
      'bu kabukta olan ama sitede olmayanlar:',
      '· menüden kulaklık / ses çıkışı seçimi + test sesi',
      '· her zaman üstte (Ctrl+Shift+T)',
      '· kapatınca simgede kalma (Ctrl+Shift+B)',
      '· mikrofon ve kamera izin durumu',
      '· tam ekran (F11), araçlar (F12), gizli sekme yok',
      '',
      'internet yoksa paketin içindeki kopya açılıyor.'
    ].join('\n'),
    buttons: ['tamam']
  });
}

/* --- menü ------------------------------------------------------------ */
function setMenu() {
  if (!win) return;

  const ciktilar = [{ id: '', label: 'sistem varsayılanı' }].concat(state.outputs);
  const sesMenu = ciktilar.map((d) => ({
    label: d.label,
    type: 'radio',
    checked: state.sinkId === d.id,
    click: () => setSink(d.id)
  }));
  sesMenu.push({ type: 'separator' });
  sesMenu.push({ label: 'cihaz listesini tazele', click: () => refreshDevices(false) });

  const menu = Menu.buildFromTemplate([
    {
      label: 'KickForge',
      submenu: [
        {
          label: 'her zaman üstte',
          type: 'checkbox',
          checked: state.alwaysOnTop,
          accelerator: 'CmdOrCtrl+Shift+T',
          click: (item) => { state.alwaysOnTop = item.checked; win.setAlwaysOnTop(item.checked); setMenu(); }
        },
        {
          label: 'kapatınca simgede kalsın',
          type: 'checkbox',
          checked: state.trayMode,
          accelerator: 'CmdOrCtrl+Shift+B',
          click: (item) => setTrayMode(item.checked)
        },
        { type: 'separator' },
        { label: 'yenile', accelerator: 'CmdOrCtrl+R', click: () => win.webContents.reload() },
        { label: 'geliştirici araçları', accelerator: 'F12', click: () => win.webContents.toggleDevTools() },
        { type: 'separator' },
        { label: 'çıkış', role: 'quit' }
      ]
    },
    {
      label: 'ses',
      submenu: [
        { label: 'ses çıkışı (kulaklık)', submenu: sesMenu },
        { label: 'test sesi çal', accelerator: 'CmdOrCtrl+Shift+S', click: testTone },
        {
          label: 'sessize al',
          type: 'checkbox',
          checked: state.muted,
          accelerator: 'CmdOrCtrl+Shift+M',
          click: (item) => setMuted(item.checked)
        },
        { type: 'separator' },
        { label: 'sayfayı yeniden başlat', click: () => { win.webContents.reload(); refreshDevices(true); } }
      ]
    },
    {
      label: 'sistem',
      submenu: [
        { label: 'mikrofon: ' + (STATUS_TR[mediaStatus('microphone')] || mediaStatus('microphone')), enabled: false },
        { label: 'kamera: ' + (STATUS_TR[mediaStatus('camera')] || mediaStatus('camera')), enabled: false },
        { type: 'separator' },
        { label: 'mikrofon izni iste', click: () => askPermission('microphone') },
        { label: 'kamera izni iste', click: () => askPermission('camera') }
      ]
    },
    {
      label: 'görünüm',
      submenu: [
        {
          label: 'tam ekran',
          type: 'checkbox',
          checked: win.isFullScreen(),
          accelerator: 'F11',
          click: (item) => win.setFullScreen(item.checked)
        },
        { type: 'separator' },
        { label: 'yakınlaştır', role: 'zoomIn' },
        { label: 'uzaklaştır', role: 'zoomOut' },
        { label: 'yakınlaştırmayı sıfırla', role: 'resetZoom' }
      ]
    },
    {
      label: 'yardım',
      submenu: [
        { label: "kickforge'yi tarayıcıda aç", click: () => shell.openExternal(SITE) },
        { label: 'hakkında', click: showAbout }
      ]
    }
  ]);

  Menu.setApplicationMenu(menu);
}

/* --- pencere ---------------------------------------------------------- */
function createWindow() {
  win = new BrowserWindow({
    width: 1460,
    height: 920,
    minWidth: 360,
    minHeight: 560,
    backgroundColor: '#0a0810',
    show: false, // sayfa boyanınca aç; açılışta beyaz ekran flaşı olmasın
    title: 'KickForge · mor tilki yayın kontrol',
    autoHideMenuBar: false, // masaüstü özellikleri menü çubuğunda dursun (Alt ile değil)
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      spellcheck: false
    }
  });

  const hedef = process.env.KICKFORGE_URL || SITE;

  win.loadURL(hedef).catch(() => {
    console.warn('[KickForge] canlı site açılamadı, çevrimdışı kopyaya geçiliyor.');
    win.loadFile(OFFLINE_COPY);
  });

  win.once('ready-to-show', () => {
    win.show();
    refreshDevices(true);
  });

  // hedef="_blank" ile açılan her şey (kick kanal sayfası, arşiv vb.)
  // varsayılan tarayıcıda açılsın; uygulama içinde ikinci pencere açılmasın.
  win.webContents.setWindowOpenHandler(({ url }) => {
    if (url.startsWith('https://') || url.startsWith('http://')) {
      shell.openExternal(url);
    }
    return { action: 'deny' };
  });

  win.on('close', (e) => {
    if (state.trayMode && !state.quitting) {
      e.preventDefault(); // simgede kalsın
      win.hide();
    }
  });

  setMenu();
  return win;
}

app.whenReady().then(() => {
  if (!gotLock) return;

  // mikrofon/kamera izinlerini kabul edelim; soruyu Windows'un kendisi sorar
  session.defaultSession.setPermissionRequestHandler((wc, permission, callback) => {
    callback(['media', 'fullscreen'].indexOf(permission) !== -1);
  });
  session.defaultSession.setPermissionCheckHandler((wc, permission) => ['media'].indexOf(permission) !== -1);

  createWindow();

  // macOS'te pencere kapanınca tıklamayla geri gelsin.
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
    else showWindow();
  });
});

app.on('before-quit', () => { state.quitting = true; });

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
