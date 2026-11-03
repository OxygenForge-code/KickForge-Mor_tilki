/* KickForge · Tauri — uygulama döngüsü */
const { invoke } = window.__TAURI__.core;
const curWin = window.__TAURI__.window.getCurrentWindow();

const $ = (id) => document.getElementById(id);

const APP_VERSION = "2.4.0";
let S = {};                  // ayarlar
let nulledNotif = false;     // toptan susturma
let lastStats = null;
let lastPollMs = 0;
let pollTimer = null;
let chatTimer = null;
let eventIdShort = 0;
let samples = [];            // {t, viewers, followers, live}
let chatSlugInfo = null;

const isVal = (v) => v != null && v !== "";
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

/* ═══════════ sekmeler ═══════════ */
function show(page) {
  document.querySelectorAll(".page").forEach((p) => p.classList.toggle("active", p.id === "page-" + page));
  document.querySelectorAll(".tab").forEach((t) => t.classList.toggle("active", t.dataset.page === page));
  if (page === "studio") startChatLoop();
  if (page === "history") renderHistory();
}

document.querySelectorAll(".tab").forEach((t) => t.addEventListener("click", () => show(t.dataset.page)));

/* ═══════════ pencere ═══════════ */
$("btnMin").addEventListener("click", () => curWin.minimize());
$("btnMax").addEventListener("click", async () => (await curWin.isMaximized()) ? curWin.unmaximize() : curWin.toggleMaximize());
$("btnClose").addEventListener("click", async () => {
  if (S?.minimizeToTray === true) { curWin.hide(); return; }
  curWin.close();
});
$("btnGear").addEventListener("click", () => show("settings"));
$("btnPin").addEventListener("click", async () => {
  const on = !(await curWin.isAlwaysOnTop());
  await curWin.setAlwaysOnTop(on);
  $("btnPin").style.color = on ? "var(--accent)" : "";
  S = { ...S, alwaysOnTop: on };
  saveSettings();
});

/* dock sheen fareyi takip eder */
$("dock").addEventListener("mousemove", (e) => {
  const r = e.currentTarget.getBoundingClientRect();
  e.currentTarget.style.setProperty("--sheen-x", ((e.clientX - r.left) / r.width) * 100 + "%");
});
$("dock").addEventListener("mouseleave", () => $("dock").style.setProperty("--sheen-x", "50%"));

/* dış bağlantı: opener eklentisi */
function openUrl(url) {
  if (window.__TAURI__?.opener) window.__TAURI__.opener.openUrl(url);
}
$("lnkSite").addEventListener("click", () => openUrl("https://kickforge-mor-tilki.vercel.app"));
$("lnkGithub").addEventListener("click", () => openUrl("https://github.com/OxygenForge-code/KickForge-Mor_tilki"));
$("btnGithub").addEventListener("click", () => openUrl("https://github.com/OxygenForge-code/KickForge-Mor_tilki"));

/* ═══════════ ayarlar ═══════════ */
async function loadSettings() {
  try { S = await invoke("get_settings"); }
  catch { S = {}; }
  S.apiBase ||= "https://kickforge-mor-tilki.vercel.app";
  S.channelSlug ||= "mor-tilki";
  S.pollSeconds ||= 20;
  // alanlara bağla
  $("setSlug").value = S.channelSlug;
  $("setPoll").value = S.pollSeconds;
  $("setHistory").value = S.historyMinutes || 180;
  $("setDirect").checked = S.allowDirect !== false;
  $("setNotifyLive").checked = S.notifyLive !== false;
  $("setNotifyFollower").checked = S.notifyFollower !== false;
  $("setNotifySpike").checked = S.notifyViewerSpike !== false;
  $("setNotifyRecover").checked = S.notifyRecover !== false;
  $("setQuiet").checked = !!S.quietHours;
  $("setOnTop").checked = !!S.alwaysOnTop;
  $("setTray").checked = S.minimizeToTray !== false;
  $("setAutoStart").checked = !!S.startWithWindows;
  if (S.alwaysOnTop) await curWin.setAlwaysOnTop(true);
  $("brandChannel").textContent = "· " + S.channelSlug;
  $("dockName").textContent = S.channelSlug;
}

let saveT = null;
function saveSettings() {
  clearTimeout(saveT);
  saveT = setTimeout(async () => {
    try { await invoke("save_settings", { settings: S }); } catch (e) { status("ayarlar kaydedilemedi", "err"); }
  }, 350);
}

function bindSetting(id, key, transform = (v) => v) {
  $(id).addEventListener("change", () => {
    S[key] = transform($(id).checked ?? $(id).value);
    saveSettings();
  });
}
bindSetting("setSlug", "channelSlug", () => String($("setSlug").value.trim() || "mor-tilki").toLowerCase());
bindSetting("setPoll", "pollSeconds", (v) => Math.min(120, Math.max(5, Number($("setPoll").value) || 20)));
bindSetting("setHistory", "historyMinutes", () => Math.min(1440, Math.max(30, Number($("setHistory").value) || 180)));
bindSetting("setDirect", "allowDirect");
bindSetting("setNotifyLive", "notifyLive");
bindSetting("setNotifyFollower", "notifyFollower");
bindSetting("setNotifySpike", "notifyViewerSpike");
bindSetting("setNotifyRecover", "notifyRecover");
bindSetting("setQuiet", "quietHours");
bindSetting("setOnTop", "alwaysOnTop", async () => {
  const on = $("setOnTop").checked;
  await curWin.setAlwaysOnTop(on);
  return on;
});
bindSetting("setTray", "minimizeToTray");
bindSetting("setAutoStart", "startWithWindows");

/* такdır zinciri yok: kanal değişince örnekler ve kimlik sıfırlanır */
$("setSlug").addEventListener("change", () => {
  samples = [];
  chatSlugInfo = null;
  pollNow(true);
});

/* ═══════════ durum çubuğu ═══════════ */

function status(text, kind = "") {
  $("statusText").textContent = text;
  $("statusDot").className = "dot " + kind;
  const dot = $("stateDot");
  dot.className = "dot " + kind;
  $("stateText").textContent = kind === "live" ? "CANLI" : kind === "err" ? "HATA" : kind === "warn" ? "GEÇİKMİŞ" : "ÇEVRİMİÇİ";
}

/* ═══════════ istatistik döngüsü ═══════════ */
async function pollNow(first = false) {
  const slug = S.channelSlug || "mor-tilki";
  const started = Date.now();
  try {
    const st = await invoke("get_stats", { apiBase: S.apiBase, slug, allowDirect: S.allowDirect !== false });

    lastStats = st;
    const viewers = Number(st.viewerCount) || 0;
    const live = !!st.isLive;

    // hero
    $("viewerNum").textContent = viewers.toLocaleString("tr-TR");
    $("sessionTitle").textContent = st.sessionTitle || (live ? "başlık yok" : "yayın kapalı");
    $("heroFollowers").textContent = (Number(st.followersCount) || 0).toLocaleString("tr-TR");
    $("mFollowers").textContent = (Number(st.followersCount) || 0).toLocaleString("tr-TR");
    $("mSession").textContent = live ? "canlı" : "kapalı";
    $("mSource").textContent = st.source || "site";
    $("mLatency").textContent = (Date.now() - started) + " ms";
    $("mUpdated").textContent = new Date().toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit", second: "2-digit" });
    $("mChannelId").textContent = st.channelId ? "#" + st.channelId : "—";

    $("heroDot").className = "dot " + (live ? "live" : "");
    $("liveDot").className = "dot " + (live ? "live" : "");
    $("sideLiveDot").className = "dot " + (live ? "live" : "");
    $("sideLiveText").textContent = live ? "YAYINDA" : "yayın kapalı";
    status(live ? "bağlandı · yayın açık" : "bağlandı · yayın kapalı", live ? "live" : "");

    // dock
    $("dockMeta").textContent = `${(Number(st.followersCount) || 0).toLocaleString("tr-TR")} takipçi · ${st.source ? (st.source === "site" ? "site ucu" : "kick.com") : ""}`;
    $("verifiedChip").style.display = st.verified ? "inline-flex" : "none";
    if (st.profilePic && $("avatarImg").getAttribute("src") !== st.profilePic) $("avatarImg").src = st.profilePic;

    // stale bar
    if (st.stale) {
      $("staleBar").style.display = "block";
      const pct = Math.min(100, (Date.now() - (lastPollMs || Date.now())) / 60);
      $("staleFill").style.width = Math.max(15, pct) + "%";
      $("heroDot").className = "dot warn";
    } else {
      $("staleBar").style.display = "none";
    }

    // kanal no al (kick ucu)
    const cs = await invoke("get_chat", { apiBase: S.apiBase, slug: slug }).catch(() => null);
    if (cs && cs.channelId) {
      $("mChannelId").textContent = "#" + cs.channelId;
      chatSlugInfo = cs;
    }
    // örnek
    const sample = {
      t: Date.now(),
      viewers,
      followers: Number(st.followersCount) || 0,
      live: live ? 1 : 0
    };
    samples.push(sample);
    if (samples.length > 320) samples = samples.slice(-320);
    try {
      await invoke("add_history", { sample, keep_minutes: S.historyMinutes || 180 });
    } catch {}
    drawSpark();

    lastPollMs = Date.now();
  } catch (e) {
    status("veri gelmedi · " + String(e).slice(0, 80), "err");
    $("sessionTitle").textContent = "veri okunamadı";
  }
}

/* bildirim: toptan susturma butonu (css bileşeni) */
$("btnNotif").addEventListener("click", () => {
  nulledNotif = !nulledNotif;
  $("btnNotif").classList.toggle("muted", nulledNotif);
  $("notifLabel").textContent = nulledNotif ? "bildirimler suslu" : "bildirimler açık";
  $("notifHint").textContent = nulledNotif ? "bu oturum için kapatıldı" : "yayın açılınca ve takipçi değişince uyarır";
});

/* dosya ilerisi: bildirim çıkarma (basit toast yoksa native alert yerine inline) */
function notice(msg, err = false) {
  $("chatNote").textContent = msg;
  $("chatNote").style.color = err ? "var(--danger)" : "var(--dim)";
}

/* ═══════════ eğri ═══════════ */
function drawSpark() {
  const c = $("spark");
  const dpr = window.devicePixelRatio || 1;
  const w = c.clientWidth || 600;
  const h = 96;
  c.width = w * dpr; c.height = h * dpr;
  const ctx = c.getContext("2d");
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, w, h);

  const recent = samples.slice(-140);
  const vals = recent.map((s) => s.viewers);
  const peak = Math.max(0, ...vals);
  const avg = vals.length ? Math.round(vals.reduce((a, b) => a + b, 0) / vals.length) : 0;
  $("peakText").textContent = peak.toLocaleString("tr-TR");
  $("avgText").textContent = avg.toLocaleString("tr-TR");
  $("sampleCount").textContent = samples.length + " örnek";

  const maxV = Math.max(1, peak);
  ctx.beginPath();
  recent.forEach((s, i) => {
    const x = (i / Math.max(1, recent.length - 1)) * (w - 8) + 4;
    const y = h - 8 - (s.viewers / maxV) * (h - 20);
    i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y);
  });
  ctx.strokeStyle = "#c6f24e";
  ctx.lineWidth = 2;
  ctx.lineJoin = "round";
  ctx.stroke();

  // alan
  ctx.lineTo(w - 4, h); ctx.lineTo(4, h); ctx.closePath();
  const g = ctx.createLinearGradient(0, 0, 0, h);
  g.addColorStop(0, "rgba(198,242,78,0.22)");
  g.addColorStop(1, "rgba(198,242,78,0)");
  ctx.fillStyle = g;
  ctx.fill();
}

/* ═══════════ geçmiş ═══════════ */
async function renderHistory() {
  let rows = [];
  try { rows = await invoke("get_history"); } catch {}
  const list = $("histList");
  list.innerHTML = "";
  if (!rows || !rows.length) {
    list.innerHTML = '<div class="hist-empty">henüz örnek yok — uygulama çalıştıkça burada birikir</div>';
    return;
  }
  // olayları türet: canlı açılış/kapanış ve takipçi artışı
  let prev = null;
  const events = [];
  for (const s of rows) {
    if (prev) {
      if (s.live && !prev.live) events.push({ t: s.t, kind: "up", text: "yayın açıldı" });
      if (!s.live && prev.live) events.push({ t: s.t, kind: "down", text: "yayın kapandı" });
      if (s.followers > prev.followers) events.push({ t: s.t, kind: "follow", text: `+${s.followers - prev.followers} takipçi` });
      if (prev.viewers && s.viewers > prev.viewers * 1.3) events.push({ t: s.t, kind: "spike", text: `izleyici sıçraması · ${prev.viewers} → ${s.viewers}` });
    }
    prev = s;
  }
  events.reverse().slice(0, 120).forEach((ev) => {
    const div = document.createElement("div");
    div.className = "hist-line";
    const when = new Date(ev.t);
    const d = when.toLocaleDateString("tr-TR", { day: "2-digit", month: "2-digit" });
    const time = when.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" });
    const color = ev.kind === "up" ? "var(--accent)" : ev.kind === "down" ? "var(--danger)" : ev.kind === "spike" ? "#ffb347" : "var(--dim)";
    div.innerHTML = `<span class="hist-clock">${d} · ${time}</span><span style="width:7px;height:7px;border-radius:50%;background:${color}"></span><span class="hist-text">${esc(ev.text)}</span>`;
    list.appendChild(div);
  });
}
$("btnHistRefresh").addEventListener("click", renderHistory);

/* ═══════════ sohbet ═══════════ */
let chatLastId = "";

async function startChatLoop() {
  fetchChat();
  clearInterval(chatTimer);
  chatTimer = setInterval(fetchChat, 3500);
}

async function fetchChat() {
  const slug = S.channelSlug || "mor-tilki";
  try {
    const d = await invoke("get_chat", { apiBase: S.apiBase, slug });
    $("chatDot").className = "dot live";
    $("chatStateText").textContent = "BAĞLI";
    const feed = $("chatFeed");
    const atEnd = feed.scrollHeight - feed.scrollTop - feed.clientHeight < 60;
    feed.innerHTML = "";
    (d.messages || []).forEach((m) => appendChatLine(feed, m));
    if (atEnd) feed.scrollTop = feed.scrollHeight;
  } catch (e) {
    $("chatDot").className = "dot err";
    $("chatStateText").textContent = "YOK";
    if (String(e).includes("kanal no")) {
      $("chatFeed").innerHTML = `<div class="loading-row"><div class="loader"></div><span>kanal bilgisi çözülüyor… tekrar denemek için bekle</span></div>`;
    } else {
      $("chatFeed").innerHTML = `<div class="loading-row"><div class="loader"></div><span>${esc(String(e).slice(0, 120))}</span></div>`;
    }
  }
}

function appendChatLine(feed, m) {
  const badges = (m.badges || [])
    .map((b) => `<span class="badge ${b.level ? "level" : ""}" style="background:${b.level ? "" : esc(b.color)}22;color:${esc(b.color)}">${esc(b.text)}</span>`)
    .join("");
  const div = document.createElement("div");
  div.className = "chat-line";
  div.innerHTML =
    `<div>${badges}<span class="chat-user" style="color:${esc(m.color)}">${esc(m.user)}</span>${m.isBot ? '<span class="badge level" style="margin-left:5px">BOT</span>' : ""}</div>` +
    `<div class="chat-msg">${esc(m.content)}</div>`;
  feed.appendChild(div);
}

async function checkAuth() {
  try {
    const a = await invoke("check_auth_fresh", { apiBase: S.apiBase });
    $("authState").innerHTML = a.loggedIn
      ? `kick oturumu: <b class="mono">${esc(a.username)}</b>`
      : "kick oturumu yok";
    $("btnLogout").style.display = a.loggedIn ? "inline-flex" : "none";
    $("loginRow").style.display = a.loggedIn ? "none" : "flex";
    $("chatInputRow").style.display = a.loggedIn ? "flex" : "none";
  } catch (e) {
    $("authState").textContent = "oturum sorgulanamadı";
    $("loginRow").style.display = "flex";
    $("chatInputRow").style.display = "none";
  }
}
$("btnLogout").addEventListener("click", async () => { await invoke("logout").catch?.(() => {}); checkAuth(); });

$("btnLogin").addEventListener("click", async () => {
  $("btnLogin").textContent = "tarayıcı açılıyor…";
  try {
    const r = await invoke("start_login", { apiBase: S.apiBase });
    notice(`giriş tamam: ${r.username}`);
  } catch (e) {
    notice(String(e), true);
  }
  $("btnLogin").textContent = "Kick ile giriş yap";
  checkAuth();
});

$("btnSend").addEventListener("click", sendChat);
$("chatInput").addEventListener("keydown", (e) => { if (e.key === "Enter") sendChat(); });

async function sendChat() {
  const text = $("chatInput").value.trim();
  if (!text) return;
  $("chatInput").disabled = true;
  try {
    await invoke("send_chat", { apiBase: S.apiBase, slug: S.channelSlug, text });
    $("chatInput").value = "";
    notice("gönderildi");
  } catch (e) {
    notice(String(e), true);
  } finally {
    $("chatInput").disabled = false;
    $("chatInput").focus();
  }
}

/* ═══════════ güncelleme tostu ═══════════ */
function versionGt(a, b) {
  const pa = String(a || "").split(".").map(Number);
  const pb = String(b || "").split(".").map(Number);
  for (let i = 0; i < 3; i++) {
    if ((pa[i] || 0) > (pb[i] || 0)) return true;
    if ((pa[i] || 0) < (pb[i] || 0)) return false;
  }
  return false;
}

async function checkUpdate() {
  try {
    const r = await invoke("get_release");
    const ver = r.version || (String(r.title || "").match(/(\d+\.\d+\.\d+)/) || [])[1];
    if (ver && versionGt(ver, APP_VERSION)) {
      $("updateNote").textContent = `${r.build || ""} · ${(Number(r.size) / 1048576 || 0).toFixed(0)} mb · tek dosya, kurulum yok`;
      $("updateToast").classList.add("show");
      $("btnUpdate").addEventListener("click", () => openUrl(r.url || "https://github.com/OxygenForge-code/KickForge-Mor_tilki/releases/latest/download/KickForge.exe"));
      $("btnUpdateLater").addEventListener("click", () => $("updateToast").classList.remove("show"));
    }
    $("aboutRelease").innerHTML = `sürüm <b>${esc(ver || "—")}</b> · ${esc(r.build || "")} · tauri çekirdeği`;
  } catch (e) {
    $("aboutRelease").textContent = "sürüm sorgulanamadı";
  }
}

/* ═══════════ içerik düzeni döngüsü ═══════════ */
async function schedulePoll() {
  clearInterval(pollTimer);
  const ms = Math.max(5, Number(S.pollSeconds || 20)) * 1000;
  pollTimer = setInterval(pollNow, ms);
}

$("btnWatch").addEventListener("click", () => openUrl("https://kick.com/" + (S.channelSlug || "mor-tilki")));
$("btnChatGo").addEventListener("click", () => { show("studio"); fetchChat(); });
$("btnRefresh").addEventListener("click", () => pollNow(true));

/* ═══════════ başlat ═══════════ */
window.addEventListener("resize", drawSpark);

(async function main() {
  await loadSettings();
  checkAuth();
  checkUpdate();
  await pollNow(true);
  schedulePoll();
})();
