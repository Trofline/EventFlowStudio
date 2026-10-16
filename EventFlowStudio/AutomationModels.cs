using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace EventFlowStudio.Models
{
    public enum TriggerType { ProcessStarted, ProcessStopped, BluetoothConnected, BluetoothDisconnected, UsbDeviceConnected, GlobalHotkey, AppStartup }
    public enum ActionType { StartProcess, KillProcess, ChangePowerPlan, SendWebhook, SystemNotification }
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

        public TriggerType Type { get => _type; set => SetProperty(ref _type, value); }
        public string TargetName { get => _targetName; set => SetProperty(ref _targetName, value); }
        public uint HotkeyModifier { get => _hotkeyModifier; set => SetProperty(ref _hotkeyModifier, value); }
        public uint HotkeyVK { get => _hotkeyVK; set => SetProperty(ref _hotkeyVK, value); }
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

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
        public ActionType Type { get => _type; set => SetProperty(ref _type, value); }
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