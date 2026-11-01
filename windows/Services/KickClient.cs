using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KickForgeDesktop.Models;

namespace KickForgeDesktop.Services
{
    /// <summary>
    /// kanal verisi. önce sitenin kendi /api/stats ucunu deniyor (önbellek +
    /// tekrar deneme orada), o düşerse kick.com'dan doğrudan okuyor.
    /// </summary>
    public class KickClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly JsonDocumentOptions _json = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        public KickClient()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                ConnectTimeout = TimeSpan.FromSeconds(8),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2)
            };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("tr-TR,tr;q=0.9,en;q=0.8");
        }

        public string LastSource { get; private set; } = "";

        public async Task<ChannelStats> FetchAsync(string apiBase, string slug, bool allowDirect, CancellationToken token)
        {
            var problems = new List<string>();

            if (!string.IsNullOrWhiteSpace(apiBase))
            {
                var url = apiBase.TrimEnd('/') + "/api/stats";
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        using var doc = await GetJsonAsync(url, token).ConfigureAwait(false);
                        var stats = ParseProxy(doc.RootElement);
                        LastSource = "site";
                        return stats;
                    }
                    catch (Exception ex)
                    {
                        problems.Add("site: " + Short(ex));
                        if (token.IsCancellationRequested) throw;
                        await Task.Delay(450 * (attempt + 1), token).ConfigureAwait(false);
                    }
                }
            }

            if (allowDirect && !string.IsNullOrWhiteSpace(slug))
            {
                var urls = new[]
                {
                    "https://kick.com/api/v2/channels/" + Uri.EscapeDataString(slug),
                    "https://kick.com/api/v1/channels/" + Uri.EscapeDataString(slug)
                };
                foreach (var url in urls)
                {
                    try
                    {
                        using var doc = await GetJsonAsync(url, token).ConfigureAwait(false);
                        var stats = ParseChannel(doc.RootElement);
                        LastSource = "kick";
                        return stats;
                    }
                    catch (Exception ex)
                    {
                        problems.Add("kick: " + Short(ex));
                        if (token.IsCancellationRequested) throw;
                    }
                }
            }

            throw new KickException(string.Join(" | ", problems));
        }

        private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (url.Contains("kick.com/"))
            {
                request.Headers.Referrer = new Uri("https://kick.com/");
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("HTTP " + (int)response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (body.TrimStart().StartsWith("{", StringComparison.Ordinal) == false)
            {
                throw new KickException("beklenmeyen gövde");
            }

            return JsonDocument.Parse(body, _json);
        }

        private static ChannelStats ParseProxy(JsonElement root)
        {
            var stats = new ChannelStats
            {
                IsLive = ReadBool(root, "isLive"),
                ViewerCount = (int)ReadNumber(root, "viewerCount"),
                SessionTitle = ReadString(root, "sessionTitle"),
                FollowersCount = (long)ReadNumber(root, "followersCount"),
                ProfilePic = ReadString(root, "profilePic"),
                Verified = ReadBool(root, "verified"),
                BroadcasterId = (long?)ReadNumberOrNull(root, "broadcasterId"),
                Stale = ReadBool(root, "stale")
            };

            if (root.TryGetProperty("error", out _) && stats.FollowersCount == 0 && !stats.IsLive)
            {
                throw new KickException(ReadString(root, "error"));
            }

            return stats;
        }

        private static ChannelStats ParseChannel(JsonElement root)
        {
            var stats = new ChannelStats
            {
                FollowersCount = (long)ReadNumber(root, "followers_count"),
                Verified = ReadBool(root, "verified"),
                BroadcasterId = (long?)ReadNumberOrNull(root, "user_id")
            };

            if (root.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
            {
                stats.ProfilePic = ReadString(user, "profile_pic");
                stats.BroadcasterId = (long?)ReadNumberOrNull(user, "id") ?? stats.BroadcasterId;
                if (user.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True) stats.Verified = true;
            }

            if (root.TryGetProperty("livestream", out var live) && live.ValueKind == JsonValueKind.Object)
            {
                stats.IsLive = ReadBool(live, "is_live");
                stats.ViewerCount = (int)ReadNumber(live, "viewer_count");
                stats.SessionTitle = ReadString(live, "session_title");
            }

            return stats;
        }

        /// <summary>sayı bazen 27, bazen "27" geliyor.</summary>
        private static double ReadNumber(JsonElement element, string name)
        {
            return ReadNumberOrNull(element, name) ?? 0;
        }

        private static double? ReadNumberOrNull(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value)) return null;
            switch (value.ValueKind)
            {
                case JsonValueKind.Number:
                    return value.GetDouble();
                case JsonValueKind.String:
                    if (double.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
                    if (bool.TryParse(value.GetString(), out var flag)) return flag ? 1 : 0;
                    return null;
                case JsonValueKind.True: return 1;
                case JsonValueKind.False: return 0;
                default: return null;
            }
        }

        private static bool ReadBool(JsonElement element, string name)
        {
            return ReadNumberOrNull(element, name) is > 0;
        }

        private static string ReadString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value)) return "";
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Number => value.ToString(),
                _ => ""
            };
        }

        private static string Short(Exception ex)
        {
            var text = ex.Message ?? ex.GetType().Name;
            return text.Length > 90 ? text.Substring(0, 90) : text;
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }

    public class KickException : Exception
    {
        public KickException(string message) : base(message) { }
    }
}
