using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KickForgeDesktop.Settings;

namespace KickForgeDesktop.Services
{
    /// <summary>
    /// Kick OAuth'u tarayıcıda yapıp token'ı 127.0.0.1 üzerinden devralıyor.
    /// Token'lar DPAPI ile (yalnızca bu windows kullanıcısı) şifreli saklanır.
    /// </summary>
    public class KickAuthService
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = false };

        private string StorePath => Path.Combine(SettingsStore.Folder, "auth.dat");

        public string? AccessToken { get; private set; }
        public string? RefreshToken { get; private set; }
        public DateTime ExpiresAt { get; private set; } = DateTime.MinValue;
        public string UserName { get; private set; } = "";

        public bool LoggedIn => !string.IsNullOrEmpty(AccessToken);

        public bool TokenFresh => LoggedIn && ExpiresAt > DateTime.Now.AddSeconds(90);

        private class Stored
        {
            public string? Access { get; set; }
            public string? Refresh { get; set; }
            public DateTime ExpiresAt { get; set; }
            public string? UserName { get; set; }
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(StorePath)) return;
                var raw = ProtectedData.Unprotect(File.ReadAllBytes(StorePath), null, DataProtectionScope.CurrentUser);
                var stored = JsonSerializer.Deserialize<Stored>(Encoding.UTF8.GetString(raw), Json);
                if (stored?.Access == null) return;
                AccessToken = stored.Access;
                RefreshToken = stored.Refresh;
                ExpiresAt = stored.ExpiresAt;
                UserName = stored.UserName ?? "";
            }
            catch (Exception ex)
            {
                Log.Write("kayıtlı oturum okunamadı: " + ex.Message);
            }
        }

        private void Persist()
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.Folder);
                var json = JsonSerializer.Serialize(new Stored
                {
                    Access = AccessToken,
                    Refresh = RefreshToken,
                    ExpiresAt = ExpiresAt,
                    UserName = UserName
                }, Json);
                var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(StorePath, encrypted);
            }
            catch (Exception ex)
            {
                Log.Write("oturum yazılamadı: " + ex.Message);
            }
        }

        public void Clear()
        {
            AccessToken = null;
            RefreshToken = null;
            ExpiresAt = DateTime.MinValue;
            UserName = "";
            try
            {
                if (File.Exists(StorePath)) File.Delete(StorePath);
            }
            catch
            {
                // silemezsek de oturum bellekte kapandı
            }
        }

        /// <summary>tarayıcıyı açar, kullanıcı girişi bitirince token'ı alır.</summary>
        public async Task<bool> LoginAsync(string apiBase, CancellationToken token)
        {
            var port = FindFreePort();
            if (port == 0) throw new InvalidOperationException("yerel port bulunamadı");

            var state = Guid.NewGuid().ToString("N");
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();

            try
            {
                var url = apiBase.TrimEnd('/') + "/api/auth?desktop=" + port + "&state=" + state;
                Log.Write("kick girişi başlatıldı (port " + port + ")");
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromMinutes(3));

                while (!timeout.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
                    var ok = await HandleRequestAsync(client, state, apiBase, timeout.Token).ConfigureAwait(false);
                    if (ok) return true;
                }

                throw new TimeoutException("giriş tamamlanmadı");
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("giriş zaman aşımına uğradı");
            }
            finally
            {
                listener.Stop();
            }
        }

        private async Task<bool> HandleRequestAsync(TcpClient client, string expectedState, string apiBase, CancellationToken token)
        {
            using var _ = client;
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);

            var requestLine = await reader.ReadLineAsync().ConfigureAwait(false) ?? "";
            string? line;
            do
            {
                line = await reader.ReadLineAsync().ConfigureAwait(false);
            } while (!string.IsNullOrEmpty(line));

            var parts = requestLine.Split(' ');
            var ok = false;

            if (parts.Length >= 2 && parts[1].StartsWith("/auth", StringComparison.Ordinal))
            {
                var query = ParseQuery(parts[1]);
                query.TryGetValue("state", out var gotState);

                if (gotState != expectedState)
                {
                    await WriteResponseAsync(stream, "giriş reddedildi", "Durum doğrulanamadı. Uygulamadan tekrar dene.", false).ConfigureAwait(false);
                }
                else if (query.TryGetValue("access_token", out var access) && !string.IsNullOrEmpty(access))
                {
                    AccessToken = access;
                    query.TryGetValue("refresh_token", out var refresh);
                    RefreshToken = refresh;
                    var seconds = double.TryParse(query.GetValueOrDefault("expires_in"), out var parsed) ? parsed : 3600;
                    ExpiresAt = DateTime.Now.AddSeconds(seconds);
                    ok = true;

                    UserName = await FetchUserNameAsync(apiBase, token).ConfigureAwait(false);
                    Persist();
                    Log.Write("kick girişi tamam" + (string.IsNullOrEmpty(UserName) ? "" : " · " + UserName));

                    await WriteResponseAsync(stream, "giriş tamam", "KickForge'a dönebilirsin, bu sekmeyi kapatabilirsin.", true).ConfigureAwait(false);
                }
                else
                {
                    await WriteResponseAsync(stream, "giriş tamamlanmadı", "Kick token göndermedi.", false).ConfigureAwait(false);
                }
            }
            else
            {
                await WriteResponseAsync(stream, "KickForge", "Bu sayfa yalnızca giriş dönüşü içindir.", false).ConfigureAwait(false);
            }

            return ok;
        }

        private static async Task WriteResponseAsync(NetworkStream stream, string title, string message, bool success)
        {
            var accent = success ? "#c6f24e" : "#ff6b6b";
            var html = "<!doctype html><meta charset=\"utf-8\"><title>" + title + "</title>"
                + "<style>html{background:#0b0a10;color:#f3f1f8;font:15px/1.6 'Segoe UI',sans-serif}"
                + "body{display:grid;place-items:center;height:100vh;margin:0}"
                + "div{text-align:center;padding:32px 40px;border:1px solid #2c2539;border-radius:16px;background:#16131f}"
                + "b{color:" + accent + ";display:block;font-size:20px;margin-bottom:8px}"
                + "span{color:#9992ad;font-size:13px}</style>"
                + "<div><b>" + WebUtility.HtmlEncode(title) + "</b><span>" + WebUtility.HtmlEncode(message) + "</span></div>"
                + "<script>history.replaceState({}, '', '/');</script>";

            var bytes = Encoding.UTF8.GetBytes(html);
            var header = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: "
                         + bytes.Length + "\r\nConnection: close\r\n\r\n";
            var head = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
            await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        private static Dictionary<string, string> ParseQuery(string pathAndQuery)
        {
            var result = new Dictionary<string, string>();
            var index = pathAndQuery.IndexOf('?');
            if (index < 0 || index == pathAndQuery.Length - 1) return result;

            foreach (var pair in pathAndQuery.Substring(index + 1).Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                var key = Uri.UnescapeDataString(pair.Substring(0, eq));
                var value = Uri.UnescapeDataString(pair.Substring(eq + 1));
                result[key] = value;
            }
            return result;
        }

        private static int FindFreePort()
        {
            for (var port = 47821; port <= 47831; port++)
            {
                try
                {
                    using var probe = new TcpListener(IPAddress.Loopback, port);
                    probe.Start();
                    probe.Stop();
                    return port;
                }
                catch
                {
                    // sıradaki portu dene
                }
            }
            return 0;
        }

        public async Task<string> FetchUserNameAsync(string apiBase, CancellationToken token)
        {
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var request = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Get, "https://api.kick.com/public/v1/users");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken);
                request.Headers.Accept.ParseAdd("application/json");
                using var response = await http.SendAsync(request, token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return UserName;

                var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
                {
                    var first = data[0];
                    if (first.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String) return name.GetString() ?? "";
                    if (first.TryGetProperty("username", out var username) && username.ValueKind == JsonValueKind.String) return username.GetString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Log.Write("kullanıcı adı alınamadı: " + ex.Message);
            }
            return UserName;
        }

        /// <summary>401 gelince zorla yenile.</summary>
        public Task<bool> ForceRefreshAsync(string apiBase, CancellationToken token)
        {
            ExpiresAt = DateTime.MinValue;
            return EnsureTokenAsync(apiBase, token);
        }

        /// <summary>token süresi dolmak üzereyse site üzerinden yeniler.</summary>
        public async Task<bool> EnsureTokenAsync(string apiBase, CancellationToken token)
        {
            if (TokenFresh) return true;
            if (string.IsNullOrEmpty(RefreshToken)) return false;

            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var payload = JsonSerializer.Serialize(new { refresh_token = RefreshToken });
                using var content = new System.Net.Http.StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await http.PostAsync(apiBase.TrimEnd('/') + "/api/desktop-refresh", content, token).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Write("token yenilenemedi: " + (int)response.StatusCode);
                    return false;
                }

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (!root.TryGetProperty("access_token", out var access)) return false;

                AccessToken = access.GetString();
                if (root.TryGetProperty("refresh_token", out var refresh) && refresh.ValueKind == JsonValueKind.String)
                {
                    RefreshToken = refresh.GetString();
                }
                if (root.TryGetProperty("expires_in", out var expires) && expires.ValueKind == JsonValueKind.Number)
                {
                    ExpiresAt = DateTime.Now.AddSeconds(expires.GetDouble());
                }
                Persist();
                Log.Write("kick token yenilendi");
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("token yenileme hatası: " + ex.Message);
                return false;
            }
        }
    }
}
