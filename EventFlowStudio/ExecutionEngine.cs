using System;
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
                }
                catch (Exception ex) { Log($"Fehler bei Aktion: {ex.Message}"); }
            }
        }

        private async void HandleProcessExitCleanup(Preset preset)
        {
            foreach (var action in preset.Actions.Where(a => a.IsEnabled && a.TerminateOnTriggerProcessExit))
            {
                // NEU: Verzögerung beim Beenden (Exit-Delay) abwarten
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

            // Fallback für ältere Configs ohne Multi-Trigger
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