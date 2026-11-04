use serde_json::{json, Value};
use std::path::PathBuf;
use std::sync::Mutex;
use tauri::Manager;
use tauri::State;
use tauri::tray::TrayIconBuilder;
use tokio::io::{AsyncReadExt, AsyncWriteExt};

/// uygulama durumu: http istemcisi + kanal kimliği önbelleği
pub struct AppState {
    pub client: reqwest::Client,
    pub ids_cache: Mutex<std::collections::HashMap<String, ChannelIds>>,
}

#[derive(Clone)]
pub struct ChannelIds {
    pub channel_id: i64,
    pub broadcaster_id: i64,
    pub slug: String,
}

// ─────────────────────────────────────────────────────────────────────────────
// küçük yardımcılar: kick/site json'u bazen çok konuşkan geliyor, esnek oku
// ─────────────────────────────────────────────────────────────────────────────

fn vnum(root: &Value, name: &str) -> f64 {
    match root.get(name) {
        Some(Value::Number(n)) => n.as_f64().unwrap_or(0.0),
        Some(Value::String(s)) => s.trim().parse::<f64>().unwrap_or(0.0),
        Some(Value::Bool(b)) => {
            if *b {
                1.0
            } else {
                0.0
            }
        }
        _ => 0.0,
    }
}

fn vstr(root: &Value, name: &str) -> String {
    match root.get(name) {
        Some(Value::String(s)) => s.clone(),
        Some(Value::Number(n)) => n.to_string(),
        _ => String::new(),
    }
}

fn vbool(root: &Value, name: &str) -> bool {
    vnum(root, name) > 0.0
}

fn folder() -> PathBuf {
    if let Some(dir) = dirs_roaming() {
        PathBuf::from(dir).join("KickForge").join("tauri")
    } else {
        PathBuf::from(".")
    }
}

// %APPDATA% şeklinde çözümle (tauri path api'sine bağımlılık azalt)
fn dirs_roaming() -> Option<String> {
    let home = std::env::var("APPDATA").ok()?;
    if home.trim().is_empty() {
        None
    } else {
        Some(home)
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// ayarlar
// ─────────────────────────────────────────────────────────────────────────────

const DEFAULT_SETTINGS: &str = r#"{
  "apiBase": "https://kickforge-mor-tilki.vercel.app",
  "channelSlug": "mor-tilki",
  "pollSeconds": 20,
  "allowDirect": true,
  "historyMinutes": 180,
  "notifyLive": true,
  "notifyFollower": true,
  "notifyViewerSpike": true,
  "notifyRecover": true,
  "quietHours": false,
  "quietStart": 23,
  "quietEnd": 9,
  "alwaysOnTop": false,
  "minimizeToTray": true,
  "startWithWindows": false
}"#;

#[tauri::command]
async fn get_settings() -> Result<Value, String> {
    let path = folder().join("settings.json");
    match std::fs::read_to_string(&path) {
        Ok(text) => Ok(serde_json::from_str::<Value>(&text)
            .unwrap_or_else(|_| serde_json::from_str(DEFAULT_SETTINGS).unwrap())),
        Err(_) => Ok(serde_json::from_str(DEFAULT_SETTINGS).unwrap()),
    }
}

#[tauri::command]
async fn save_settings(settings: Value) -> Result<Value, String> {
    let dir = folder();
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let path = dir.join("settings.json");
    std::fs::write(path, serde_json::to_string_pretty(&settings).unwrap())
        .map_err(|e| e.to_string())?;
    Ok(json!({ "ok": true }))
}

// ─────────────────────────────────────────────────────────────────────────────
// istatistik: önce site (/api/stats, önbekli + tekrar dener), sonra kick.com
// ─────────────────────────────────────────────────────────────────────────────

fn now_iso_ms() -> String {
    // utc saniye — ön yüz çizim anahtarı olarak kullanıyor
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| (d.as_millis() as i64).to_string())
        .unwrap_or_default()
}

fn parse_site_stats(root: &Value, state: &AppState, slug: &str) -> Value {
    let stats = json!({
        "isLive": vbool(root, "isLive"),
        "viewerCount": vnum(root, "viewerCount") as i64,
        "sessionTitle": vstr(root, "sessionTitle"),
        "followersCount": vnum(root, "followersCount") as i64,
        "profilePic": vstr(root, "profilePic"),
        "verified": vbool(root, "verified"),
        "broadcasterId": root.get("broadcasterId").and_then(Value::as_i64).unwrap_or(0),
        "channelId": root.get("channelId").and_then(Value::as_i64).unwrap_or(
            root.get("id").and_then(Value::as_i64).unwrap_or(0)
        ),
        "stale": vbool(root, "stale"),
        "source": "site",
        "fetchedAt": now_iso_ms()
    });
    cache_ids(state, slug, stats.clone());
    stats
}

fn cache_ids(state: &AppState, slug: &str, stats: Value) {
    let broadcaster = stats
        .get("broadcasterId")
        .and_then(Value::as_i64)
        .unwrap_or(0);
    let channel = stats.get("channelId").and_then(Value::as_i64).unwrap_or(0);
    // kanal no doğrulanmadan önbelleğe koy: 0'ın web ucu 403'e düşer
    if channel != 0 {
        state.ids_cache.lock().unwrap().insert(
            slug.to_string(),
            ChannelIds {
                channel_id: channel,
                broadcaster_id: broadcaster,
                slug: slug.to_string(),
            },
        );
    }
}

fn parse_kick_stats(root: &Value, _slug: &str) -> Value {
    let mut broadcaster = root
        .get("user_id")
        .and_then(Value::as_i64)
        .unwrap_or(0);
    let mut pic = String::new();
    if let Some(user) = root.get("user") {
        if user.get("verified").and_then(Value::as_bool).unwrap_or(false) {
            // verified alanı altta da var; sadece resmi oku
        }
        if let Some(p) = user.get("profile_pic").and_then(Value::as_str) {
            pic = p.to_string();
        }
        broadcaster = user.get("id").and_then(Value::as_i64).unwrap_or(broadcaster);
    }
    let live = root.get("livestream");
    let (is_live, viewers, title) = match live {
        Some(l) => (
            vbool(l, "is_live"),
            vnum(l, "viewer_count") as i64,
            vstr(l, "session_title"),
        ),
        None => (false, 0, String::new()),
    };
    json!({
        "isLive": is_live,
        "viewerCount": viewers,
        "sessionTitle": title,
        "followersCount": vnum(root, "followers_count") as i64,
        "profilePic": pic,
        "verified": vbool(root, "verified"),
        "broadcasterId": broadcaster,
        "stale": false,
        "source": "kick",
        "fetchedAt": now_iso_ms()
    })
}

async fn fetch_json(state: &AppState, url: &str, referer: Option<&str>, origin: Option<&str>) -> Result<Value, String> {
    let mut req = state
        .client
        .get(url)
        .header("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36")
        .header("accept", "application/json");
    if let Some(r) = referer {
        req = req.header("referer", r);
    }
    if let Some(o) = origin {
        req = req.header("origin", o);
    }
    let text = req.send().await.map_err(|e| e.to_string())?.error_for_status().map_err(|e| e.to_string())?.text().await.map_err(|e| e.to_string())?;
    let trimmed = text.trim_start();
    if !trimmed.starts_with('{') && !trimmed.starts_with('[') {
        return Err("beklenmeyen gövde".into());
    }
    serde_json::from_str::<Value>(trimmed).map_err(|e| e.to_string())
}

#[tauri::command]
async fn get_stats(
    state: State<'_, AppState>,
    api_base: String,
    slug: String,
    allow_direct: bool,
) -> Result<Value, String> {
    let mut problems: Vec<String> = Vec::new();

    if !api_base.trim().is_empty() {
        let url = format!("{}/api/stats", api_base.trim_end_matches('/'));
        for attempt in 0..2u32 {
            match fetch_json(&state, &url, None, None).await {
                Ok(root) => {
                    if root.get("error").is_some()
                        && vnum(&root, "followersCount") == 0.0
                        && !vbool(&root, "isLive")
                    {
                        problems.push(format!("site: {}", vstr(&root, "error")));
                    } else {
                        return Ok(parse_site_stats(&root, &state, &slug));
                    }
                }
                Err(e) => {
                    let short = e.chars().take(90).collect::<String>();
                    problems.push(format!("site: {}", short));
                }
            }
            if attempt == 0 {
                tokio::time::sleep(std::time::Duration::from_millis(450)).await;
            }
        }
    }

    if allow_direct && !slug.trim().is_empty() {
        for url in [
            format!("https://kick.com/api/v2/channels/{}", slug),
            format!("https://kick.com/api/v1/channels/{}", slug),
        ] {
            match fetch_json(&state, &url, Some("https://kick.com/"), None).await {
                Ok(root) => {
                    let stats = parse_kick_stats(&root, &slug);
                    let mut with_ids = stats;
                    with_ids["channelId"] = json!(root.get("id").and_then(Value::as_i64).unwrap_or(0));
                    cache_ids(&state, &slug, with_ids.clone());
                    return Ok(with_ids);
                }
                Err(e) => {
                    let short = e.chars().take(90).collect::<String>();
                    problems.push(format!("kick: {}", short));
                }
            }
        }
    }

    Err(problems.join(" | "))
}

// ─────────────────────────────────────────────────────────────────────────────
// güncelleme: site /api/release; düşerse github latest
// ─────────────────────────────────────────────────────────────────────────────

#[tauri::command]
async fn get_release(state: State<'_, AppState>) -> Result<Value, String> {
    let site = format!(
        "{}/api/release",
        "https://kickforge-mor-tilki.vercel.app"
    );
    if let Ok(root) = fetch_json(&state, &site, None, None).await {
        if root.get("ok").and_then(Value::as_bool).unwrap_or(false) {
            return Ok(root);
        }
    }
    let gh = fetch_json(
        &state,
        "https://api.github.com/repos/OxygenForge-code/KickForge-Mor_tilki/releases/latest",
        None,
        None,
    )
    .await?;
    Ok(json!({
        "ok": true,
        "title": gh.get("name").and_then(Value::as_str).unwrap_or(""),
        "build": gh.get("tag_name").and_then(Value::as_str).unwrap_or(""),
        "published": gh.get("published_at").and_then(Value::as_str).unwrap_or(""),
        "notes": gh.get("body").and_then(Value::as_str).unwrap_or("")
    }))
}

// ─────────────────────────────────────────────────────────────────────────────
// geçmiş (history.json): örnek listesi + eğri
// ─────────────────────────────────────────────────────────────────────────────

// beğeni/beğenmeme: sitendeki /api/rate ucu (vercel'de herkeste senkron)
#[tauri::command]
async fn rate_get(state: State<'_, AppState>, api_base: String) -> Result<Value, String> {
    let url = format!("{}/api/rate", api_base.trim_end_matches('/'));
    fetch_json(&state, &url, None, None).await
}

#[tauri::command]
async fn rate_post(
    state: State<'_, AppState>,
    api_base: String,
    likes: i64,
    dislikes: i64,
) -> Result<Value, String> {
    let url = format!("{}/api/rate", api_base.trim_end_matches('/'));
    let resp = state
        .client
        .post(&url)
        .header("content-type", "application/json")
        .body(json!({ "likes": likes, "dislikes": dislikes }).to_string())
        .send()
        .await
        .map_err(|e| e.to_string())?;
    let status = resp.status().as_u16();
    let text = resp.text().await.map_err(|e| e.to_string())?;
    if !(200..300).contains(&status) {
        return Err(format!(
            "rate ({}) · {}",
            status,
            text.chars().take(110).collect::<String>()
        ));
    }
    serde_json::from_str::<Value>(&text).map_err(|e| e.to_string())
}

#[tauri::command]
fn get_history() -> Result<Value, String> {
    let path = folder().join("history.json");
    match std::fs::read_to_string(&path) {
        Ok(text) => Ok(serde_json::from_str::<Value>(&text)
            .unwrap_or_else(|_| json!([]))),
        Err(_) => Ok(json!([])),
    }
}

#[tauri::command]
fn add_history(sample: Value, keep_minutes: i64) -> Result<Value, String> {
    let mut list = get_history().unwrap_or_else(|_| json!([]));
    let arr = list.as_array_mut().ok_or("bozuk geçmiş")?;
    arr.push(sample);
    let cutoff = (std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or(0))
        - (keep_minutes.max(30) * 60_000);
    arr.retain(|s| {
        s.get("t")
            .and_then(Value::as_i64)
            .map(|t| t >= cutoff)
            .unwrap_or(true)
    });
    while arr.len() > 3000 {
        arr.remove(0);
    }
    let dir = folder();
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    std::fs::write(dir.join("history.json"), serde_json::to_string(&list).unwrap())
        .map_err(|e| e.to_string())?;
    Ok(list)
}

// ─────────────────────────────────────────────────────────────────────────────
// kick oturumu: DPAPI token deposu (WPF native auth.dat ile uyumlu)
// ─────────────────────────────────────────────────────────────────────────────

mod dpapi {
    use windows_sys::Win32::Security::Cryptography::{CryptProtectData, CryptUnprotectData, CRYPT_INTEGER_BLOB};
    use windows_sys::Win32::Foundation::LocalFree;

    pub fn decrypt(bytes: &[u8]) -> Result<Vec<u8>, String> {
        if bytes.is_empty() {
            return Err("boş blob".into());
        }
        let mut input = CRYPT_INTEGER_BLOB { cbData: bytes.len() as u32, pbData: bytes.as_ptr() as *mut u8 };
        let mut out = CRYPT_INTEGER_BLOB { cbData: 0, pbData: std::ptr::null_mut() };
        let ok = unsafe {
            CryptUnprotectData(
                &mut input,
                std::ptr::null_mut(),
                std::ptr::null(),
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                0,
                &mut out,
            )
        };
        if ok == 0 {
            return Err("dpapi açılamadı".into());
        }
        let data = unsafe { std::slice::from_raw_parts(out.pbData, out.cbData as usize) }.to_vec();
        unsafe { LocalFree(out.pbData as _) };
        Ok(data)
    }

    pub fn encrypt(bytes: &[u8]) -> Result<Vec<u8>, String> {
        let mut input = CRYPT_INTEGER_BLOB { cbData: bytes.len() as u32, pbData: bytes.as_ptr() as *mut u8 };
        let mut out = CRYPT_INTEGER_BLOB { cbData: 0, pbData: std::ptr::null_mut() };
        let ok = unsafe {
            CryptProtectData(
                &mut input,
                std::ptr::null_mut(),
                std::ptr::null(),
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                1, // CRYPTPROTECT_UI_FORBIDDEN
                &mut out,
            )
        };
        if ok == 0 {
            return Err("dpapi kapatılamadı".into());
        }
        let data = unsafe { std::slice::from_raw_parts(out.pbData, out.cbData as usize) }.to_vec();
        unsafe { LocalFree(out.pbData as _) };
        Ok(data)
    }
}

fn native_auth_path() -> PathBuf {
    dirs_roaming()
        .map(|d| PathBuf::from(d).join("KickForge").join("native").join("auth.dat"))
        .unwrap_or_else(|| PathBuf::from("auth.dat"))
}

fn load_auth() -> Result<Value, String> {
    let bytes = std::fs::read(native_auth_path()).map_err(|_| "oturum yok".to_string())?;
    let plain = dpapi::decrypt(&bytes)?;
    serde_json::from_slice::<Value>(&plain).map_err(|e| format!("bozuk oturum: {}", e))
}

fn save_auth(auth: &Value) -> Result<(), String> {
    let bytes = dpapi::encrypt(serde_json::to_string(auth).unwrap().as_bytes())?;
    std::fs::write(native_auth_path(), bytes).map_err(|e| e.to_string())
}

#[tauri::command]
fn get_auth() -> Result<Value, String> {
    match load_auth() {
        Ok(a) => {
            let username = vstr(&a, "UserName");
            let has_token = !vstr(&a, "access").is_empty() || !vstr(&a, "Access").is_empty();
            Ok(json!({ "loggedIn": has_token && !username.is_empty() || has_token, "username": username }))
        }
        Err(_) => Ok(json!({ "loggedIn": false, "username": "" })),
    }
}

#[tauri::command]
fn logout() -> Result<Value, String> {
    match std::fs::remove_file(native_auth_path()) {
        Ok(_) | Err(_) => Ok(json!({ "ok": true })),
    }
}

// token var mı bak; süresi dolmuşsa 401 yanıtında refresh eder
async fn ensure_token(state: &State<'_, AppState>, _api_base: &str) -> Result<String, String> {
    let _ = state;
    let auth = load_auth()?;
    let access = {
        let a = vstr(&auth, "Access");
        if a.is_empty() {
            vstr(&auth, "access")
        } else {
            a
        }
    };
    if access.is_empty() {
        return Err("giriş gerekli".into());
    }
    Ok(access)
}

async fn refresh_token(
    state: &State<'_, AppState>,
    api_base: &str,
    auth: &mut Value,
) -> Result<String, String> {
    let mut refresh = vstr(auth, "Refresh");
    if refresh.is_empty() {
        refresh = vstr(auth, "refresh");
    }
    if refresh.is_empty() {
        return Err("giriş gerekli".into());
    }
    let resp = state
        .client
        .post(format!("{}/api/desktop-refresh", api_base.trim_end_matches('/')))
        .json(&json!({ "refresh_token": refresh }))
        .send()
        .await
        .map_err(|e| e.to_string())?;
    let status = resp.status().as_u16();
    let body = resp.text().await.map_err(|e| e.to_string())?;
    if !(200..300).contains(&status) {
        return Err(format!("token yenilenemedi ({})", status));
    }
    let root: Value =
        serde_json::from_str(&body).map_err(|e| format!("bozuk yanıt: {}", e))?;
    let access = vstr(&root, "access_token");
    if access.is_empty() {
        return Err("token yenilenemedi".into());
    }
    if let Some(r) = root.get("refresh_token").and_then(Value::as_str) {
        auth["Refresh"] = json!(r);
    }
    auth["Access"] = json!(access);
    let _ = save_auth(auth);
    Ok(access)
}

// tarayıcıda açıp port'a dönen token'ı alır (WPF'le aynı uçlar)
#[tauri::command]
async fn start_login(state: State<'_, AppState>, api_base: String) -> Result<Value, String> {
    // boş port seç (47821..47831)
    let state_key = format!("{}", std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_nanos()).unwrap_or(0));
    let mut picked: Option<u16> = None;
    let mut listener_holder: Option<tokio::net::TcpListener> = None;
    for port in 47821u16..=47831u16 {
        match tokio::net::TcpListener::bind(("127.0.0.1", port)).await {
            Ok(l) => {
                picked = Some(port);
                listener_holder = Some(l);
                break;
            }
            Err(_) => continue,
        }
    }
    let port = picked.ok_or("yerel port bulunamadı")?;
    let mut listener = listener_holder.take().unwrap();
    let url = format!("{}/api/auth?desktop={}&state={}", api_base.trim_end_matches('/'), port, state_key);

    tauri_plugin_opener::open_url(url, None::<&str>).map_err(|e| e.to_string())?;

    let deadline = tokio::time::Instant::now() + std::time::Duration::from_secs(180);
    let mut username = String::new();

    loop {
        let (mut sock, _) = tokio::select! {
            _ = tokio::time::sleep_until(deadline) => return Err("giriş zaman aşımına uğradı".into()),
            c = listener.accept() => c.map_err(|e| e.to_string())?,
        };
        let mut buf = [0u8; 4096];
        let n = sock.read(&mut buf).await.unwrap_or(0);
        let req = String::from_utf8_lossy(&buf[..n]).to_string();
        let first = req.lines().next().unwrap_or("");
        let mut ok = false;
        // /auth?state=...&access_token=...
        if first.to_uppercase().contains(" HTTP") && first.contains("/auth") {
            let params = parse_query(first.split('?').nth(1).unwrap_or(""));
            if params.get("state").map(String::as_str) != Some(state_key.as_str()) {
                write_html(&mut sock, "giriş reddedildi", "Durum doğrulanamadı. Uygulamadan tekrar dene.", false).await;
            } else if let Some(access) = params.get("access_token").cloned() {
                if !access.is_empty() {
                    let mut stored = json!({
                        "Access": access,
                        "Refresh": params.get("refresh_token").cloned().unwrap_or_default(),
                        "ExpiresAt": "9999-01-01T00:00:00Z",
                        "UserName": ""
                    });
                    username = fetch_username(&state, &access).await;
                    stored["UserName"] = json!(username);
                    save_auth(&stored).ok();
                    ok = true;
                    write_html(&mut sock, "giriş tamam", "KickForge'a dönebilirsin, bu sekmeyi kapatabilirsin.", true).await;
                } else {
                    write_html(&mut sock, "giriş tamamlanmadı", "Kick token göndermedi.", false).await;
                }
            } else {
                write_html(&mut sock, "giriş tamamlanmadı", "Kick token göndermedi.", false).await;
            }
        } else {
            write_html(&mut sock, "KickForge", "Bu sayfa yalnızca giriş dönüşü içindir.", false).await;
        }
        if ok {
            break;
        }
    }

    Ok(json!({ "ok": true, "username": username }))
}

fn parse_query(q: &str) -> std::collections::HashMap<String, String> {
    let mut out = std::collections::HashMap::new();
    for pair in q.split('&') {
        let (k, v) = match pair.split_once('=') {
            Some((k, v)) => (k, v),
            None => continue,
        };
        out.insert(url_decode(k), url_decode(v));
    }
    out
}

fn url_decode(s: &str) -> String {
    let bytes = s.as_bytes();
    let mut out: Vec<u8> = Vec::with_capacity(bytes.len());
    let mut i = 0;
    while i < bytes.len() {
        match bytes[i] {
            b'%' if i + 2 < bytes.len() => {
                let hex = std::str::from_utf8(&bytes[i + 1..i + 3]).unwrap_or("");
                if let Ok(v) = u8::from_str_radix(hex, 16) {
                    out.push(v);
                    i += 3;
                } else {
                    out.push(b'%');
                    i += 1;
                }
            }
            b'+' => {
                out.push(b' ');
                i += 1;
            }
            c => {
                out.push(c);
                i += 1;
            }
        }
    }
    String::from_utf8(out).unwrap_or_default()
}

async fn write_html(sock: &mut tokio::net::TcpStream, title: &str, message: &str, success: bool) {
    let accent = if success { "#c6f24e" } else { "#ff6b6b" };
    let html = format!(
        "<!doctype html><meta charset=\"utf-8\"><title>{}</title><style>html{{background:#0b0a10;color:#f3f1f8;font:15px/1.6 'Segoe UI',sans-serif}}body{{display:grid;place-items:center;height:100vh;margin:0}}div{{text-align:center;padding:32px 40px;border:1px solid #2c2539;border-radius:16px;background:#16131f}}b{{color:{};display:block;font-size:20px;margin-bottom:8px}}span{{color:#9992ad;font-size:13px}}</style><div><b>{}</b><span>{}</span></div><script>history.replaceState({{}},'', '/');</script>",
        title, accent, title, message
    );
    let bytes = html.as_bytes();
    let head = format!("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {}\r\nConnection: close\r\n\r\n", bytes.len());
    let _ = sock.write_all(head.as_bytes()).await;
    let _ = sock.write_all(bytes).await;
    let _ = sock.flush().await;
}

async fn fetch_username(state: &State<'_, AppState>, access: &str) -> String {
    let resp = state
        .client
        .get("https://api.kick.com/public/v1/users")
        .bearer_auth(access)
        .header("accept", "application/json")
        .send()
        .await;
    let body = match resp {
        Ok(r) => match r.text().await {
            Ok(t) => t,
            Err(_) => return String::new(),
        },
        Err(_) => return String::new(),
    };
    let root: Value = serde_json::from_str(&body).unwrap_or(Value::Null);
    if let Some(data) = root.get("data").and_then(Value::as_array) {
        if let Some(first) = data.first() {
            if let Some(n) = first.get("name").and_then(Value::as_str) {
                return n.to_string();
            }
            if let Some(u) = first.get("username").and_then(Value::as_str) {
                return u.to_string();
            }
        }
    }
    String::new()
}

// ─────────────────────────────────────────────────────────────────────────────
// sohbet: okuma web.uç (kimliksiz), yazma resmî uç (token)
// ─────────────────────────────────────────────────────────────────────────────

fn resolve_ids(state: &AppState, slug: &str) -> Option<ChannelIds> {
    state.ids_cache.lock().unwrap().get(slug).cloned()
}

async fn fetch_ids(state: &AppState, slug: &str) -> Result<ChannelIds, String> {
    if let Some(ids) = resolve_ids(state, slug) {
        if ids.channel_id != 0 {
            return Ok(ids);
        }
    }
    let url = format!("https://kick.com/api/v2/channels/{}", slug);
    let root = match fetch_json(state, &url, Some("https://kick.com/"), None).await {
        Ok(r) => r,
        Err(_) => {
            let url2 = format!("https://kick.com/api/v1/channels/{}", slug);
            fetch_json(state, &url2, Some("https://kick.com/"), None).await?
        }
    };
    let channel = root.get("id").and_then(Value::as_i64).unwrap_or(0);
    let mut broadcaster = root.get("user_id").and_then(Value::as_i64).unwrap_or(0);
    if broadcaster == 0 {
        if let Some(user) = root.get("user") {
            broadcaster = user.get("id").and_then(Value::as_i64).unwrap_or(0);
        }
    }
    if channel == 0 {
        return Err("kanal no bulunamadı".into());
    }
    let ids = ChannelIds { channel_id: channel, broadcaster_id: broadcaster, slug: slug.to_string() };
    state.ids_cache.lock().unwrap().insert(slug.to_string(), ids.clone());
    Ok(ids)
}

// rozet etiketi + renk (WPF tablosuyla aynı)
fn badge_label(kind: &str) -> (String, String) {
    let k = kind.to_ascii_lowercase();
    match k.as_str() {
        "broadcaster" => ("YAYINCI".into(), "#C6F24E".into()),
        "moderator" => ("MOD".into(), "#7CE38B".into()),
        "staff" => ("EKİP".into(), "#8FD8FF".into()),
        "vip" => ("VIP".into(), "#FF7AC8".into()),
        "founder" => ("KURUCU".into(), "#FFC46B".into()),
        "subscriber" => ("ABONE".into(), "#8FA8FF".into()),
        "sub_gifter" => ("HEDİYE".into(), "#8FA8FF".into()),
        "og" => ("OG".into(), "#FFC46B".into()),
        _ => ("ONAYLI".into(), "#8FD8FF".into()),
    }
}

fn parse_message(item: &Value) -> Option<Value> {
    let content = vstr(item, "content");
    if content.trim().is_empty() {
        return None;
    }
    let mut user = String::new();
    let mut color = "#9A94AC".to_string();
    let mut badges: Vec<Value> = Vec::new();
    let mut level = 0i64;
    let mut is_bot = false;

    if let Some(sender) = item.get("sender") {
        user = vstr(sender, "username");
        if user.is_empty() {
            user = vstr(sender, "slug");
        }
        if vstr(sender, "slug").eq_ignore_ascii_case("kickbot") {
            is_bot = true;
        }
        if let Some(ident) = sender.get("identity") {
            if let Some(c) = ident.get("color").and_then(Value::as_str) {
                if c.starts_with('#') || c.starts_with("rgb") {
                    color = c.to_string();
                }
            }
            if let Some(blist) = ident.get("badges").and_then(Value::as_array) {
                for b in blist {
                    let kind = vstr(b, "type");
                    if kind.is_empty() {
                        continue;
                    }
                    let known = badge_label(&kind);
                    let label = match b.get("text").and_then(Value::as_str) {
                        Some(t) if t.to_ascii_uppercase() != known.0 => t.to_uppercase(),
                        _ => known.0,
                    };
                    if kind.eq_ignore_ascii_case("moderator") || kind.eq_ignore_ascii_case("vip") {
                        badges.push(json!({ "text": label, "color": known.1, "major": true }));
                    } else {
                        badges.push(json!({ "text": label, "color": known.1 }));
                    }
                }
            }
            if let Some(v2) = ident.get("badges_v2").and_then(Value::as_array) {
                for b in v2 {
                    if let Some(name) = b.get("name").and_then(Value::as_str) {
                        if name.eq_ignore_ascii_case("level") {
                            if let Some(l) = b.pointer("/metadata/level").and_then(Value::as_i64) {
                                level = l;
                                if l > 0 {
                                    badges.push(json!({ "text": l, "color": "#C2B8E6", "level": true }));
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    if let Some(meta) = item.get("metadata") {
        if let Value::String(m) = meta {
            if m.contains("\"sender_type\":\"bot\"") {
                is_bot = true;
            }
        }
    }

    Some(json!({
        "id": vstr(item, "id"),
        "content": content,
        "time": vstr(item, "created_at"),
        "user": user,
        "color": color,
        "badges": badges,
        "level": level,
        "isBot": is_bot
    }))
}

#[tauri::command]
async fn get_chat(state: State<'_, AppState>, api_base: Option<String>, slug: String) -> Result<Value, String> {
    let ids = fetch_ids(&state, &slug).await?;

    // önce kendi sahamız: site sohbet geçmişi proxy'si (istemciden web.kick.com 403)
    if let Some(base) = api_base {
        if !base.trim().is_empty() {
            let url = format!(
                "{}/api/chat-history?channelId={}&slug={}",
                base.trim_end_matches('/'), ids.channel_id, ids.slug
            );
            if let Ok(root) = fetch_json(&state, &url, None, None).await {
                let mut messages: Vec<Value> = Vec::new();
                if let Some(list) = root.pointer("/data/messages").and_then(Value::as_array) {
                    for item in list {
                        if let Some(m) = parse_message(item) {
                            messages.push(m);
                        }
                    }
                }
                let take_from = messages.len().saturating_sub(120);
                return Ok(json!({
                    "channelId": ids.channel_id,
                    "broadcasterId": ids.broadcaster_id,
                    "slug": ids.slug,
                    "source": "site",
                    "messages": &messages[take_from..]
                }));
            }
        }
    }

    let url = format!("https://web.kick.com/api/v1/chat/{}/history", ids.channel_id);
    let root = fetch_json(
        &state,
        &url,
        Some(&format!("https://kick.com/{}/chat", ids.slug)),
        Some("https://kick.com"),
    )
    .await
    .map_err(|e| format!("sohbet alınamadı · {}", e))?;

    let mut messages: Vec<Value> = Vec::new();
    if let Some(list) = root.pointer("/data/messages").and_then(Value::as_array) {
        for item in list {
            if let Some(m) = parse_message(item) {
                messages.push(m);
            }
        }
    }
    let take_from = messages.len().saturating_sub(120);
    Ok(json!({
        "channelId": ids.channel_id,
        "broadcasterId": ids.broadcaster_id,
        "slug": ids.slug,
        "messages": &messages[take_from..]
    }))
}

#[tauri::command]
async fn send_chat(
    state: State<'_, AppState>,
    api_base: String,
    slug: String,
    text: String,
) -> Result<Value, String> {
    if text.trim().is_empty() {
        return Err("boş mesaj".into());
    }
    let ids = fetch_ids(&state, &slug).await?;
    let mut access = ensure_token(&state, &api_base).await?;

    let payload = json!({
        "broadcaster_user_id": ids.broadcaster_id,
        "content": text,
        "type": "user"
    })
    .to_string();

    let mut last_err = String::new();
    for attempt in 0..2 {
        let req = state
            .client
            .post("https://api.kick.com/public/v1/chat")
            .bearer_auth(&access)
            .header("content-type", "application/json")
            .body(payload.clone());
        let resp = req.send().await.map_err(|e| e.to_string())?;
        let status = resp.status().as_u16();
        let body = resp.text().await.unwrap_or_default();
        if (200..300).contains(&status) {
            return Ok(json!({ "ok": true }));
        }
        if status == 401 && attempt == 0 {
            let mut auth = load_auth().map_err(|_| "giriş gerekli".to_string())?;
            let refreshed = refresh_token(&state, &api_base, &mut auth).await?;
            access = refreshed;
            continue;
        }
        last_err = match status {
            401 | 403 => format!("yetki yok · tekrar giriş yapman gerekebilir ({})", status),
            429 => "çok hızlı yazdın, biraz bekleyip tekrar dene".into(),
            _ => {
                let short = body.chars().take(160).collect::<String>();
                format!("gönderilemedi ({}) {}", status, short)
            }
        };
        break;
    }
    Err(last_err)
}

#[tauri::command]
async fn check_auth_fresh(state: State<'_, AppState>, _api_base: String) -> Result<Value, String> {
    let auth = match load_auth() {
        Ok(a) => a,
        Err(_) => return Ok(json!({ "loggedIn": false })),
    };
    let access_raw = {
        let a = vstr(&auth, "Access");
        if a.is_empty() {
            vstr(&auth, "access")
        } else {
            a
        }
    };
    if access_raw.is_empty() {
        return Ok(json!({ "loggedIn": false }));
    }
    let username = fetch_username(&state, &access_raw).await;
    Ok(json!({ "loggedIn": !username.is_empty(), "username": username }))
}

// ─────────────────────────────────────────────────────────────────────────────
// koşu
// ─────────────────────────────────────────────────────────────────────────────

// çıkışa zorla: pencere kapanışı tepsiye gizlene hatta app tamamen sonlanır
#[tauri::command]
fn quit_app(app: tauri::AppHandle) {
    app.exit(0);
}

// koşu
// ────────────────────────────────────────────────────────────────────

pub fn run() {
    let client = reqwest::Client::builder()
        .timeout(std::time::Duration::from_secs(12))
        .connect_timeout(std::time::Duration::from_secs(8))
        .user_agent("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36")
        .build()
        .unwrap();

    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .manage(AppState {
            client,
            ids_cache: Mutex::new(std::collections::HashMap::new()),
        })
        .setup(|app| {
            // tepsi: köşedeki ikondan göster/çıkış — pencere gizliyken yol
            let goster = tauri::menu::MenuItem::with_id(app, "goster", "KickForge'u göster", true, None::<&str>)?;
            let cikis = tauri::menu::MenuItem::with_id(app, "cikis", "Çıkış", true, None::<&str>)?;
            let menu = tauri::menu::Menu::with_items(app, &[&goster, &cikis])?;
            TrayIconBuilder::with_id("kickforge-tray")
                .icon(app.default_window_icon().expect("ikon yok").clone())
                .tooltip("KickForge · mor-tilki")
                .menu(&menu)
                .show_menu_on_left_click(true)
                .on_tray_icon_event(|tray, event| {
                    if let tauri::tray::TrayIconEvent::Click { .. } = event {
                        if let Some(w) = tray.app_handle().get_webview_window("main") {
                            let _ = w.show();
                            let _ = w.unminimize();
                            let _ = w.set_focus();
                        }
                    }
                })
                .on_menu_event(|app, event| match event.id().as_ref() {
                    "goster" => {
                        if let Some(w) = app.get_webview_window("main") {
                            let _ = w.show();
                            let _ = w.unminimize();
                            let _ = w.set_focus();
                        }
                    }
                    "cikis" => app.exit(0),
                    _ => {}
                })
                .build(app)?;
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            get_settings,
            save_settings,
            get_stats,
            get_release,
            get_history,
            add_history,
            get_auth,
            logout,
            start_login,
            send_chat,
            get_chat,
            check_auth_fresh,
            rate_get,
            rate_post,
            quit_app
        ])
        .run(tauri::generate_context!())
        .expect("tauri çalışmadı");
}
