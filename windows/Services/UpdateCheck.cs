using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KickForgeDesktop.Settings;

namespace KickForgeDesktop.Services
{
    /// <summary>github'daki son release bilgisi (sitendeki /api/release üzerinden).</summary>
    public sealed class LatestRelease
    {
        public string Version { get; init; } = "";
        public string Build { get; init; } = "";
        public string Url { get; init; } = "";
        public long? Size { get; init; }
        public string Title { get; init; } = "";
    }

    /// <summary>
    /// güncelleme denetimi: önce sitenin /api/release ucu denenir (5 dk cache'li),
    /// cevap gelmezse github'ın herkese açık release ucuna düşer.
    /// sürüm numarası release adı / notları / etiketinden aranır: yoksa
    /// sessizce "güncel" sayılır (uydurma bir sürümle uyarma olmaz).
    /// </summary>
    public static class UpdateCheck
    {
        private const string GithubLatest =
            "https://api.github.com/repos/OxygenForge-code/KickForge-Mor_tilki/releases/latest";

        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        private sealed class Payload
        {
            [JsonPropertyName("ok")] public bool Ok { get; set; }
            [JsonPropertyName("version")] public string? Version { get; set; }
            [JsonPropertyName("build")] public string? Build { get; set; }
            [JsonPropertyName("url")] public string? Url { get; set; }
            [JsonPropertyName("size")] public long? Size { get; set; }
            [JsonPropertyName("title")] public string? Title { get; set; }
        }

        public static string? CurrentVersion() =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);

        private static string StampPath => System.IO.Path.Combine(SettingsStore.Folder, "lastupdate");

        private static bool ShouldCheck()
        {
            try
            {
                if (File.Exists(StampPath) &&
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(StampPath) < TimeSpan.FromHours(2))
                {
                    return false;
                }
            }
            catch
            {
                // damga okunamadıysa denetim yapalım, sorun değil
            }

            return true;
        }

        private static void StampChecked()
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.Folder);
                File.WriteAllText(StampPath, DateTime.UtcNow.ToString("o"));
            }
            catch
            {
                // damga yazılamazsa her açılışta bir istek atarız, zararsız
            }
        }

        private static string? FindVersion(string?[] sources)
        {
            foreach (var source in sources)
            {
                if (string.IsNullOrWhiteSpace(source)) continue;
                var match = Regex.Match(source, @"(?<!\d)(\d+\.\d+\.\d+)(?!\d)");
                if (match.Success) return match.Groups[1].Value;
            }

            return null;
        }

        /// <summary>yeni sürüm varsa bilgisi; güncelse ya da okunamazsa null.</summary>
        public static async Task<LatestRelease?> ProbeAsync(string apiBase, bool force = false)
        {
            var current = CurrentVersion();
            if (current == null) return null;
            if (!force && !ShouldCheck()) return null;

            LatestRelease? found = null;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                var address = apiBase.TrimEnd('/') + "/api/release";
                Http.DefaultRequestHeaders.UserAgent.ParseAdd("kickforge-native");

                LatestRelease? fromSite = null;
                try
                {
                    var json = await Http.GetStringAsync(address, timeout.Token);
                    var data = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Payload>(json);
                    if (data is { Ok: true, Version: not null })
                    {
                        Log.Write($"güncelleme denetimi: site {data.Version} diyor · uygulamada {current}");
                        if (Version.TryParse(data.Version, out var latest))
                        {
                            if (latest > new Version(current))
                            {
                                fromSite = new LatestRelease
                                {
                                    Version = data.Version,
                                    Build = data.Build ?? "",
                                    Url = string.IsNullOrWhiteSpace(data.Url)
                                        ? "https://github.com/OxygenForge-code/KickForge-Mor_tilki/releases/latest/download/KickForge.exe"
                                        : data.Url,
                                    Size = data.Size,
                                    Title = data.Title ?? ""
                                };
                            }
                        }
                        else
                        {
                            Log.Write("güncelleme: sitenin sürüm bilgisi sayısal değil, github'a bakılıyor");
                        }
                    }
                }
                catch (Exception siteEx)
                {
                    Log.Write("güncelleme: site ucu cevap vermedi (" + siteEx.Message + "), github deneniyor");
                }

                found = fromSite;
                if (found == null)
                {
                    using var github = new HttpRequestMessage(HttpMethod.Get, GithubLatest);
                    github.Headers.UserAgent.ParseAdd("kickforge-native");
                    github.Headers.Accept.ParseAdd("application/vnd.github+json");
                    using var resp = await Http.SendAsync(github, timeout.Token);
                    resp.EnsureSuccessStatusCode();
                    var raw = await resp.Content.ReadAsStringAsync(timeout.Token);
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;

                    var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var body = root.TryGetProperty("body", out var b) ? b.GetString() : null;
                    var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                    var version = FindVersion([name, body, tag]);

                    string? assetUrl = null;
                    long? assetSize = null;
                    if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            var assetName = asset.TryGetProperty("name", out var an) ? an.GetString() : null;
                            if (assetName != null && assetSize == null)
                            {
                                assetSize = asset.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number
                                    ? sz.GetInt64()
                                    : null;
                            }

                            if (assetName == "KickForge.exe")
                            {
                                assetUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                                assetSize = asset.TryGetProperty("size", out var s2) && s2.ValueKind == JsonValueKind.Number
                                    ? s2.GetInt64()
                                    : assetSize;
                                break;
                            }
                        }
                    }

                    if (version == null)
                    {
                        Log.Write($"güncelleme: github'daki son sürümde (r etiketi {tag ?? "?"}) sayısal sürüm yok, uyarma yapılmıyor");
                    }
                    else
                    {
                        Log.Write($"güncelleme denetimi: github {version} · uygulamada {current}");
                        if (Version.TryParse(version, out var latest) && latest > new Version(current))
                        {
                            found = new LatestRelease
                            {
                                Version = version,
                                Build = tag ?? "",
                                Url = string.IsNullOrWhiteSpace(assetUrl)
                                    ? "https://github.com/OxygenForge-code/KickForge-Mor_tilki/releases/latest/download/KickForge.exe"
                                    : assetUrl,
                                Size = assetSize,
                                Title = name ?? ""
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("güncelleme denetimi başarısız: " + ex.Message);
            }
            finally
            {
                StampChecked();
            }

            return found;
        }
    }
}
