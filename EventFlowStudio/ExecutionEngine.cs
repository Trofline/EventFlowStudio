using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using EventFlowStudio.Models;

namespace EventFlowStudio.Services
{
    public class ExecutionEngine
    {
        private const string GithubRepository = "DeinName/EventFlowStudio";
        private readonly string _appDataFolder;
        private readonly string _configFile;
        private readonly string _settingsFile;

        public ObservableCollection<Preset> Presets { get; set; } = new ObservableCollection<Preset>();
        public AppSettings Settings { get; set; } = new AppSettings();

        private readonly SystemMonitorService _monitor = new SystemMonitorService();
        private GlobalKeyboardHook? _keyboardHook;
        private GithubUpdater? _updater;

        public event Action<string>? LogMessage;
        public event Action? ToggleMainWindow; // NEU: Event für das Fenster
        public event Action<string, string>? NotificationRequested; // Titel, Nachricht -> Tray-Benachrichtigung

        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        // Windows-Standard-GUIDs der eingebauten Energiesparpläne
        private static readonly Dictionary<PowerPlanType, string> PowerPlanGuids = new()
        {
            { PowerPlanType.Balanced, "381b4222-f694-41f0-9685-ff5bb260df2e" },
            { PowerPlanType.HighPerformance, "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" },
            { PowerPlanType.PowerSaver, "a1841308-3541-4fab-bc81-f71556f20b4a" },
        };

        public ExecutionEngine()
        {
            _appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EventFlowStudio");
            if (!Directory.Exists(_appDataFolder)) Directory.CreateDirectory(_appDataFolder);

            _configFile = Path.Combine(_appDataFolder, "presets_config_v4.json");
            _settingsFile = Path.Combine(_appDataFolder, "app_settings.json");
        }

        public void Log(string message) => LogMessage?.Invoke(message);

        public void Initialize()
        {
            LoadSettings();
            LoadPresets();

            LocalizationService.Instance.CurrentLanguage = Settings.Language;

            _updater = new GithubUpdater(Log);
            if (Settings.AutoUpdate) _ = _updater.CheckAndUpdateAsync(GithubRepository);

            _keyboardHook = new GlobalKeyboardHook(OnGlobalKeyPressed);

            _monitor.ProcessStarted += name => CheckTriggers(TriggerType.ProcessStarted, name);
            _monitor.ProcessStopped += name => CheckTriggers(TriggerType.ProcessStopped, name);
            _monitor.BluetoothConnected += name => CheckTriggers(TriggerType.BluetoothConnected, name);
            _monitor.BluetoothDisconnected += name => CheckTriggers(TriggerType.BluetoothDisconnected, name);
            _monitor.UsbConnected += name => CheckTriggers(TriggerType.UsbDeviceConnected, name);

            _monitor.StartMonitoring();
            Log("EventFlow Engine gestartet.");

            CheckTriggers(TriggerType.AppStartup, "EventFlow");
        }

        private void OnGlobalKeyPressed(uint modifiers, uint vkCode)
        {
            // Prüfen, ob der Globale App-Hotkey gedrückt wurde
            if (Settings.ToggleHotkeyModifier != 0 && Settings.ToggleHotkeyModifier == modifiers && Settings.ToggleHotkeyVK == vkCode)
            {
                ToggleMainWindow?.Invoke();
            }

            foreach (var preset in Presets.Where(p => p.IsActive))
            {
                foreach (var trigger in preset.Triggers.Where(t => t.Type == TriggerType.GlobalHotkey))
                {
                    if (trigger.HotkeyModifier == modifiers && trigger.HotkeyVK == vkCode)
                    {
                        Log($"Globaler Hotkey gedrückt! Starte Profil: {preset.Name}");
                        ExecutePresetActions(preset);
                    }
                }
            }
        }

        private void CheckTriggers(TriggerType type, string targetName)
        {
            foreach (var preset in Presets.Where(p => p.IsActive))
            {
                foreach (var trigger in preset.Triggers.Where(t => t.Type == type))
                {
                    if (type == TriggerType.AppStartup || IsNameMatch(trigger.TargetName, targetName))
                    {
                        Log($"Trigger ausgelöst [{type} -> {targetName}] für Profil: {preset.Name}");
                        ExecutePresetActions(preset);
                    }
                }
            }

            if (type == TriggerType.ProcessStopped)
            {
                foreach (var preset in Presets.Where(p => p.IsActive))
                {
                    foreach (var trigger in preset.Triggers.Where(t => t.Type == TriggerType.ProcessStarted))
                    {
                        if (IsNameMatch(trigger.TargetName, targetName))
                        {
                            Log($"Hauptprozess beendet [{targetName}] für Profil: {preset.Name} -> Starte Clean-up...");
                            HandleProcessExitCleanup(preset);
                        }
                    }
                }
            }
        }

        private bool IsNameMatch(string triggerTarget, string actualTarget)
        {
            if (string.IsNullOrWhiteSpace(triggerTarget) || string.IsNullOrWhiteSpace(actualTarget)) return false;
            return actualTarget.Equals(triggerTarget, StringComparison.OrdinalIgnoreCase) ||
                   actualTarget.Contains(triggerTarget, StringComparison.OrdinalIgnoreCase);
        }

        public async void ExecutePresetActions(Preset preset)
        {
            foreach (var action in preset.Actions.Where(a => a.IsEnabled))
            {
                if (action.DelaySeconds > 0) await Task.Delay(action.DelaySeconds * 1000);

                try
                {
                    if (action.Type == ActionType.StartProcess)
                    {
                        var psi = new ProcessStartInfo { FileName = action.TargetPath, Arguments = action.Arguments, UseShellExecute = true, WindowStyle = (ProcessWindowStyle)action.LaunchState };
                        if (action.RunAsAdmin) psi.Verb = "runas";

                        var proc = Process.Start(psi);
                        if (proc != null) { action.LaunchedProcessId = proc.Id; Log($"Gestartet: {Path.GetFileName(action.TargetPath)} [PID: {proc.Id}]"); }
                    }
                    else if (action.Type == ActionType.KillProcess) KillProcessByNameOrPath(action.TargetPath);
                    else if (action.Type == ActionType.ChangePowerPlan) await ChangePowerPlanAsync(action.PowerPlan);
                    else if (action.Type == ActionType.SendWebhook) await SendWebhookAsync(action.WebhookUrl, preset);
                    else if (action.Type == ActionType.SystemNotification) ShowNotification(preset, action.NotificationMessage);
                }
                catch (Exception ex) { Log($"Fehler bei Aktion: {ex.Message}"); }
            }
        }

        private async void HandleProcessExitCleanup(Preset preset)
        {
            // Clean-up betrifft nur Prozess-Aktionen (Energieplan/Webhook/Benachrichtigung haben keinen Prozess)
            foreach (var action in preset.Actions.Where(a => a.IsEnabled && a.TerminateOnTriggerProcessExit &&
                                                             (a.Type == ActionType.StartProcess || a.Type == ActionType.KillProcess)))
            {
                if (action.ExitDelaySeconds > 0)
                {
                    Log($"Warte {action.ExitDelaySeconds} Sek. vor dem Beenden von {Path.GetFileName(action.TargetPath)}...");
                    await Task.Delay(action.ExitDelaySeconds * 1000);
                }

                bool killedByPid = false;
                if (action.LaunchedProcessId.HasValue)
                {
                    try
                    {
                        var proc = Process.GetProcessById(action.LaunchedProcessId.Value);
                        if (!proc.HasExited) { AggressiveKill(proc); Log($"Auto-Kill PID: {Path.GetFileName(action.TargetPath)}"); killedByPid = true; }
                    }
                    catch { }
                    action.LaunchedProcessId = null;
                }
                if (!killedByPid && !string.IsNullOrWhiteSpace(action.TargetPath)) KillProcessByNameOrPath(action.TargetPath);
            }
        }

        private async Task ChangePowerPlanAsync(PowerPlanType plan)
        {
            if (!PowerPlanGuids.TryGetValue(plan, out var guid)) { Log($"Unbekannter Energieplan: {plan}"); return; }

            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = $"/setactive {guid}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) { Log("Energieplan: powercfg konnte nicht gestartet werden."); return; }

            var stdOutTask = proc.StandardOutput.ReadToEndAsync();
            var stdErrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            string output = ((await stdErrTask) + " " + (await stdOutTask)).Trim();

            if (proc.ExitCode == 0) Log($"Energieplan aktiviert: {plan}");
            else Log($"Energieplan {plan} konnte nicht aktiviert werden (powercfg Code {proc.ExitCode}). " +
                     $"Auf manchen Geräten ist dieser Plan nicht vorhanden. {output}".Trim());
        }

        private async Task SendWebhookAsync(string url, Preset preset)
        {
            if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                Log($"Webhook übersprungen: ungültige URL \"{url}\"");
                return;
            }

            var payload = new
            {
                source = "EventFlowStudio",
                preset = preset.Name,
                presetId = preset.Id,
                machine = Environment.MachineName,
                timestamp = DateTimeOffset.Now.ToString("o")
            };

            using var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
            try
            {
                using var response = await _httpClient.PostAsync(uri, content);
                if (response.IsSuccessStatusCode) Log($"Webhook gesendet: {uri.Host} [{(int)response.StatusCode}]");
                else Log($"Webhook fehlgeschlagen: {uri.Host} antwortete mit {(int)response.StatusCode} {response.ReasonPhrase}");
            }
            catch (TaskCanceledException) { Log($"Webhook Timeout: {uri.Host} hat nicht innerhalb von 10 Sek. geantwortet."); }
            catch (HttpRequestException ex) { Log($"Webhook fehlgeschlagen: {uri.Host} ({ex.Message})"); }
        }

        private void ShowNotification(Preset preset, string message)
        {
            string text = string.IsNullOrWhiteSpace(message) ? "Aktion ausgeführt." : message;
            if (NotificationRequested == null) { Log("Benachrichtigung nicht möglich: kein Tray-Icon verfügbar."); return; }

            NotificationRequested.Invoke(preset.Name, text);
            Log($"Benachrichtigung angezeigt: {text}");
        }

        private void KillProcessByNameOrPath(string targetPath)
        {
            try
            {
                string exactName = Path.GetFileNameWithoutExtension(targetPath);
                var processes = Process.GetProcessesByName(exactName).ToList();
                if (!processes.Any()) processes = Process.GetProcesses().Where(p => p.ProcessName.ToLower().Contains(exactName.Replace(" ", "").ToLower())).ToList();
                foreach (var p in processes) { AggressiveKill(p); Log($"Beendet (Smart-Kill): {p.ProcessName}"); }
            }
            catch { }
        }

        private void AggressiveKill(Process p)
        {
            try { p.Kill(true); }
            catch (Win32Exception)
            {
                Process.Start(new ProcessStartInfo { FileName = "taskkill", Arguments = $"/F /T /PID {p.Id}", CreateNoWindow = true, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden, Verb = "runas" });
            }
            catch { }
        }

        private JsonSerializerOptions GetJsonOptions()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        public void SavePresets()
        {
            File.WriteAllText(_configFile, JsonSerializer.Serialize(Presets, GetJsonOptions()));
            File.WriteAllText(_settingsFile, JsonSerializer.Serialize(Settings, GetJsonOptions()));
            Log("Konfiguration & Settings gespeichert.");
        }

        public void LoadPresets()
        {
            if (File.Exists(_configFile))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<ObservableCollection<Preset>>(File.ReadAllText(_configFile), GetJsonOptions());
                    if (loaded != null) { Presets.Clear(); foreach (var p in loaded) Presets.Add(p); }
                }
                catch { }
            }

            foreach (var p in Presets)
            {
                if (p.Triggers == null) p.Triggers = new ObservableCollection<TriggerCondition>();
                if (!p.Triggers.Any()) p.Triggers.Add(new TriggerCondition());
            }

            if (!Presets.Any())
            {
                var defaultPreset = new Preset { Name = "Standard Profil" };
                defaultPreset.Triggers.Add(new TriggerCondition { Type = TriggerType.ProcessStarted, TargetName = "ac_client" });
                Presets.Add(defaultPreset);
                SavePresets();
            }
        }

        public void LoadSettings()
        {
            if (File.Exists(_settingsFile))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsFile), GetJsonOptions());
                    if (loaded != null) Settings = loaded;
                }
                catch { }
            }
        }

        public void ExportPresets(string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(Presets, GetJsonOptions()));
            Log($"Profile exportiert nach: {path}");
        }

        public void ImportPresets(string path)
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<ObservableCollection<Preset>>(File.ReadAllText(path), GetJsonOptions());
                if (loaded != null)
                {
                    foreach (var p in loaded) { p.Id = Guid.NewGuid().ToString(); Presets.Add(p); }
                    SavePresets();
                    Log($"Profile importiert von: {path}");
                }
            }
            catch (Exception ex) { Log($"Fehler beim Importieren: {ex.Message}"); }
        }
    }
}