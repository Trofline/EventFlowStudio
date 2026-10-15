using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace EventFlowStudio.Services
{
    public class GithubUpdater
    {
        private readonly HttpClient _http;
        private readonly Action<string> _log;

        public GithubUpdater(Action<string> logAction)
        {
            _log = logAction;
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("User-Agent", "EventFlowStudio-AutoUpdater");
        }

        public async Task CheckAndUpdateAsync(string repo)
        {
            if (string.IsNullOrWhiteSpace(repo) || !repo.Contains("/")) return;

            try
            {
                _log($"Prüfe auf Updates via GitHub ({repo})...");
                string url = $"https://api.github.com/repos/{repo}/releases/latest";
                var release = await _http.GetFromJsonAsync<GithubRelease>(url);

                if (release != null && release.Assets != null && release.Assets.Length > 0)
                {
                    // Sehr simple Logik: Wenn ein Release gefunden wird, laden wir die erste Exe runter
                    // (Für Produktion würdest du hier die aktuelle Version mit release.TagName vergleichen)
                    string downloadUrl = release.Assets[0].BrowserDownloadUrl;
                    if (downloadUrl.EndsWith(".exe"))
                    {
                        _log($"Update gefunden: {release.TagName}. Lade herunter...");
                        byte[] fileBytes = await _http.GetByteArrayAsync(downloadUrl);

                        string newExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EventFlowStudio_Update.exe");
                        await File.WriteAllBytesAsync(newExePath, fileBytes);

                        _log("Update heruntergeladen. Starte Updater...");

                        // Startet das Update und schließt die aktuelle Instanz
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c ping 127.0.0.1 -n 2 > nul & move /y \"{newExePath}\" \"{Process.GetCurrentProcess().MainModule.FileName}\" & start \"\" \"{Process.GetCurrentProcess().MainModule.FileName}\"",
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        });

                        Environment.Exit(0);
                    }
                }
                else
                {
                    _log("Du bist auf der neuesten Version.");
                }
            }
            catch (Exception ex)
            {
                _log($"Update fehlgeschlagen: {ex.Message}");
            }
        }

        private class GithubRelease
        {
            [JsonPropertyName("tag_name")] public string TagName { get; set; } = string.Empty;
            [JsonPropertyName("assets")] public GithubAsset[]? Assets { get; set; }
        }

        private class GithubAsset
        {
            [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = string.Empty;
        }
    }
}