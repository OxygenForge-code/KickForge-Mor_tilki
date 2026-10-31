#!/usr/bin/env node
/* Windows paketi.

   electron-packager bu ortamda sessizce çıkıyordu (hiç hata vermeden dist
   üretmiyordu), o yüzden paketin ne yaptığı belli olsun diye betiği kendim
   yazdım:

     1. ../index.html'in güncel kopyasını bu klasöre al
     2. electron windows zip'ini indir (yoksa)
     3. zip'i dist/KickForge-win32-x64 içine aç  (linux: unzip, windows: tar)
     4. electron.exe  ->  KickForge.exe
     5. main.js + index.html'i resources/app.asar içine koy
     6. makensis varsa tek dosya dist/KickForge.exe üret (CI bunu yayimlar)
     7. büyüklüğü ve dosya yolunu raporla

   Windows'a özel ikon/versiyon bilgisi yazılmıyor; onun için wine + rcedit
   gerekiyor, bu ortamda yok. Uygulama çalışan bir .exe olarak çıkıyor.
*/
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const VERSION = '33.4.11';
const ZIP_NAME = `electron-v${VERSION}-win32-x64.zip`;
const ZIP_URL = `https://github.com/electron/electron/releases/download/v${VERSION}/${ZIP_NAME}`;
const CACHE = path.join(__dirname, '.electron-cache', ZIP_NAME);
const OUT = path.join(__dirname, 'dist', 'KickForge-win32-x64');
const INSTALLER_OUT = path.join(__dirname, 'dist', 'KickForge.exe');

const log = (...a) => console.log('[build]', ...a);

function sizeOf(dir) {
  let total = 0;
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, entry.name);
    total += entry.isDirectory() ? sizeOf(p) : fs.statSync(p).size;
  }
  return total;
}

function human(bytes) {
  const mb = bytes / (1024 * 1024);
  return mb >= 1024 ? (mb / 1024).toFixed(2) + ' GB' : mb.toFixed(1) + ' MB';
}

/* makensis'i bul: PATH, sonra windows'ta bilinen kurulum yeri */
function findMakensis() {
  const adaylar = ['makensis'];
  if (process.platform === 'win32') {
    adaylar.push(
      'C:\\Program Files (x86)\\NSIS\\makensis.exe',
      'C:\\Program Files\\NSIS\\makensis.exe'
    );
  }
  for (const aday of adaylar) {
    try {
      execFileSync(aday, ['/VERSION'], { stdio: 'ignore' });
      return aday;
    } catch (e) {
      try {
        execFileSync(aday, ['-VERSION'], { stdio: 'ignore' });
        return aday;
      } catch (e2) { /* yok, sıradaki */ }
    }
  }
  return null;
}

async function main() {
  // 1) site sayfasının güncel kopyası
  execFileSync(process.execPath, [path.join(__dirname, 'sync.js')], { stdio: 'inherit' });

  // 2) electron zip'i
  fs.mkdirSync(path.dirname(CACHE), { recursive: true });
  if (!fs.existsSync(CACHE) || fs.statSync(CACHE).size < 100 * 1024 * 1024) {
    log('electron indiriliyor...');
    const args = ['-L', '--retry', '3', '--retry-delay', '2', '-sS', '-o', CACHE, ZIP_URL];
    if (fs.existsSync(CACHE)) args.unshift('-C', '-');
    execFileSync('curl', args, { stdio: 'inherit' });
  } else {
    log("electron zip'i önbellekte:", human(fs.statSync(CACHE).size));
  }

  // 3) aç
  // extract-zip bu ortamda ilk girdiden sonra sessizce çıkıyordu (hiç hata
  // vermeden) — sistemdekini kullanıyoruz: linux'ta unzip, windows'ta tar.
  fs.rmSync(path.join(__dirname, 'dist'), { recursive: true, force: true });
  fs.mkdirSync(OUT, { recursive: true });
  log('zip açılıyor...');
  const acici =
    process.platform === 'win32'
      ? ['tar', ['-xf', CACHE, '-C', OUT]]
      : ['unzip', ['-q', '-o', CACHE, '-d', OUT]];
  execFileSync(acici[0], acici[1], { stdio: 'inherit' });
  const acilanGirdi = fs.readdirSync(OUT).length;
  if (acilanGirdi < 10) throw new Error('zip eksik açıldı (' + acilanGirdi + ' girdi)');

  // 4) exe
  const electronExe = path.join(OUT, 'electron.exe');
  if (!fs.existsSync(electronExe)) throw new Error('electron.exe bulunamadı: ' + OUT);
  const exe = path.join(OUT, 'KickForge.exe');
  fs.renameSync(electronExe, exe);

  // 5) uygulama dosyaları
  const resources = path.join(OUT, 'resources');
  fs.rmSync(path.join(resources, 'default_app.asar'), { force: true });

  const appDir = path.join(resources, 'app');
  fs.mkdirSync(appDir, { recursive: true });
  fs.writeFileSync(
    path.join(appDir, 'package.json'),
    JSON.stringify({ name: 'kickforge-desktop', productName: 'KickForge', version: '1.0.0', main: 'main.js' }, null, 2)
  );
  fs.copyFileSync(path.join(__dirname, 'main.js'), path.join(appDir, 'main.js'));
  fs.copyFileSync(path.join(__dirname, 'index.html'), path.join(appDir, 'index.html'));

  const asar = require('@electron/asar');
  await asar.createPackage(appDir, path.join(resources, 'app.asar'));
  fs.rmSync(appDir, { recursive: true, force: true });

  log('exe:', exe);
  log('exe boyutu:', human(fs.statSync(exe).size));
  log('paket toplamı:', human(sizeOf(OUT)));

  // 6) tek dosya .exe (makensis yoksa bu ortamda atlanır; CI'da var)
  const makensis = findMakensis();
  if (!makensis) {
    log('makensis yok → tek dosya kurucu bu ortamda üretilmedi (CI derler)');
    return;
  }
  const surum = require('./package.json').version + '-' + new Date().toISOString().replace(/[-:T]/g, '').slice(0, 12);
  const onEk = process.platform === 'win32' ? '/D' : '-D';
  log('tek dosya .exe derleniyor (sürüm ' + surum + ')...');
  execFileSync(makensis, [onEk + 'VERSION=' + surum, 'installer.nsi'], {
    cwd: __dirname,
    stdio: 'inherit'
  });
  if (!fs.existsSync(INSTALLER_OUT)) throw new Error('installer çıktısı yok: ' + INSTALLER_OUT);
  log('tek dosya exe:', INSTALLER_OUT, human(fs.statSync(INSTALLER_OUT).size));
}

main().catch((err) => {
  console.error('[build] HATA:', err && err.stack ? err.stack : err);
  process.exit(1);
});
