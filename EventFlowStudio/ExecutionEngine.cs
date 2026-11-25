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
using System.Threading;
using System.Threading.Tasks;
using EventFlowStudio.Models;
using Microsoft.Win32;

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
        private SynchronizationContext? _uiContext;

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
            // Alle Trigger werden auf dem UI-Thread ausgewertet. So ändert niemand die Profile, während wir sie durchlaufen.
            _uiContext = SynchronizationContext.Current;

            LoadSettings();
            LoadPresets();

            LocalizationService.Instance.CurrentLanguage = Settings.Language;

            _updater = new GithubUpdater(Log);
            if (Settings.AutoUpdate) _ = _updater.CheckAndUpdateAsync(GithubRepository);

            _keyboardHook = new GlobalKeyboardHook(OnGlobalKeyPressed);

            _monitor.ProcessStarted += name => Post(() => CheckTriggers(TriggerType.ProcessStarted, name));
            _monitor.ProcessStopped += name => Post(() => CheckTriggers(TriggerType.ProcessStopped, name));
            _monitor.BluetoothConnected += name => Post(() => CheckTriggers(TriggerType.BluetoothConnected, name));
            _monitor.BluetoothDisconnected += name => Post(() => CheckTriggers(TriggerType.BluetoothDisconnected, name));
            _monitor.UsbConnected += name => Post(() => CheckTriggers(TriggerType.UsbDeviceConnected, name));
            _monitor.UsbDisconnected += name => Post(() => CheckTriggers(TriggerType.UsbDeviceDisconnected, name));
            _monitor.ForegroundAppChanged += name => Post(() => CheckTriggers(TriggerType.ForegroundApp, name));
            _monitor.NetworkConnected += name => Post(() => CheckTriggers(TriggerType.NetworkConnected, name));
            _monitor.NetworkDisconnected += name => Post(() => CheckTriggers(TriggerType.NetworkDisconnected, name));
            _monitor.PowerSourceChanged += onAc => Post(() => CheckTriggers(onAc ? TriggerType.PowerPluggedIn : TriggerType.PowerUnplugged, onAc ? "Netzteil" : "Akku"));
            _monitor.StatusUpdated += status => Post(() => OnStatusUpdated(status));

            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            RefreshUsedTriggerTypes();
            _monitor.StartMonitoring();
            Log("EventFlow Engine gestartet.");

            CheckTriggers(TriggerType.AppStartup, "EventFlow");
        }

        public void Shutdown()
        {
            _monitor.StopMonitoring();
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _keyboardHook?.Dispose();
        }

        private void Post(Action action)
        {
            void Run() { try { action(); } catch (Exception ex) { Log($"Fehler bei der Trigger-Auswertung: {ex.Message}"); } }
            if (_uiContext != null) _uiContext.Post(_ => Run(), null);
            else Run();
        }

        private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock) Post(() => CheckTriggers(TriggerType.SessionLocked, "Sperren"));
            else if (e.Reason == SessionSwitchReason.SessionUnlock) Post(() => CheckTriggers(TriggerType.SessionUnlocked, "Entsperren"));
        }

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) Post(() => CheckTriggers(TriggerType.SystemResumed, "Aufwachen"));
        }

        // Läuft alle 1,5 Sek. auf dem UI-Thread: wertet Uhrzeit, Intervall, Leerlauf und Akku aus
        private void OnStatusUpdated(SystemStatus status)
        {
            RefreshUsedTriggerTypes();
            var now = DateTime.Now;
            foreach (var preset in Presets.Where(p => p.IsActive).ToList())
            {
                foreach (var trigger in preset.Triggers.Where(t => TriggerEvaluator.IsPolled(t.Type)).ToList())
                {
                    if (TriggerEvaluator.ShouldFire(trigger, now, status))
                    {
                        Log($"Trigger ausgelöst [{trigger.Type} {trigger.TargetName}] für Profil: {preset.Name}");
                        ExecutePresetActions(preset);
                    }
                }
            }
        }

        // Der Monitor fragt WMI/WLAN nur ab, wenn ein aktives Profil den passenden Trigger nutzt
        private void RefreshUsedTriggerTypes()
        {
            _monitor.UsedTriggerTypes = Presets.Where(p => p.IsActive).SelectMany(p => p.Triggers).Select(t => t.Type).ToHashSet();
        }

        private void OnGlobalKeyPressed(uint modifiers, uint vkCode)
        {
            // Prüfen, ob der Globale App-Hotkey gedrückt wurde
            if (Settings.ToggleHotkeyModifier != 0 && Settings.ToggleHotkeyModifier == modifiers && Settings.ToggleHotkeyVK == vkCode)
            {
                ToggleMainWindow?.Invoke();
            }

            foreach (var preset in Presets.Where(p => p.IsActive).ToList())
            {
                foreach (var trigger in preset.Triggers.Where(t => t.Type == TriggerType.GlobalHotkey).ToList())
                {
                    if (trigger.HotkeyModifier == modifiers && trigger.HotkeyVK == vkCode)
                    {
                        Log($"Globaler Hotkey gedrückt! Starte Profil: {preset.Name}");
                        // Nicht direkt im Tastatur-Hook arbeiten: Windows entfernt Hooks, die zu lange brauchen
                        Post(() => ExecutePresetActions(preset));
                    }
                }
            }
        }

        private void CheckTriggers(TriggerType type, string targetName)
        {
            foreach (var preset in Presets.Where(p => p.IsActive).ToList())
            {
                foreach (var trigger in preset.Triggers.Where(t => t.Type == type).ToList())
                {
                    if (IsTriggerMatch(trigger, targetName))
                    {
                        Log($"Trigger ausgelöst [{type} -> {targetName}] für Profil: {preset.Name}");
                        ExecutePresetActions(preset);
                    }
                }
            }

            if (type == TriggerType.ProcessStopped)
            {
                foreach (var preset in Presets.Where(p => p.IsActive).ToList())
                {
                    foreach (var trigger in preset.Triggers.Where(t => t.Type == TriggerType.ProcessStarted).ToList())
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

        private static bool IsTriggerMatch(TriggerCondition trigger, string actual) => trigger.Type switch
        {
            // Ereignisse ohne Namen: jeder Trigger dieses Typs passt
            TriggerType.AppStartup or TriggerType.PowerPluggedIn or TriggerType.PowerUnplugged or
            TriggerType.SessionLocked or TriggerType.SessionUnlocked or TriggerType.SystemResumed => true,
            TriggerType.NetworkConnected or TriggerType.NetworkDisconnected => TriggerEvaluator.IsNetworkMatch(trigger.TargetName, actual),
            _ => IsNameMatch(trigger.TargetName, actual),
        };

        public static bool IsNameMatch(string triggerTarget, string actualTarget)
        {
            if (string.IsNullOrWhiteSpace(triggerTarget) || string.IsNullOrWhiteSpace(actualTarget)) return false;
            return actualTarget.Equals(triggerTarget, StringComparison.OrdinalIgnoreCase) ||
                   actualTarget.Contains(triggerTarget, StringComparison.OrdinalIgnoreCase);
        }

        public async void ExecutePresetActions(Preset preset)
        {
            // Kopie der Liste: der Nutzer darf Aktionen bearbeiten, während eine Verzögerung läuft
            foreach (var action in preset.Actions.Where(a => a.IsEnabled).ToList())
            {
                try { if (action.DelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(action.DelaySeconds)); }
                catch (ArgumentOutOfRangeException) { }

                try
                {
                    if (action.Type == ActionType.StartProcess)
                    {
                        if (string.IsNullOrWhiteSpace(action.TargetPath)) { Log("Programm starten übersprungen: kein Pfad angegeben."); continue; }
                        var psi = new ProcessStartInfo { FileName = action.TargetPath.Trim().Trim('"'), Arguments = action.Arguments, UseShellExecute = true, WindowStyle = (ProcessWindowStyle)action.LaunchState };
                        if (action.RunAsAdmin) psi.Verb = "runas";

                        var proc = Process.Start(psi);
                        if (proc != null) { action.LaunchedProcessId = proc.Id; Log($"Gestartet: {Path.GetFileName(action.TargetPath)} [PID: {proc.Id}]"); }
                    }
                    else if (action.Type == ActionType.KillProcess) KillProcessByNameOrPath(action.TargetPath);
                    else if (action.Type == ActionType.ChangePowerPlan) await ChangePowerPlanAsync(action.PowerPlan);
                    else if (action.Type == ActionType.SendWebhook) await SendWebhookAsync(action.WebhookUrl, preset);
                    else if (action.Type == ActionType.SystemNotification) ShowNotification(preset, action.NotificationMessage);
                    else if (action.Type == ActionType.OpenUrl) OpenUrl(action.TargetPath);
                    else if (action.Type == ActionType.RunCommand) await RunCommandAsync(action);
                    else if (action.Type == ActionType.CloseProcess) CloseProcessGracefully(action.TargetPath);
                    else if (action.Type == ActionType.AudioControl) Log(SystemActions.RunAudioCommand(action.AudioCommand, action.VolumePercent));
                    else if (action.Type == ActionType.MediaControl) { SystemActions.PressMediaKey(action.MediaKey); Log($"Medientaste: {action.MediaKey}"); }
                    else if (action.Type == ActionType.SystemPower) Log(SystemActions.RunPowerCommand(action.SystemPowerCommand));
                }
                catch (Exception ex) { Log($"Fehler bei Aktion: {ex.Message}"); }
            }
        }

        private async void HandleProcessExitCleanup(Preset preset)
        {
            // Clean-up betrifft nur Prozess-Aktionen (Energieplan/Webhook/Benachrichtigung haben keinen Prozess)
            foreach (var action in preset.Actions.Where(a => a.IsEnabled && a.TerminateOnTriggerProcessExit &&
                                                             (a.Type == ActionType.StartProcess || a.Type == ActionType.KillProcess)).ToList())
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

        private void OpenUrl(string target)
        {
            string url = target?.Trim() ?? string.Empty;
            if (url.Length == 0) { Log("URL öffnen übersprungen: keine Adresse angegeben."); return; }
            // "example.com" ohne Schema als Webseite behandeln
            if (!url.Contains(':') && !File.Exists(url) && !Directory.Exists(url) && url.Contains('.')) url = "https://" + url;
            using (Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true })) { }
            Log($"Geöffnet: {url}");
        }

        private async Task RunCommandAsync(ActionItem action)
        {
            string command = action.TargetPath?.Trim() ?? string.Empty;
            if (command.Length == 0) { Log("Befehl übersprungen: kein Befehl angegeben."); return; }

            // -EncodedCommand umgeht alle Probleme mit Anführungszeichen im Befehl
            string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command));
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };

            if (action.RunAsAdmin)
            {
                // "runas" geht nur mit ShellExecute, dann ohne Ausgabe
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                using (Process.Start(psi)) { }
                Log($"Befehl als Admin gestartet: {Shorten(command)}");
                return;
            }

            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using var proc = Process.Start(psi);
            if (proc == null) { Log("Befehl konnte nicht gestartet werden."); return; }

            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                Log($"Befehl läuft länger als 60 Sek., die nächsten Aktionen starten trotzdem: {Shorten(command)}");
                return;
            }

            string output = ((await outTask) + " " + (await errTask)).Trim();
            if (proc.ExitCode == 0) Log($"Befehl ausgeführt: {Shorten(command)}{(output.Length > 0 ? " -> " + Shorten(output) : "")}");
            else Log($"Befehl fehlgeschlagen (Code {proc.ExitCode}): {Shorten(command)} {Shorten(output)}".Trim());
        }

        private static string Shorten(string text)
        {
            string oneLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return oneLine.Length <= 120 ? oneLine : oneLine.Substring(0, 117) + "...";
        }

        private void CloseProcessGracefully(string target)
        {
            var processes = FindProcesses(target);
            if (processes.Count == 0) { Log($"Schließen: kein laufender Prozess für \"{target}\" gefunden."); return; }
            foreach (var p in processes)
            {
                using (p)
                {
                    try
                    {
                        // Wie ein Klick auf X: die App kann noch speichern oder nachfragen
                        if (p.CloseMainWindow()) Log($"Schließen angefordert: {p.ProcessName}");
                        else Log($"{p.ProcessName} hat kein Fenster zum Schließen (nutze \"Prozess beenden\" zum Erzwingen).");
                    }
                    catch (Exception ex) { Log($"Fehler beim Schließen von {p.ProcessName}: {ex.Message}"); }
                }
            }
        }

        /// <summary>
        /// Sucht Prozesse nach Pfad oder Name. Leere Eingaben liefern NIE Treffer
        /// (vorher passte ein leerer Name auf jeden Prozess und "Prozess beenden" hätte alles beendet).
        /// </summary>
        private static List<Process> FindProcesses(string target)
        {
            string exactName = Path.GetFileNameWithoutExtension(target?.Trim().Trim('"') ?? string.Empty);
            if (string.IsNullOrWhiteSpace(exactName)) return new List<Process>();

            var processes = Process.GetProcessesByName(exactName).ToList();
            if (processes.Any()) return processes;

            string needle = exactName.Replace(" ", "");
            var result = new List<Process>();
            foreach (var p in Process.GetProcesses())
            {
                if (p.Id != Environment.ProcessId && p.ProcessName.Contains(needle, StringComparison.OrdinalIgnoreCase)) result.Add(p);
                else p.Dispose();
            }
            return result;
        }

        private void KillProcessByNameOrPath(string targetPath)
        {
            try
            {
                var processes = FindProcesses(targetPath);
                if (processes.Count == 0) { Log($"Beenden: kein laufender Prozess für \"{targetPath}\" gefunden."); return; }
                foreach (var p in processes) { using (p) { string name = p.ProcessName; AggressiveKill(p); Log($"Beendet (Smart-Kill): {name}"); } }
            }
            catch (Exception ex) { Log($"Fehler beim Beenden von {targetPath}: {ex.Message}"); }
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