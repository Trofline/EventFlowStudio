using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
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

        // repoName sieht z.B. so aus: "DeinName/EventFlowStudio"
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

                // Wirft das "v" vom Tag weg (z.B. "v1.0.1" -> "1.0.1")
                var latestVersionTag = root.GetProperty("tag_name").GetString()?.Replace("v", "");

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

                        // Sucht in den GitHub Releases nach der .exe Datei
                        foreach (var asset in assets.EnumerateArray())
                        {
                            var name = asset.GetProperty("name").GetString();
                            if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                downloadUrl = asset.GetProperty("browser_download_url").GetString()!;
                                break;
                            }
                        }

                        if (!string.IsNullOrEmpty(downloadUrl))
                        {
                            await PerformSeamlessUpdate(downloadUrl);
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

        private async Task PerformSeamlessUpdate(string downloadUrl)
        {
            try
            {
                // Die Pfade zur aktuellen und zur neuen temporären Datei
                string currentExe = Process.GetCurrentProcess().MainModule!.FileName;
                string currentDir = Path.GetDirectoryName(currentExe)!;
                string newExe = Path.Combine(currentDir, "EventFlowStudio_Update.exe");

                // 1. Update herunterladen
                using var client = new HttpClient();
                var response = await client.GetAsync(downloadUrl);
                using var fs = new FileStream(newExe, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs);
                fs.Close();

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
            }
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
            catch { }
        }
    }
}