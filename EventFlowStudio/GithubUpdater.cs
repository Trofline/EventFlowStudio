using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace EventFlowStudio.Services
{
    public class GithubUpdater
    {
        private readonly Action<string> _log;
        private const string AppDataFolder = "EventFlowStudio";

        public GithubUpdater(Action<string> log)
        {
            _log = log;
        }

        // repoName sieht z.B. so aus: "Trofline/EventFlowStudio"
        public async Task CheckAndUpdateAsync(string repoName)
        {
            try
            {
                _log("Suche im Hintergrund nach Updates...");

                using var client = new HttpClient();
                // GitHub API verlangt zwingend einen User-Agent
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EventFlowStudio", "1.0"));

                var url = $"https://api.github.com/repos/{repoName}/releases/latest";
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode) return; // Wenn kein Release da ist, einfach ignorieren

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Entfernt nur ein führendes "v" vom Tag (z.B. "v1.0.1" -> "1.0.1")
                var latestVersionTag = NormalizeTag(root.GetProperty("tag_name").GetString());

                // Holt die aktuelle Version deiner eigenen gebauten .exe
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);

                if (Version.TryParse(latestVersionTag, out Version latest) &&
                    Version.TryParse(currentVersion, out Version current))
                {
                    if (latest > current)
                    {
                        _log($"Neue Version gefunden ({latestVersionTag})! Lade im Hintergrund herunter...");

                        var assets = root.GetProperty("assets");
                        string downloadUrl = string.Empty;
                        long expectedSize = -1;
                        string? expectedSha256 = null;

                        // Sucht in den GitHub Releases nach der .exe Datei
                        foreach (var asset in assets.EnumerateArray())
                        {
                            var name = asset.GetProperty("name").GetString();
                            if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                                if (asset.TryGetProperty("size", out var sizeProp) && sizeProp.ValueKind == JsonValueKind.Number)
                                    expectedSize = sizeProp.GetInt64();
                                // GitHub liefert (bei neueren Releases) einen Digest wie "sha256:abc..."
                                if (asset.TryGetProperty("digest", out var digestProp) && digestProp.ValueKind == JsonValueKind.String)
                                {
                                    var digest = digestProp.GetString();
                                    if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                                        expectedSha256 = digest.Substring("sha256:".Length);
                                }
                                break;
                            }
                        }

                        if (string.IsNullOrEmpty(downloadUrl))
                        {
                            _log("Kein .exe-Asset im neuesten Release gefunden. Update übersprungen.");
                        }
                        else if (!IsTrustedDownloadUrl(downloadUrl))
                        {
                            _log("Download-URL ist nicht vertrauenswürdig. Update abgebrochen.");
                        }
                        else
                        {
                            await PerformSeamlessUpdate(downloadUrl, expectedSize, expectedSha256);
                        }
                    }
                    else
                    {
                        _log($"EventFlow Studio ist aktuell (v{currentVersion}).");
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"Update-Check fehlgeschlagen: {ex.Message}");
            }
        }

        // Entfernt genau ein führendes "v"/"V", lässt den Rest des Tags unverändert
        internal static string? NormalizeTag(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            tag = tag.Trim();
            return tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag.Substring(1) : tag;
        }

        // Nur HTTPS-Downloads von GitHub zulassen
        private static bool IsTrustedDownloadUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);
        }

        // Prüft die heruntergeladene Datei: Größe, SHA-256 (falls bekannt) und Windows-EXE-Header ("MZ")
        private static string? ValidateDownload(string path, long expectedSize, string? expectedSha256)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0) return "Datei ist leer.";
            if (expectedSize > 0 && info.Length != expectedSize)
                return $"Dateigröße stimmt nicht ({info.Length} statt {expectedSize} Bytes).";

            using var fs = File.OpenRead(path);
            int b1 = fs.ReadByte(), b2 = fs.ReadByte();
            if (b1 != 'M' || b2 != 'Z') return "Datei ist keine gültige Windows-Anwendung.";

            if (!string.IsNullOrEmpty(expectedSha256))
            {
                fs.Position = 0;
                var actual = Convert.ToHexString(SHA256.HashData(fs));
                if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                    return "SHA-256-Prüfsumme stimmt nicht.";
            }
            return null;
        }

        private async Task PerformSeamlessUpdate(string downloadUrl, long expectedSize, string? expectedSha256)
        {
            string? newExe = null;
            try
            {
                // Die Pfade zur aktuellen und zur neuen temporären Datei
                string currentExe = Process.GetCurrentProcess().MainModule!.FileName;
                string currentDir = Path.GetDirectoryName(currentExe)!;
                newExe = Path.Combine(currentDir, "EventFlowStudio_Update.exe");

                // 1. Update herunterladen
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EventFlowStudio", "1.0"));
                    using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode)
                    {
                        _log($"Download fehlgeschlagen (HTTP {(int)response.StatusCode}). Update abgebrochen.");
                        return;
                    }
                    using var fs = new FileStream(newExe, FileMode.Create, FileAccess.Write, FileShare.None);
                    await response.Content.CopyToAsync(fs);
                }

                // 1b. Download prüfen, bevor irgendetwas ersetzt wird
                var validationError = ValidateDownload(newExe, expectedSize, expectedSha256);
                if (validationError != null)
                {
                    _log($"Update verworfen: {validationError}");
                    TryDelete(newExe);
                    return;
                }

                // 2. Sicherheits-Backup der Profile erstellen
                BackupProfiles();

                // 3. Unsichtbares Update-Skript erstellen
                string batPath = Path.Combine(currentDir, "update_eventflow.bat");

                // Dieses Skript wartet 2 Sekunden, tauscht die Dateien, startet die neue und löscht sich selbst
                string batContent = $@"
@echo off
timeout /t 2 /nobreak >nul
del ""{currentExe}""
rename ""{newExe}"" ""{Path.GetFileName(currentExe)}""
start """" ""{currentExe}""
del ""%~f0""
";
                File.WriteAllText(batPath, batContent);

                _log("Update erfolgreich geladen. Starte blitzschnell neu...");

                // 4. Update-Skript starten (komplett unsichtbar, kein schwarzes CMD-Fenster)
                Process.Start(new ProcessStartInfo
                {
                    FileName = batPath,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                // 5. Beende die aktuelle App SOFORT, damit das Skript die Datei überschreiben kann
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                _log($"Fehler beim Update-Prozess: {ex.Message}");
                if (newExe != null) TryDelete(newExe);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private void BackupProfiles()
        {
            try
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDataFolder);
                string configFile = Path.Combine(appData, "presets_config_v4.json");

                // Sichert die Profile als Backup ab
                string backupFile = Path.Combine(appData, $"presets_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json");

                if (File.Exists(configFile))
                {
                    File.Copy(configFile, backupFile, true);
                    _log("Sicherheits-Backup der Profile wurde angelegt.");
                }
            }
            catch (Exception ex) { _log($"Backup der Profile fehlgeschlagen: {ex.Message}"); }
        }
    }
}