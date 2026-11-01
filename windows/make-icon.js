// windows/app.ico üreticisi: koyu yuvarlak kare üstünde lime oynat üçgeni.
// tek seferlik bir araç; uygulama derlemesi bu dosyaya ihtiyaç duymaz.
const fs = require('fs');
const zlib = require('zlib');

function crc32(buf) {
  if (!crc32.t) {
    crc32.t = new Int32Array(256);
    for (let n = 0; n < 256; n++) {
      let c = n;
      for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
      crc32.t[n] = c;
    }
  }
  let c = -1;
  for (let i = 0; i < buf.length; i++) c = crc32.t[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  return (c ^ -1) >>> 0;
}

function chunk(type, data) {
  const out = Buffer.alloc(12 + data.length);
  out.writeUInt32BE(data.length, 0);
  out.write(type, 4, 'ascii');
  data.copy(out, 8);
  out.writeUInt32BE(crc32(out.subarray(4, 8 + data.length)), 8 + data.length);
  return out;
}

const inBox = (x, y, w, h, r) => {
  if (x < 0 || y < 0 || x >= w || y >= h) return false;
  const cx = x < r ? r : x >= w - r ? w - r - 1 : x;
  const cy = y < r ? r : y >= h - r ? h - r - 1 : y;
  return (x - cx) ** 2 + (y - cy) ** 2 <= r * r;
};

const inTri = (px, py, ax, ay, bx, by, cx, cy) => {
  const d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
  if (!d) return false;
  const a = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / d;
  const b = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / d;
  return a >= 0 && b >= 0 && 1 - a - b >= 0;
};

function png(size) {
  const stride = size * 4 + 1;
  const raw = Buffer.alloc(stride * size);
  const r = Math.max(2, Math.round(size * 0.22));
  const pad = size * 0.06;
  for (let y = 0; y < size; y++) {
    raw[y * stride] = 0;
    for (let x = 0; x < size; x++) {
      const o = y * stride + 1 + x * 4;
      const box = inBox(x - pad, y - pad, size - pad * 2, size - pad * 2, r);
      const tri = inTri(x + 0.5, y + 0.5, size * 0.38, size * 0.26, size * 0.38, size * 0.74, size * 0.74, size * 0.5);
      const glow = inBox(x - pad, y - pad, size - pad * 2, size - pad * 2, r) ? 1 : 0;
      if (!box) {
        raw[o] = raw[o + 1] = raw[o + 2] = raw[o + 3] = 0;
      } else if (tri) {
        raw[o] = 12; raw[o + 1] = 10; raw[o + 2] = 18; raw[o + 3] = 255;
      } else if (glow) {
        raw[o] = 198; raw[o + 1] = 242; raw[o + 2] = 78; raw[o + 3] = 255;
      }
    }
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(size, 0);
  ihdr.writeUInt32BE(size, 4);
  ihdr[8] = 8;
  ihdr[9] = 6;
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', zlib.deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0))
  ]);
}

const sizes = [16, 32, 48, 64, 128, 256];
const images = sizes.map(png);
const header = Buffer.alloc(6 + sizes.length * 16);
header.writeUInt16LE(0, 0);
header.writeUInt16LE(1, 2);
header.writeUInt16LE(sizes.length, 4);
let offset = header.length;
sizes.forEach((size, i) => {
  const e = 6 + i * 16;
  header[e] = size >= 256 ? 0 : size;
  header[e + 1] = size >= 256 ? 0 : size;
  header[e + 2] = 0;
  header[e + 3] = 0;
  header.writeUInt16LE(1, e + 4);
  header.writeUInt16LE(32, e + 6);
  header.writeUInt32LE(images[i].length, e + 8);
  header.writeUInt32LE(offset, e + 12);
  offset += images[i].length;
});
fs.writeFileSync(__dirname + '/app.ico', Buffer.concat([header, ...images]));
console.log('app.ico yazildi:', offset, 'bayt');
