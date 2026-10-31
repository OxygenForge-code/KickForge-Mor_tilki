const { app, BrowserWindow, shell } = require('electron');
const path = require('path');

/* Site normalde canlı çalışıyor; internet yoksa paketin içine gömdüğümüz
   index.html kopyasına düşüyor (o da güzelden uyarıyı gösterir). */
const SITE = 'https://kickforge-mor-tilki.vercel.app/';
const OFFLINE_COPY = path.join(__dirname, 'index.html');

app.setName('KickForge');

// Aynı anda iki kopya açılmasın; ikinci tıklama mevcut pencereye gitsin.
if (!app.requestSingleInstanceLock()) {
  app.quit();
}

function createWindow() {
  const win = new BrowserWindow({
    width: 1460,
    height: 920,
    minWidth: 360,
    minHeight: 560,
    backgroundColor: '#0a0810',
    show: false, // sayfa boyanınca aç; açılışta beyaz ekran flaşı olmasın
    title: 'KickForge · mor tilki yayın kontrol',
    autoHideMenuBar: true,
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

  win.once('ready-to-show', () => win.show());

  // hedef="_blank" ile açılan her şey (kick kanal sayfası, arşiv vb.)
  // varsayılan tarayıcıda açılsın; uygulama içinde ikinci pencere açılmasın.
  win.webContents.setWindowOpenHandler(({ url }) => {
    if (url.startsWith('https://') || url.startsWith('http://')) {
      shell.openExternal(url);
    }
    return { action: 'deny' };
  });

  return win;
}

app.whenReady().then(() => {
  createWindow();

  // macOS'te pencere kapanınca tıklamayla geri gelsin.
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
