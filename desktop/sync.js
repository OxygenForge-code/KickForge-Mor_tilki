/* Paketlemeden önce site sayfasının en güncel kopyasını bu klasöre alır.
   Yani exe'deki çevrimdışı kopya, her zaman repodaki index.html ile aynı olur. */
const fs = require('fs');
const path = require('path');

const kaynak = path.join(__dirname, '..', 'index.html');
const hedef = path.join(__dirname, 'index.html');

if (!fs.existsSync(kaynak)) {
  console.error('index.html bulunamadı:', kaynak);
  process.exit(1);
}

fs.copyFileSync(kaynak, hedef);
console.log('kopyalandı:', path.relative(process.cwd(), hedef), '(' + fs.statSync(hedef).size + ' bayt)');
