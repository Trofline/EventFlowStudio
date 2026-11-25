using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
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
        private const string GithubRepository = "Trofline/EventFlowStudio";

        // Mehrere passende Ereignisse im selben Moment (z.B. "steam" passt auf steam UND steamwebhelper)
        // sollen ein Profil nur einmal starten.
        private static readonly TimeSpan MinTimeBetweenRuns = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan ScheduleTickInterval = TimeSpan.FromSeconds(5);

        private readonly string _appDataFolder;
        private readonly string _configFile;
        private readonly object _saveLock = new();
        private readonly string _settingsFile;

        public ObservableCollection<Preset> Presets { get; set; } = new ObservableCollection<Preset>();
        public AppSettings Settings { get; set; } = new AppSettings();

        private readonly SystemMonitorService _monitor = new SystemMonitorService();
        private GlobalKeyboardHook? _keyboardHook;
        private GithubUpdater? _updater;
        private SynchronizationContext? _uiContext;
        private System.Threading.Timer? _scheduleTimer;
        private bool _sessionEventsHooked;

        // Laufzeit-Zustand (wird nicht gespeichert)
        private readonly Dictionary<string, DateTime> _lastRun = new();
        private readonly Dictionary<string, DateTime> _lastCleanup = new();
        private readonly Dictionary<TriggerCondition, DateTime> _timeOfDayFired = new();
        private readonly Dictionary<TriggerCondition, DateTime> _intervalNextRun = new();
        private readonly HashSet<TriggerCondition> _idleFired = new();
        private readonly HashSet<TriggerCondition> _wasAway = new();
        private readonly HashSet<TriggerCondition> _batteryLowFired = new();
        private bool? _lastOnAcPower;

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
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EventFlowStudio"))
        {
        }

        // Erlaubt einen eigenen Datenordner (z.B. für Tests)
        public ExecutionEngine(string appDataFolder)
        {
            _appDataFolder = appDataFolder;
            if (!Directory.Exists(_appDataFolder)) Directory.CreateDirectory(_appDataFolder);

            _configFile = Path.Combine(_appDataFolder, "presets_config_v4.json");
            _settingsFile = Path.Combine(_appDataFolder, "app_settings.json");
        }

        public void Log(string message) => LogMessage?.Invoke(message);

        public void Initialize()
        {
            // Initialize läuft auf dem UI-Thread. Alle Ereignisse aus Hintergrund-Threads werden dorthin
            // umgeleitet, weil Presets eine an die Oberfläche gebundene ObservableCollection ist.
            _uiContext = SynchronizationContext.Current;

            LoadSettings();
            LoadPresets();

            LocalizationService.Instance.CurrentLanguage = Settings.Language;

            _updater = new GithubUpdater(Log);
            if (Settings.AutoUpdate) _ = _updater.CheckAndUpdateAsync(GithubRepository);

            _keyboardHook = new GlobalKeyboardHook(OnGlobalKeyPressed);

            _monitor.ProcessStarted += name => RunOnUi(() => CheckTriggers(TriggerType.ProcessStarted, name));
            _monitor.ProcessStopped += name => RunOnUi(() => CheckTriggers(TriggerType.ProcessStopped, name));
            _monitor.BluetoothConnected += name => RunOnUi(() => CheckTriggers(TriggerType.BluetoothConnected, name));
            _monitor.BluetoothDisconnected += name => RunOnUi(() => CheckTriggers(TriggerType.BluetoothDisconnected, name));
            _monitor.UsbConnected += name => RunOnUi(() => CheckTriggers(TriggerType.UsbDeviceConnected, name));
            _monitor.UsbDisconnected += name => RunOnUi(() => CheckTriggers(TriggerType.UsbDeviceDisconnected, name));
            _monitor.ForegroundAppChanged += name => RunOnUi(() => CheckTriggers(TriggerType.ForegroundApp, name));
            _monitor.NetworkConnected += name => RunOnUi(() => CheckTriggers(TriggerType.NetworkConnected, name));
            _monitor.NetworkDisconnected += name => RunOnUi(() => CheckTriggers(TriggerType.NetworkDisconnected, name));
            _monitor.Error += msg => RunOnUi(() => Log(msg));

            RefreshUsedTriggerTypes();
            _monitor.StartMonitoring();

            try
            {
                SystemEvents.SessionSwitch += OnSessionSwitch;
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
                _sessionEventsHooked = true;
            }
            catch (Exception ex) { Log($"Sperr-/Entsperr-Ereignisse nicht verfügbar: {ex.Message}"); }

            _scheduleTimer = new System.Threading.Timer(_ => RunOnUi(OnScheduleTick), null, ScheduleTickInterval, ScheduleTickInterval);

            Log("EventFlow Engine gestartet.");

            CheckTriggers(TriggerType.AppStartup, "EventFlow");
        }

        // Beendet Überwachung, Hook und Timer sauber (beim Schließen der App)
        public void Shutdown()
        {
            _monitor.StopMonitoring();
            _scheduleTimer?.Dispose();
            _scheduleTimer = null;
            _keyboardHook?.Dispose();
            _keyboardHook = null;
            if (_sessionEventsHooked)
            {
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                _sessionEventsHooked = false;
            }
        }

        private void RunOnUi(Action action)
        {
            void Safe()
            {
                try { action(); }
                catch (Exception ex) { Log($"Fehler bei der Trigger-Auswertung: {ex.Message}"); }
            }

            if (_uiContext == null || SynchronizationContext.Current == _uiContext) Safe();
            else _uiContext.Post(_ => Safe(), null);
        }

        private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock)
                RunOnUi(() => CheckTriggers(TriggerType.SessionLocked, "Windows"));
            else if (e.Reason == SessionSwitchReason.SessionUnlock)
                RunOnUi(() => CheckTriggers(TriggerType.SessionUnlocked, "Windows"));
        }

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) RunOnUi(() => CheckTriggers(TriggerType.SystemResumed, "Windows"));
        }

        // Der Monitor fragt Bluetooth/USB (WMI), WLAN (netsh) und die Vordergrund-App nur ab,
        // wenn ein aktives Profil den passenden Trigger nutzt. Vorher liefen die WMI-Abfragen immer alle 1,5 Sek.
        private void RefreshUsedTriggerTypes()
        {
            _monitor.UsedTriggerTypes = Presets.Where(p => p.IsActive).SelectMany(p => p.Triggers).Select(t => t.Type).ToHashSet();
        }

        private void OnGlobalKeyPressed(uint modifiers, uint vkCode)
        {
            // Prüfen, ob der Globale App-Hotkey gedrückt wurde
            if (Settings.ToggleHotkeyVK != 0 && Settings.ToggleHotkeyModifier == modifiers && Settings.ToggleHotkeyVK == vkCode)
            {
                ToggleMainWindow?.Invoke();
            }

            foreach (var preset in Presets.Where(p => p.IsActive).ToList())
            {
                if (preset.Triggers.Any(t => t.Type == TriggerType.GlobalHotkey && t.HotkeyVK != 0 && t.HotkeyModifier == modifiers && t.HotkeyVK == vkCode))
                {
                    TryRunPreset(preset, "Globaler Hotkey");
                }
            }
        }

        private void CheckTriggers(TriggerType type, string targetName)
        {
            // ToList: Aktionen können Profile aktivieren/deaktivieren, während wir hier durchlaufen
            foreach (var preset in Presets.Where(p => p.IsActive).ToList())
            {
                if (preset.Triggers.Any(t => t.Type == type && IsTriggerMatch(t, targetName)))
                {
                    TryRunPreset(preset, $"{type} -> {targetName}");
                }
            }

            if (type == TriggerType.ProcessStopped)
            {
                foreach (var preset in Presets.Where(p => p.IsActive).ToList())
                {
                    if (!preset.Triggers.Any(t => t.Type == TriggerType.ProcessStarted && IsNameMatch(t.TargetName, targetName))) continue;
                    if (IsWithin(_lastCleanup, preset.Id, MinTimeBetweenRuns)) continue;
                    _lastCleanup[preset.Id] = DateTime.Now;

                    Log($"Hauptprozess beendet [{targetName}] für Profil: {preset.Name} -> Starte Clean-up...");
                    HandleProcessExitCleanup(preset);
                }
            }
        }

        private static bool IsTriggerMatch(TriggerCondition trigger, string actual) => trigger.Type switch
        {
            // Ereignisse ohne Wert: jeder Trigger dieses Typs passt
            TriggerType.AppStartup or TriggerType.SessionLocked or TriggerType.SessionUnlocked or
            TriggerType.PowerPluggedIn or TriggerType.PowerUnplugged or TriggerType.SystemResumed => true,
            TriggerType.NetworkConnected or TriggerType.NetworkDisconnected => IsNetworkMatch(trigger.TargetName, actual),
            _ => IsNameMatch(trigger.TargetName, actual),
        };

        /// <summary>Netzwerk-Trigger: leeres Feld = jede Verbindung (Ereignis "*"), sonst Teil des WLAN-Namens.</summary>
        public static bool IsNetworkMatch(string? triggerTarget, string actual)
        {
            if (string.IsNullOrWhiteSpace(triggerTarget)) return actual == SystemMonitorService.AnyNetwork;
            return actual != SystemMonitorService.AnyNetwork && IsNameMatch(triggerTarget, actual);
        }

        // Zeitgesteuerte Trigger (Uhrzeit, Intervall, Inaktivität). Läuft alle 5 Sek. auf dem UI-Thread.
        private void OnScheduleTick()
        {
            RefreshUsedTriggerTypes();

            var now = DateTime.Now;
            double idleSeconds = GetIdleSeconds();
            var power = SystemActions.GetPowerStatus();

            // Netzteil an/ab als Ereignis (nur Wechsel, nicht der Zustand beim Start)
            if (_lastOnAcPower.HasValue && _lastOnAcPower.Value != power.OnAcPower)
                CheckTriggers(power.OnAcPower ? TriggerType.PowerPluggedIn : TriggerType.PowerUnplugged, power.OnAcPower ? "Netzteil" : "Akku");
            _lastOnAcPower = power.OnAcPower;

            foreach (var preset in Presets.Where(p => p.IsActive).ToList())
            {
                foreach (var trigger in preset.Triggers.ToList())
                {
                    switch (trigger.Type)
                    {
                        case TriggerType.TimeOfDay:
                            foreach (var time in ParseTimes(trigger.TargetName))
                            {
                                var due = now.Date + time;
                                // Feuert einmal pro Tag und Uhrzeit, auch wenn der Tick ein paar Sekunden später kommt
                                if (now >= due && now < due.AddMinutes(1) &&
                                    (!_timeOfDayFired.TryGetValue(trigger, out var last) || last != due))
                                {
                                    _timeOfDayFired[trigger] = due;
                                    TryRunPreset(preset, $"Uhrzeit {time:hh\\:mm}");
                                }
                            }
                            break;

                        case TriggerType.Interval:
                            if (!TryParseMinutes(trigger.TargetName, out double minutes)) break;
                            if (!_intervalNextRun.TryGetValue(trigger, out var next) || next > now.AddMinutes(minutes))
                            {
                                // Erster Lauf erst nach Ablauf des Intervalls (auch nach Änderung des Werts)
                                _intervalNextRun[trigger] = now.AddMinutes(minutes);
                            }
                            else if (now >= next)
                            {
                                _intervalNextRun[trigger] = now.AddMinutes(minutes);
                                TryRunPreset(preset, $"Intervall {minutes.ToString(CultureInfo.InvariantCulture)} Min.");
                            }
                            break;

                        case TriggerType.UserIdle:
                            if (!TryParseMinutes(trigger.TargetName, out double idleMinutes) || idleSeconds < 0) break;
                            if (idleSeconds >= idleMinutes * 60)
                            {
                                // Nur einmal pro Inaktivitäts-Phase
                                if (_idleFired.Add(trigger)) TryRunPreset(preset, $"Inaktiv seit {idleMinutes.ToString(CultureInfo.InvariantCulture)} Min.");
                            }
                            else _idleFired.Remove(trigger);
                            break;

                        case TriggerType.UserReturned:
                            if (!TryParseMinutes(trigger.TargetName, out double awayMinutes) || idleSeconds < 0) break;
                            if (idleSeconds >= awayMinutes * 60) _wasAway.Add(trigger);
                            // Erste Eingabe nach der Pause (Tick alle 5 Sek., daher < 10 Sek.)
                            else if (idleSeconds < 10 && _wasAway.Remove(trigger))
                                TryRunPreset(preset, $"Zurück nach mind. {awayMinutes.ToString(CultureInfo.InvariantCulture)} Min.");
                            break;

                        case TriggerType.BatteryLow:
                            {
                                if (!power.HasBattery || power.BatteryPercent < 0) break;
                                double limit = TryParseMinutes(trigger.TargetName, out double pct) ? pct : 20;
                                if (power.OnAcPower || power.BatteryPercent > limit) { _batteryLowFired.Remove(trigger); break; }
                                // Nur einmal, bis wieder geladen wird
                                if (_batteryLowFired.Add(trigger)) TryRunPreset(preset, $"Akku bei {power.BatteryPercent} %");
                                break;
                            }
                    }
                }
            }
        }

        // "07:30" oder mehrere: "07:30, 18:00"
        public static List<TimeSpan> ParseTimes(string? text)
        {
            var result = new List<TimeSpan>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (var part in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (TimeSpan.TryParseExact(part.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var t) && t < TimeSpan.FromDays(1))
                    result.Add(t);
            }
            return result;
        }

        public static bool TryParseMinutes(string? text, out double minutes)
        {
            minutes = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string normalized = text.Trim().Replace(',', '.');
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out minutes) && minutes >= 0.1;
        }

        private static bool IsWithin(Dictionary<string, DateTime> map, string key, TimeSpan window) =>
            map.TryGetValue(key, out var last) && DateTime.Now - last < window;

        private void TryRunPreset(Preset preset, string reason)
        {
            var window = TimeSpan.FromSeconds(Math.Max(preset.CooldownSeconds, MinTimeBetweenRuns.TotalSeconds));
            if (IsWithin(_lastRun, preset.Id, window))
            {
                if (preset.CooldownSeconds > 0) Log($"Profil {preset.Name} übersprungen (Cooldown {preset.CooldownSeconds} Sek.)");
                return;
            }
            _lastRun[preset.Id] = DateTime.Now;

            Log($"Trigger ausgelöst [{reason}] für Profil: {preset.Name}");
            ExecutePresetActions(preset);
        }

        public static bool IsNameMatch(string triggerTarget, string actualTarget)
        {
            if (string.IsNullOrWhiteSpace(triggerTarget) || string.IsNullOrWhiteSpace(actualTarget)) return false;

            // Prozessnamen haben kein ".exe". Wer "spiel.exe" einträgt, soll trotzdem einen Treffer bekommen.
            string target = triggerTarget.Trim();
            if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && target.Length > 4) target = target[..^4];

            return actualTarget.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                   actualTarget.Contains(target, StringComparison.OrdinalIgnoreCase);
        }

        public async void ExecutePresetActions(Preset preset)
        {
            try
            {
                // Kopie: Der Nutzer kann während einer Verzögerung Aktionen hinzufügen/löschen.
                // Vorher führte das zu einer Exception in einer async-void-Methode und damit zum Absturz.
                foreach (var action in preset.Actions.Where(a => a.IsEnabled).ToList())
                {
                    if (action.DelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(action.DelaySeconds));

                    try { RunAction(action, preset); }
                    catch (Exception ex) { Log($"Fehler bei Aktion {action.Type}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { Log($"Fehler beim Ausführen von {preset.Name}: {ex.Message}"); }
        }

        private void RunAction(ActionItem action, Preset preset)
        {
            switch (action.Type)
            {
                case ActionType.StartProcess:
                    {
                        if (string.IsNullOrWhiteSpace(action.TargetPath)) { Log("Starten übersprungen: kein Pfad angegeben."); return; }

                        var psi = new ProcessStartInfo { FileName = action.TargetPath, Arguments = action.Arguments ?? string.Empty, UseShellExecute = true, WindowStyle = (ProcessWindowStyle)action.LaunchState };
                        // Viele Spiele/Tools finden ihre Dateien nur, wenn sie im eigenen Ordner gestartet werden
                        string? dir = File.Exists(action.TargetPath) ? Path.GetDirectoryName(action.TargetPath) : null;
                        if (!string.IsNullOrEmpty(dir)) psi.WorkingDirectory = dir;
                        if (action.RunAsAdmin) psi.Verb = "runas";

                        var proc = Process.Start(psi);
                        if (proc != null) { action.LaunchedProcessId = proc.Id; Log($"Gestartet: {Path.GetFileName(action.TargetPath)} [PID: {proc.Id}]"); }
                        else Log($"Gestartet: {Path.GetFileName(action.TargetPath)}");
                        break;
                    }

                case ActionType.KillProcess:
                    KillProcessByNameOrPath(action.TargetPath);
                    break;

                case ActionType.OpenUrl:
                    {
                        string url = (action.TargetPath ?? string.Empty).Trim();
                        if (url.Length == 0) { Log("URL öffnen übersprungen: keine URL angegeben."); return; }
                        if (!url.Contains(':') && !url.StartsWith("\\\\")) url = "https://" + url;
                        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                        Log($"Geöffnet: {url}");
                        break;
                    }

                case ActionType.RunCommand:
                    RunCommand(action);
                    break;

                case ActionType.LockWorkstation:
                    if (!LockWorkStation()) Log("PC sperren fehlgeschlagen.");
                    else Log("PC gesperrt.");
                    break;

                case ActionType.ToggleMute:
                    keybd_event(VK_VOLUME_MUTE, 0, 0, UIntPtr.Zero);
                    keybd_event(VK_VOLUME_MUTE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Log("Ton stumm/laut umgeschaltet.");
                    break;

                case ActionType.CloseProcess:
                    CloseProcessGracefully(action.TargetPath);
                    break;

                case ActionType.SetVolume:
                    {
                        if (!int.TryParse((action.TargetPath ?? string.Empty).Trim().TrimEnd('%').Trim(), out int volume) || volume < 0 || volume > 100)
                        { Log($"Lautstärke übersprungen: \"{action.TargetPath}\" ist keine Zahl von 0 bis 100."); return; }
                        SystemActions.SetVolume(volume);
                        Log($"Lautstärke auf {volume} % gesetzt.");
                        break;
                    }

                case ActionType.MuteAudio:
                case ActionType.UnmuteAudio:
                    SystemActions.SetMute(action.Type == ActionType.MuteAudio);
                    Log(action.Type == ActionType.MuteAudio ? "Ton stumm." : "Ton an.");
                    break;

                case ActionType.MediaControl:
                    SystemActions.PressMediaKey(action.MediaKey);
                    Log($"Medientaste: {LocalizationService.Instance.GetDisplayName(action.MediaKey)}");
                    break;

                case ActionType.SystemPower:
                    Log(SystemActions.RunPowerCommand(action.SystemPowerCommand));
                    break;

                case ActionType.EnableProfile:
                case ActionType.DisableProfile:
                    {
                        bool enable = action.Type == ActionType.EnableProfile;
                        string name = (action.TargetPath ?? string.Empty).Trim();
                        var targets = Presets.Where(p => p.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (!targets.Any()) { Log($"Profil \"{name}\" nicht gefunden."); return; }
                        foreach (var p in targets) p.IsActive = enable;
                        Log($"Profil \"{name}\" {(enable ? "aktiviert" : "deaktiviert")}.");
                        break;
                    }

                case ActionType.ChangePowerPlan:
                    RunAsyncAction(() => ChangePowerPlanAsync(action.PowerPlan), action.Type);
                    break;

                case ActionType.SendWebhook:
                    RunAsyncAction(() => SendWebhookAsync(action.WebhookUrl, preset), action.Type);
                    break;

                case ActionType.SystemNotification:
                    ShowNotification(preset, action.NotificationMessage);
                    break;

                default:
                    Log($"Aktion {action.Type} wird in dieser Version noch nicht ausgeführt.");
                    break;
            }
        }

        private void RunCommand(ActionItem action)
        {
            string command = (action.TargetPath ?? string.Empty).Trim();
            if (command.Length == 0) { Log("Befehl übersprungen: kein Befehl angegeben."); return; }
            if (!string.IsNullOrWhiteSpace(action.Arguments)) command += " " + action.Arguments;

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + command,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = action.RunAsAdmin, // "runas" geht nur mit ShellExecute
            };
            if (action.RunAsAdmin) psi.Verb = "runas";

            var proc = Process.Start(psi);
            Log($"Befehl gestartet: {command}");
            if (proc == null) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    using (proc)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                        await proc.WaitForExitAsync(cts.Token);
                        int code = proc.ExitCode;
                        RunOnUi(() => Log(code == 0 ? $"Befehl fertig: {command}" : $"Befehl beendet mit Code {code}: {command}"));
                    }
                }
                catch (OperationCanceledException) { RunOnUi(() => Log($"Befehl läuft noch nach 10 Min.: {command}")); }
                catch (Exception ex) { RunOnUi(() => Log($"Befehl-Status unbekannt: {ex.Message}")); }
            });
        }

        private void CloseProcessGracefully(string? target)
        {
            string name = Path.GetFileNameWithoutExtension((target ?? string.Empty).Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(name)) { Log("Schließen übersprungen: kein Prozessname angegeben."); return; }

            var processes = Process.GetProcessesByName(name).Where(p => p.Id != Environment.ProcessId).ToList();
            if (!processes.Any()) { Log($"Kein laufender Prozess gefunden für: {name}"); return; }
            foreach (var p in processes)
            {
                using (p)
                {
                    try
                    {
                        // Wie ein Klick auf X: die App kann noch speichern oder nachfragen
                        if (p.CloseMainWindow()) Log($"Schließen angefordert: {p.ProcessName}");
                        else Log($"{p.ProcessName} hat kein Fenster (zum Erzwingen \"Programm beenden\" nutzen).");
                    }
                    catch (Exception ex) { Log($"Fehler beim Schließen von {name}: {ex.Message}"); }
                }
            }
        }

        private async void HandleProcessExitCleanup(Preset preset)
        {
            // Clean-up betrifft nur Prozess-Aktionen (Energieplan/Webhook/Benachrichtigung haben keinen Prozess)
            try
            {
                foreach (var action in preset.Actions.Where(a => a.IsEnabled && a.TerminateOnTriggerProcessExit &&
                                                                 (a.Type == ActionType.StartProcess || a.Type == ActionType.KillProcess)).ToList())
                {
                    if (action.ExitDelaySeconds > 0)
                    {
                        Log($"Warte {action.ExitDelaySeconds} Sek. vor dem Beenden von {Path.GetFileName(action.TargetPath)}...");
                        await Task.Delay(TimeSpan.FromSeconds(action.ExitDelaySeconds));
                    }

                    bool killedByPid = false;
                    if (action.LaunchedProcessId.HasValue)
                    {
                        try
                        {
                            using var proc = Process.GetProcessById(action.LaunchedProcessId.Value);
                            if (!proc.HasExited) { AggressiveKill(proc); Log($"Auto-Kill PID: {Path.GetFileName(action.TargetPath)}"); killedByPid = true; }
                        }
                        catch { }
                        action.LaunchedProcessId = null;
                    }
                    if (!killedByPid && !string.IsNullOrWhiteSpace(action.TargetPath)) KillProcessByNameOrPath(action.TargetPath);
                }
            }
            catch (Exception ex) { Log($"Fehler beim Clean-up von {preset.Name}: {ex.Message}"); }
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

        // Asynchrone Aktionen laufen im Hintergrund weiter, Fehler landen im Log statt die App abstürzen zu lassen
        private async void RunAsyncAction(Func<Task> work, ActionType type)
        {
            try { await work(); }
            catch (Exception ex) { Log($"Fehler bei Aktion {type}: {ex.Message}"); }
        }


        private void KillProcessByNameOrPath(string targetPath)
        {
            try
            {
                string exactName = Path.GetFileNameWithoutExtension((targetPath ?? string.Empty).Trim());

                // Ohne Namen hätte die Teilstring-Suche unten auf JEDEN Prozess gepasst und alles beendet.
                if (string.IsNullOrWhiteSpace(exactName)) { Log("Beenden übersprungen: kein Prozessname angegeben."); return; }

                int ownPid = Environment.ProcessId;
                var processes = Process.GetProcessesByName(exactName).Where(p => p.Id != ownPid).ToList();

                // Teilstring-Suche nur als Notlösung und nur bei ausreichend langen Namen
                string needle = exactName.Replace(" ", "");
                if (!processes.Any() && needle.Length >= 3)
                {
                    processes = Process.GetProcesses()
                        .Where(p => p.Id != ownPid && p.ProcessName.Contains(needle, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                if (!processes.Any()) Log($"Kein laufender Prozess gefunden für: {exactName}");
                foreach (var p in processes)
                {
                    using (p) { string name = p.ProcessName; AggressiveKill(p); Log($"Beendet (Smart-Kill): {name}"); }
                }
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
            catch (Exception ex) { Log($"Fehler beim Beenden von PID {p.Id}: {ex.Message}"); }
        }

        private JsonSerializerOptions GetJsonOptions()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        // Schreibt erst in eine Temp-Datei und tauscht dann. Ein Absturz mitten im Schreiben
        // hinterlässt so nie eine halbe (und damit unlesbare) Konfiguration.
        private static void WriteFileAtomic(string path, string content)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, content);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        public bool SavePresets()
        {
            lock (_saveLock)
            try
            {
                WriteFileAtomic(_configFile, JsonSerializer.Serialize(Presets, GetJsonOptions()));
                WriteFileAtomic(_settingsFile, JsonSerializer.Serialize(Settings, GetJsonOptions()));
                Log("Konfiguration & Settings gespeichert.");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Speichern fehlgeschlagen: {ex.Message}");
                return false;
            }
        }

        // Speichert nur den An/Aus-Zustand eines Profils, ohne andere ungespeicherte Änderungen
        // (z.B. halb bearbeitete Trigger) mit auf die Platte zu schreiben. Dazu wird die gespeicherte
        // Datei gelesen, nur "IsActive" des passenden Profils geändert und zurückgeschrieben.
        // Gibt false zurück, wenn das Profil noch nie gespeichert wurde (dann gibt es nichts zu patchen).
        public bool SavePresetActiveState(Preset preset)
        {
            lock (_saveLock)
            {
                try
                {
                    if (!File.Exists(_configFile)) return false;
                    if (System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(_configFile)) is not System.Text.Json.Nodes.JsonArray list) return false;

                    bool found = false;
                    foreach (var node in list)
                    {
                        if (node is System.Text.Json.Nodes.JsonObject obj && (string?)obj["Id"] == preset.Id)
                        {
                            obj["IsActive"] = preset.IsActive;
                            found = true;
                        }
                    }
                    if (!found) return false;

                    WriteFileAtomic(_configFile, list.ToJsonString(GetJsonOptions()));
                    Log($"Profil \"{preset.Name}\" {(preset.IsActive ? "aktiviert" : "deaktiviert")} (gespeichert).");
                    return true;
                }
                catch (Exception ex)
                {
                    Log($"Speichern des Profil-Status fehlgeschlagen: {ex.Message}");
                    return false;
                }
            }
        }

        // Fehlende Listen (z.B. aus Importen oder alten Dateien) ergänzen, damit nichts mit NullReference abstürzt
        private static void NormalizePreset(Preset p)
        {
            p.Triggers ??= new ObservableCollection<TriggerCondition>();
            p.Actions ??= new ObservableCollection<ActionItem>();
            p.Name ??= "Profil";
            if (string.IsNullOrWhiteSpace(p.Id)) p.Id = Guid.NewGuid().ToString();
            foreach (var t in p.Triggers) t.TargetName ??= string.Empty;
            foreach (var a in p.Actions)
            {
                a.TargetPath ??= string.Empty;
                a.Arguments ??= string.Empty;
                if (string.IsNullOrWhiteSpace(a.Id)) a.Id = Guid.NewGuid().ToString();
            }
            if (!p.Triggers.Any()) p.Triggers.Add(new TriggerCondition());
        }

        public void LoadPresets()
        {
            if (File.Exists(_configFile))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<ObservableCollection<Preset>>(File.ReadAllText(_configFile), GetJsonOptions());
                    if (loaded != null) { Presets.Clear(); foreach (var p in loaded.Where(p => p != null)) Presets.Add(p); }
                }
                catch (Exception ex)
                {
                    Log($"Fehler beim Laden der Profile: {ex.Message}");
                    BackupUnreadableFile(_configFile);
                }
            }

            foreach (var p in Presets) NormalizePreset(p);

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
                catch (Exception ex) { Log($"Fehler beim Laden der Einstellungen: {ex.Message}"); }
            }
        }

        // Sichert eine nicht lesbare Datei, bevor sie durch die Standardwerte überschrieben wird
        private void BackupUnreadableFile(string path)
        {
            try
            {
                string backup = Path.Combine(_appDataFolder, $"{Path.GetFileNameWithoutExtension(path)}_unreadable_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                File.Copy(path, backup, true);
                Log($"Nicht lesbare Datei gesichert unter: {backup}");
            }
            catch (Exception ex) { Log($"Sicherung der nicht lesbaren Datei fehlgeschlagen: {ex.Message}"); }
        }

        public void ExportPresets(string path)
        {
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(Presets, GetJsonOptions()));
                Log($"Profile exportiert nach: {path}");
            }
            catch (Exception ex) { Log($"Fehler beim Exportieren: {ex.Message}"); }
        }

        public void ImportPresets(string path)
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<ObservableCollection<Preset>>(File.ReadAllText(path), GetJsonOptions());
                if (loaded != null)
                {
                    foreach (var p in loaded.Where(p => p != null))
                    {
                        NormalizePreset(p);
                        p.Id = Guid.NewGuid().ToString();
                        foreach (var a in p.Actions) a.Id = Guid.NewGuid().ToString();
                        Presets.Add(p);
                    }
                    SavePresets();
                    Log($"Profile importiert von: {path}");
                }
            }
            catch (Exception ex) { Log($"Fehler beim Importieren: {ex.Message}"); }
        }

        // --- Native Hilfen ---

        private const byte VK_VOLUME_MUTE = 0xAD;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LockWorkStation();

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        // Sekunden seit der letzten Maus-/Tastatureingabe, -1 wenn nicht ermittelbar
        private static double GetIdleSeconds()
        {
            try
            {
                var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
                if (!GetLastInputInfo(ref info)) return -1;
                uint idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
                return idleMs / 1000.0;
            }
            catch { return -1; }
        }
    }
}
