/* KickForge · Tauri — uygulama döngüsü */
const { invoke } = window.__TAURI__.core;
const curWin = window.__TAURI__.window.getCurrentWindow();

const $ = (id) => document.getElementById(id);

const APP_VERSION = "2.7.0";
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

/* ═══════════ yayın oynatıcı ═══════════ */
let playerOn = false;
let playerAutoTried = false;
let playerStartedAt = 0;
let miniDismissed = false;

function playerEnsure(on) {
  playerOn = on;
  if (on && !playerStartedAt) { playerStartedAt = Date.now(); miniDismissed = false; }
  if (!on) playerStartedAt = 0;
  const box = $("playerBox");
  const frame = $("playerFrame");
  box.classList.toggle("playing", on);
  $("btnPlayerToggle").textContent = on ? "oynatıcıyı kapat" : "oynatıcıyı aç";
  if (on) {
    frame.src = "https://player.kick.com/" + (S.channelSlug || "mor-tilki");
    if (frame.style.display !== "block") {
      frame.style.display = "block";
      $("playerStateText").textContent = "AÇIK";
    }
  } else {
    frame.src = "about:blank";
    setTimeout(() => { if (!playerOn) frame.style.display = "none"; }, 150);
    $("playerStateText").textContent = "hazır";
  }
}

$("btnPlayerToggle").addEventListener("click", () => playerEnsure(!playerOn));
$("btnPlayerStart").addEventListener("click", () => playerEnsure(true));
$("btnKickOpen").addEventListener("click", () => openUrl("https://kick.com/" + (S.channelSlug || "mor-tilki")));

/* ═══════════ sekmeler ═══════════ */
function show(page) {
  document.querySelectorAll(".page").forEach((p) => p.classList.toggle("active", p.id === "page-" + page));
  document.querySelectorAll(".tab").forEach((t) => t.classList.toggle("active", t.dataset.page === page));
  if (page === "studio") startChatLoop();
  if (page === "history") renderHistory();
  // studio'ya ilk girişte oynatıcıyı kendiliğinden aç
  if (page === "studio" && !playerOn && !playerAutoTried) {
    playerAutoTried = true;
    playerEnsure(true);
  }
  updateMini();
}

document.querySelectorAll(".tab").forEach((t) => t.addEventListener("click", () => show(t.dataset.page)));

/* ═══════════ pencere ═══════════ */
$("btnMin").addEventListener("click", () => curWin.minimize());
$("btnMax").addEventListener("click", async () => (await curWin.isMaximized()) ? curWin.unmaximize() : curWin.toggleMaximize());
$("btnClose").addEventListener("click", async () => {
  if (S?.minimizeToTray !== false) {
    // pencereyi gizle, uygulama tepsi ikonunda arka planda çalışmaya devam eder
    await curWin.hide();
    const hint = $("statusHint");
    if (hint) {
      hint.textContent = "arka planda çalışıyor · tepsi ikonundan (KickForge) geri gel";
      setTimeout(() => { hint.textContent = ""; }, 6000);
    }
    return;
  }
  // tepsiye küçültme kapalıysa gerçekten çık
  await invoke("quit_app");
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
  $("setRadar").checked = !!S.radarMode;
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
    const numEl = $("viewerNum");
    numEl.textContent = viewers.toLocaleString("tr-TR");
    numEl.classList.remove("bump");
    void numEl.offsetWidth; // animasyonu yeniden tetikle
    numEl.classList.add("bump");
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
    updateRadar();
    maybeShowRating(live);

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
/* bildirim susturma: toggleNotif aşağıdaki "yeni cssler" bölümünde ortak */

/* inline bildirim notu */
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

$("btnWatch").addEventListener("click", () => { show("studio"); playerEnsure(true); });
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

/* ═══════════ yeni cssler: sağ tık menüsü ═══════════ */
function ctxRefresh() {
  $("ctxPlayerText").textContent = playerOn ? "oynatıcıyı kapat" : "oynatıcıyı aç";
  $("ctxPinText").textContent = "her zaman üstte";
  $("ctxMuteText").textContent = nulledNotif ? "bildirimleri aç" : "bildirimleri sustur";
}

function ctxOpenAt(x, y) {
  const m = $("ctxMenu");
  ctxRefresh();
  m.classList.add("open");
  const w = m.offsetWidth || 210, h = m.offsetHeight || 220;
  m.style.left = Math.min(x, window.innerWidth - w - 8) + "px";
  m.style.top = Math.min(y, window.innerHeight - h - 8) + "px";
}

function ctxClose() { $("ctxMenu").classList.remove("open"); }

document.addEventListener("contextmenu", (e) => {
  if (e.target.closest(".chat-line, .chat-feed, input")) { ctxClose(); return; }
  e.preventDefault();
  ctxOpenAt(e.clientX, e.clientY);
});

document.addEventListener("click", (e) => {
  if (!e.target.closest("#ctxMenu")) ctxClose();
});
document.addEventListener("keydown", (e) => { if (e.key === "Escape") { ctxClose(); closeEmoji(); } });

document.querySelectorAll("#ctxMenu .item").forEach((li) => {
  li.addEventListener("click", async () => {
    const act = li.dataset.act;
    ctxClose();
    if (act === "refresh") pollNow(true);
    if (act === "player") playerEnsure(!playerOn);
    if (act === "pin") {
      const on = !(await curWin.isAlwaysOnTop());
      await curWin.setAlwaysOnTop(on);
      S = { ...S, alwaysOnTop: on };
      $("setOnTop").checked = on;
      $("btnPin").style.color = on ? "var(--accent)" : "";
    }
    if (act === "mute") toggleNotif();
    if (act === "kick") openUrl("https://kick.com/" + (S.channelSlug || "mor-tilki"));
    if (act === "quit") await invoke("quit_app");
  });
});

/* ═══════════ bildirim susturma ortak işlevi ═══════════ */
function toggleNotif() {
  nulledNotif = !nulledNotif;
  $("btnNotif").classList.toggle("muted", nulledNotif);
  $("notifLabel").textContent = nulledNotif ? "bildirimler suslu" : "bildirimler açık";
  $("notifHint").textContent = nulledNotif ? "bu oturum için kapatıldı" : "yayın açılınca ve takipçi değişince uyarır";
}
$("btnNotif").onclick = null; // aşağıdaki listener ile değiştirilecek
$("btnNotif").addEventListener("click", toggleNotif);

/* ═══════════ radar modu ═══════════ */
function updateRadar() {
  const on = !!S.radarMode;
  $("radarCell").style.display = on ? "block" : "none";
  if (!on) return;
  const live = !!(lastStats && lastStats.isLive);
  $("radarBlip").className = "blip " + (live ? "on" : "");
  $("radarNote").textContent = live ? "NOKTADA · mor-tilki yayınla" : "radar açık · yayın bekleniyor";
}

$("setRadar").addEventListener("change", () => {
  S.radarMode = $("setRadar").checked;
  saveSettings();
  updateRadar();
});

/* ═══════════ KAYDET (VOD) düğmesi ═══════════ */
$("btnSaveVod").addEventListener("click", () => {
  openUrl("https://kick.com/" + (S.channelSlug || "mor-tilki") + "/videos");
  $("statusHint").textContent = "kick kayıtları (VOD) tarayıcıda açıldı · yayın bitece seyir burada";
  setTimeout(() => { $("statusHint").textContent = ""; }, 6000);
});

/* ═══════════ emoji ═══════════ */
const EMOJIS = ["😂","😍","🔥","👏","🦊","⭐","😭","🤣","🤝","💯","😱","😎","❤️","🙏","👀","🎉","🤯","💀","🥳","🫡","🤌","😈"];
const pop = $("emojiPop");
EMOJIS.forEach((em) => {
  const b = document.createElement("button");
  b.type = "button";
  b.textContent = em;
  b.addEventListener("click", () => {
    const el = $("chatInput");
    const s = el.selectionStart ?? el.value.length, e2 = el.selectionEnd ?? s;
    el.value = el.value.slice(0, s) + em + el.value.slice(e2);
    el.focus();
    el.selectionStart = el.selectionEnd = s + em.length;
    closeEmoji();
  });
  pop.appendChild(b);
});

function closeEmoji() { pop.classList.remove("open"); }
$("btnEmoji").addEventListener("click", (e) => { e.stopPropagation(); pop.classList.toggle("open"); });
document.addEventListener("click", (e) => { if (!e.target.closest(".ms-emoji-wrap")) closeEmoji(); });

/* ═══════════ mini video kartı ═══════════ */
function fmtElapsed(ms) {
  const s = Math.floor(ms / 1000);
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), ss = s % 60;
  return h + ":" + String(m).padStart(2, "0") + ":" + String(ss).padStart(2, "0");
}

function updateMini() {
  const card = $("miniCard");
  const active = document.querySelector(".page.active");
  const show = playerOn && active && active.id !== "page-studio" && !miniDismissed;
  if (!show) { card.style.display = "none"; card.classList.remove("show"); return; }
  if (card.style.display !== "block") { card.style.display = "block"; card.classList.add("show"); }
  const live = !!(lastStats && lastStats.isLive);
  const title = live ? (lastStats.sessionTitle || "yayın sürüyor") : "yayın kapalı · oynatıcı açık";
  $("mcTitle1").textContent = "@" + (S.channelSlug || "mor-tilki");
  $("mcTitle2").textContent = title;
  const pic = lastStats && lastStats.profilePic;
  if (pic && $("mcPfp").getAttribute("src") !== pic) $("mcPfp").src = pic;
  const ms = playerStartedAt ? Date.now() - playerStartedAt : 0;
  $("mcTime").textContent = fmtElapsed(ms);
  $("mcElapsed").style.width = Math.min(100, (ms / (4 * 3600 * 1000)) * 100) + "%";
}

setInterval(updateMini, 1000);
$("mcGoto").addEventListener("click", () => { show("studio"); });
$("mcHide").addEventListener("click", () => { miniDismissed = true; updateMini(); });

/* ═══════════ beğen / beğenme (site senkronu) ═══════════ */
let ratingShown = false;
let voted = false;

function maybeShowRating(live) {
  if (!live || ratingShown || voted) return;
  ratingShown = true;
  setTimeout(showRatingPop, 5000);
}

function formatCount(n) { return n >= 1000 ? (n / 1000).toFixed(1).replace(".0", "") + "k" : n; }

async function showRatingPop() {
  if (voted) return;
  $("likeCount").textContent = "beğen · …";
  $("dislikeCount").textContent = "beğenmedim · …";
  $("ratingPop").classList.add("show");
  try {
    const r = await invoke("rate_get", { apiBase: S.apiBase });
    $("likeCount").textContent = "beğen · " + formatCount(r.likes | 0);
    $("dislikeCount").textContent = "beğenmedim · " + formatCount(r.dislikes | 0);
    $("ratingPop").querySelector(".nodes-ips").textContent = "oyların herkese senkronlanır · sitendeki sayaç";
  } catch {
    $("likeCount").textContent = "beğen";
    $("dislikeCount").textContent = "beğenmedim";
    $("ratingPop").querySelector(".nodes-ips").textContent = "senkron ucu ulaşılamaz · yalnız yerel";
  }
}

function fw(dotEl) {
  dotEl.classList.add("on");
  setTimeout(() => dotEl.classList.remove("on"), 6300);
}

$("likeCheckbox").addEventListener("change", async (e) => {
  if (voted || !e.target.checked) return;
  voted = true;
  $("dislikeCheckbox").disabled = true;
  fw($("fwLike"));
  try {
    const r = await invoke("rate_post", { apiBase: S.apiBase, likes: 1, dislikes: 0 });
    $("likeCount").textContent = "beğen · " + formatCount(r.likes | 0);
  } catch {
    $("likeCount").textContent = "beğen · senkron yok";
  }
  $("ratingPop").querySelector(".nodes-ips").textContent = "geri bildirimin için teşekkürler";
});

$("dislikeCheckbox").addEventListener("change", async (e) => {
  if (voted || !e.target.checked) return;
  voted = true;
  $("likeCheckbox").disabled = true;
  fw($("fwDislike"));
  try {
    const r = await invoke("rate_post", { apiBase: S.apiBase, likes: 0, dislikes: 1 });
    $("dislikeCount").textContent = "beğenmedim · " + formatCount(r.dislikes | 0);
  } catch {
    $("dislikeCount").textContent = "beğenmedim · senkron yok";
  }
  $("ratingPop").querySelector(".nodes-ips").textContent = "geri bildirimin kaydedildi";
});

$("ratingClose").addEventListener("click", () => {
  $("ratingPop").classList.remove("show");
  ratingShown = true;
});
