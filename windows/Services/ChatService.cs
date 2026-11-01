using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using KickForgeDesktop.Models;

namespace KickForgeDesktop.Services
{
    public class ChannelIds
    {
        public long ChannelId { get; set; }
        public long BroadcasterId { get; set; }
        public string Slug { get; set; } = "";
    }

    /// <summary>
    /// sohbet: okuma kick'in web ucundan (kimlik gerekmiyor), yazma kick'in
    /// resmî api'sinden (oauth token ile).
    /// </summary>
    public class ChatService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly JsonDocumentOptions _json = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        private string _cachedSlug = "";
        private ChannelIds? _cachedIds;

        public ChatService()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                ConnectTimeout = TimeSpan.FromSeconds(8),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2)
            };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<ChannelIds> ResolveIdsAsync(string slug, CancellationToken token)
        {
            if (_cachedIds != null && _cachedSlug == slug) return _cachedIds;

            using var response = await _http.GetAsync("https://kick.com/api/v2/channels/" + Uri.EscapeDataString(slug), token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new ChatException("kanal bilgisi alınamadı (" + (int)response.StatusCode + ")");

            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body, _json);
            var root = doc.RootElement;

            var ids = new ChannelIds { Slug = slug };
            if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number) ids.ChannelId = id.GetInt64();
            if (root.TryGetProperty("user_id", out var userId) && userId.ValueKind == JsonValueKind.Number) ids.BroadcasterId = userId.GetInt64();
            else if (root.TryGetProperty("user", out var user) && user.TryGetProperty("id", out var userId2) && userId2.ValueKind == JsonValueKind.Number)
                ids.BroadcasterId = userId2.GetInt64();

            if (ids.ChannelId == 0) throw new ChatException("kanal no bulunamadı");

            _cachedSlug = slug;
            _cachedIds = ids;
            return ids;
        }

        public async Task<List<ChatMessage>> FetchHistoryAsync(ChannelIds ids, CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://web.kick.com/api/v1/chat/" + ids.ChannelId + "/history");
            request.Headers.Referrer = new Uri("https://kick.com/" + ids.Slug + "/chat");
            request.Headers.TryAddWithoutValidation("Origin", "https://kick.com");

            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new ChatException("sohbet alınamadı (" + (int)response.StatusCode + ")");

            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body, _json);

            var list = new List<ChatMessage>();
            if (!doc.RootElement.TryGetProperty("data", out var data)) return list;
            if (!data.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array) return list;

            foreach (var item in messages.EnumerateArray())
            {
                var message = ParseMessage(item);
                if (message != null) list.Add(message);
            }

            return list;
        }

        private ChatMessage? ParseMessage(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object) return null;

            var content = item.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(content)) return null;

            var message = new ChatMessage
            {
                Id = item.TryGetProperty("id", out var id) ? id.ToString() : Guid.NewGuid().ToString(),
                Content = content,
                Time = DateTime.UtcNow
            };

            if (item.TryGetProperty("created_at", out var created) && created.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(created.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                message.Time = parsed;
            }

            message.UserBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x94, 0xAC));

            if (item.TryGetProperty("sender", out var sender) && sender.ValueKind == JsonValueKind.Object)
            {
                message.User = sender.TryGetProperty("username", out var name) && name.ValueKind == JsonValueKind.String
                    ? name.GetString() ?? ""
                    : sender.TryGetProperty("slug", out var slug) && slug.ValueKind == JsonValueKind.String ? slug.GetString() ?? "" : "";

                if (sender.TryGetProperty("identity", out var identity) && identity.ValueKind == JsonValueKind.Object &&
                    identity.TryGetProperty("color", out var color) && color.ValueKind == JsonValueKind.String)
                {
                    try
                    {
                        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color.GetString()!));
                        brush.Freeze();
                        message.UserBrush = brush;
                    }
                    catch
                    {
                        // renk bozuksa varsayılan kalır
                    }
                }
            }

            return message;
        }

        public async Task SendAsync(string apiBase, KickAuthService auth, ChannelIds ids, string text, CancellationToken token)
        {
            if (!await auth.EnsureTokenAsync(apiBase, token).ConfigureAwait(false))
            {
                throw new ChatException("giriş gerekli");
            }

            var payload = JsonSerializer.Serialize(new
            {
                broadcaster_user_id = ids.BroadcasterId,
                content = text,
                type = "user"
            });

            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.kick.com/public/v1/chat");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

                if (response.IsSuccessStatusCode) return;

                // token süresi dolmuş olabilir: bir kez yenileyip tekrar dene
                if ((int)response.StatusCode == 401 && attempt == 0)
                {
                    await auth.ForceRefreshAsync(apiBase, token).ConfigureAwait(false);
                    continue;
                }

                throw new ChatException(ExplainError((int)response.StatusCode, body));
            }
        }

        private static string ExplainError(int status, string body)
        {
            var shortBody = body.Length > 160 ? body.Substring(0, 160) : body;
            switch (status)
            {
                case 401:
                case 403:
                    return "yetki yok · tekrar giriş yapman gerekebilir (" + status + ")";
                case 429:
                    return "çok hızlı yazdın, biraz bekleyip tekrar dene";
                default:
                    return "gönderilemedi (" + status + ") " + shortBody;
            }
        }

        public void Dispose() => _http.Dispose();
    }

    public class ChatException : Exception
    {
        public ChatException(string message) : base(message) { }
    }
}
