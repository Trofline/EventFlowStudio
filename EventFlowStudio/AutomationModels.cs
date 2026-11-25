using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace EventFlowStudio.Models
{
    // Neue Werte immer HINTEN anhängen: die Konfiguration speichert die Namen, die Reihenfolge bestimmt die Anzeige.
    public enum TriggerType
    {
        ProcessStarted, ProcessStopped, BluetoothConnected, BluetoothDisconnected, UsbDeviceConnected, GlobalHotkey, AppStartup,
        UsbDeviceDisconnected, TimeOfDay, Interval, UserIdle, UserReturned, ForegroundApp,
        NetworkConnected, NetworkDisconnected, PowerPluggedIn, PowerUnplugged, BatteryLow,
        SessionLocked, SessionUnlocked, SystemResumed
    }

    public enum ActionType
    {
        StartProcess, KillProcess, ChangePowerPlan, SendWebhook, SystemNotification,
        OpenUrl, RunCommand, CloseProcess, AudioControl, MediaControl, SystemPower
    }

    public enum AudioCommand { SetVolume, Mute, Unmute, ToggleMute }
    public enum MediaKey { PlayPause, NextTrack, PreviousTrack, Stop, VolumeUp, VolumeDown }
    public enum SystemPowerCommand { LockScreen, MonitorOff, Sleep, Hibernate, SignOut, Shutdown, Restart }
    public enum WindowLaunchState { Normal = ProcessWindowStyle.Normal, Minimized = ProcessWindowStyle.Hidden, Maximized = ProcessWindowStyle.Maximized }
    public enum PowerPlanType { Balanced, HighPerformance, PowerSaver }

    // Basisklasse, die alle falschen "Wurde geändert"-Alarme verhindert
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            // Wenn der Wert exakt gleich bleibt, brich sofort ab!
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;

            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }

    public class AppSettings : ObservableObject
    {
        private bool _hideInstructions = false;
        private string _language = "Deutsch";
        private bool _runAsAdmin = false;
        private bool _autoStartWindows = false;
        private bool _autoUpdate = true;
        private bool _enableSystemTray = true;
        private bool _startMinimized = false;
        private string _toggleHotkeyText = string.Empty;
        private uint _toggleHotkeyModifier = 0;
        private uint _toggleHotkeyVK = 0;

        public bool HideInstructions { get => _hideInstructions; set => SetProperty(ref _hideInstructions, value); }
        public string Language { get => _language; set => SetProperty(ref _language, value); }
        public bool RunAsAdmin { get => _runAsAdmin; set => SetProperty(ref _runAsAdmin, value); }
        public bool AutoStartWindows { get => _autoStartWindows; set => SetProperty(ref _autoStartWindows, value); }
        public bool AutoUpdate { get => _autoUpdate; set => SetProperty(ref _autoUpdate, value); }
        public bool EnableSystemTray { get => _enableSystemTray; set => SetProperty(ref _enableSystemTray, value); }

        public bool StartMinimized { get => _startMinimized; set => SetProperty(ref _startMinimized, value); }
        public string ToggleHotkeyText { get => _toggleHotkeyText; set => SetProperty(ref _toggleHotkeyText, value); }
        public uint ToggleHotkeyModifier { get => _toggleHotkeyModifier; set => SetProperty(ref _toggleHotkeyModifier, value); }
        public uint ToggleHotkeyVK { get => _toggleHotkeyVK; set => SetProperty(ref _toggleHotkeyVK, value); }
    }

    public class TriggerCondition : ObservableObject
    {
        private TriggerType _type = TriggerType.ProcessStarted;
        private string _targetName = string.Empty;
        private uint _hotkeyModifier;
        private uint _hotkeyVK;

        public TriggerType Type
        {
            get => _type;
            set
            {
                if (!SetProperty(ref _type, value)) return;
                ResetRuntimeState();
                OnPropertyChanged(nameof(ShowTextInput));
                OnPropertyChanged(nameof(ShowHotkeyInput));
            }
        }
        public string TargetName { get => _targetName; set { if (SetProperty(ref _targetName, value)) ResetRuntimeState(); } }
        public uint HotkeyModifier { get => _hotkeyModifier; set => SetProperty(ref _hotkeyModifier, value); }
        public uint HotkeyVK { get => _hotkeyVK; set => SetProperty(ref _hotkeyVK, value); }

        // Trigger ohne Eingabefeld (der Hinweistext erklärt sie)
        private static readonly HashSet<TriggerType> NoInputTypes = new()
        {
            TriggerType.GlobalHotkey, TriggerType.AppStartup, TriggerType.PowerPluggedIn, TriggerType.PowerUnplugged,
            TriggerType.SessionLocked, TriggerType.SessionUnlocked, TriggerType.SystemResumed
        };

        [JsonIgnore] public bool ShowTextInput => !NoInputTypes.Contains(Type);
        [JsonIgnore] public bool ShowHotkeyInput => Type == TriggerType.GlobalHotkey;

        // Laufzeit-Zustand für zeit- und zustandsbasierte Trigger (wird nicht gespeichert)
        [JsonIgnore] public DateTime LastFired { get; set; }
        [JsonIgnore] public bool StateLatched { get; set; }

        public void ResetRuntimeState() { LastFired = default; StateLatched = false; }
    }

    public class ActionItem : ObservableObject
    {
        private ActionType _type = ActionType.StartProcess;
        private string _targetPath = string.Empty;
        private string _arguments = string.Empty;
        private int _delaySeconds = 0;
        private int _exitDelaySeconds = 0;
        private WindowLaunchState _launchState = WindowLaunchState.Normal;
        private bool _isEnabled = true;
        private bool _terminateOnTriggerProcessExit = false;
        private bool _runAsAdmin = false;
        private PowerPlanType _powerPlan = PowerPlanType.HighPerformance;
        private string _webhookUrl = "https://";
        private string _notificationMessage = "Aktion ausgeführt.";
        private AudioCommand _audioCommand = AudioCommand.SetVolume;
        private int _volumePercent = 50;
        private MediaKey _mediaKey = MediaKey.PlayPause;
        private SystemPowerCommand _systemPowerCommand = SystemPowerCommand.LockScreen;

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
        public ActionType Type
        {
            get => _type;
            set
            {
                if (!SetProperty(ref _type, value)) return;
                foreach (var n in new[] { nameof(ShowTargetInput), nameof(ShowBrowseButton), nameof(ShowArguments), nameof(ShowRunAsAdmin), nameof(ShowProcessOptions),
                                          nameof(ShowPowerPlan), nameof(ShowWebhook), nameof(ShowNotification), nameof(ShowAudio),
                                          nameof(ShowVolume), nameof(ShowMediaKey), nameof(ShowSystemPower) })
                    OnPropertyChanged(n);
            }
        }
        public string TargetPath { get => _targetPath; set => SetProperty(ref _targetPath, value); }
        public string Arguments { get => _arguments; set => SetProperty(ref _arguments, value); }
        public int DelaySeconds { get => _delaySeconds; set => SetProperty(ref _delaySeconds, value); }
        public int ExitDelaySeconds { get => _exitDelaySeconds; set => SetProperty(ref _exitDelaySeconds, value); }
        public WindowLaunchState LaunchState { get => _launchState; set => SetProperty(ref _launchState, value); }
        public bool TerminateOnTriggerProcessExit { get => _terminateOnTriggerProcessExit; set => SetProperty(ref _terminateOnTriggerProcessExit, value); }
        public bool RunAsAdmin { get => _runAsAdmin; set => SetProperty(ref _runAsAdmin, value); }
        public PowerPlanType PowerPlan { get => _powerPlan; set => SetProperty(ref _powerPlan, value); }
        public string WebhookUrl { get => _webhookUrl; set => SetProperty(ref _webhookUrl, value); }
        public string NotificationMessage { get => _notificationMessage; set => SetProperty(ref _notificationMessage, value); }
        public AudioCommand AudioCommand { get => _audioCommand; set { if (SetProperty(ref _audioCommand, value)) OnPropertyChanged(nameof(ShowVolume)); } }
        public int VolumePercent { get => _volumePercent; set => SetProperty(ref _volumePercent, Math.Clamp(value, 0, 100)); }
        public MediaKey MediaKey { get => _mediaKey; set => SetProperty(ref _mediaKey, value); }
        public SystemPowerCommand SystemPowerCommand { get => _systemPowerCommand; set => SetProperty(ref _systemPowerCommand, value); }

        // Welche Eingaben der Editor für den gewählten Aktionstyp zeigt
        [JsonIgnore] public bool ShowTargetInput => Type is ActionType.StartProcess or ActionType.KillProcess or ActionType.CloseProcess or ActionType.OpenUrl or ActionType.RunCommand;
        [JsonIgnore] public bool ShowBrowseButton => Type is ActionType.StartProcess or ActionType.KillProcess or ActionType.CloseProcess;
        [JsonIgnore] public bool ShowArguments => Type == ActionType.StartProcess;
        [JsonIgnore] public bool ShowRunAsAdmin => Type is ActionType.StartProcess or ActionType.RunCommand;
        [JsonIgnore] public bool ShowProcessOptions => Type is ActionType.StartProcess or ActionType.KillProcess;
        [JsonIgnore] public bool ShowPowerPlan => Type == ActionType.ChangePowerPlan;
        [JsonIgnore] public bool ShowWebhook => Type == ActionType.SendWebhook;
        [JsonIgnore] public bool ShowNotification => Type == ActionType.SystemNotification;
        [JsonIgnore] public bool ShowAudio => Type == ActionType.AudioControl;
        [JsonIgnore] public bool ShowVolume => Type == ActionType.AudioControl && AudioCommand == AudioCommand.SetVolume;
        [JsonIgnore] public bool ShowMediaKey => Type == ActionType.MediaControl;
        [JsonIgnore] public bool ShowSystemPower => Type == ActionType.SystemPower;

        [JsonIgnore]
        public int? LaunchedProcessId { get; set; }
    }

    public class Preset : ObservableObject
    {
        private string _name = "Neues Profil";
        private string _description = "Beschreibung...";
        private bool _isActive = true;

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public string Description { get => _description; set => SetProperty(ref _description, value); }
        public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

        public ObservableCollection<TriggerCondition> Triggers { get; set; } = new ObservableCollection<TriggerCondition>();
        public ObservableCollection<ActionItem> Actions { get; set; } = new ObservableCollection<ActionItem>();
    }
}