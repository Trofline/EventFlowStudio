using System;
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

    public class AppSettings : INotifyPropertyChanged
    {
        private bool _hideInstructions = false;
        private string _language = "Deutsch";
        private bool _runAsAdmin = false;
        private bool _autoStartWindows = false;
        private bool _autoUpdate = true;
        private bool _enableSystemTray = true;

        public bool HideInstructions { get => _hideInstructions; set { _hideInstructions = value; OnPropertyChanged(); } }
        public string Language { get => _language; set { _language = value; OnPropertyChanged(); } }
        public bool RunAsAdmin { get => _runAsAdmin; set { _runAsAdmin = value; OnPropertyChanged(); } }
        public bool AutoStartWindows { get => _autoStartWindows; set { _autoStartWindows = value; OnPropertyChanged(); } }
        public bool AutoUpdate { get => _autoUpdate; set { _autoUpdate = value; OnPropertyChanged(); } }
        public bool EnableSystemTray { get => _enableSystemTray; set { _enableSystemTray = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class TriggerCondition : INotifyPropertyChanged
    {
        private TriggerType _type = TriggerType.ProcessStarted;
        private string _targetName = string.Empty;
        private uint _hotkeyModifier;
        private uint _hotkeyVK;

        public TriggerType Type { get => _type; set { _type = value; OnPropertyChanged(); } }
        public string TargetName { get => _targetName; set { _targetName = value; OnPropertyChanged(); } }
        public uint HotkeyModifier { get => _hotkeyModifier; set { _hotkeyModifier = value; OnPropertyChanged(); } }
        public uint HotkeyVK { get => _hotkeyVK; set { _hotkeyVK = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ActionItem : INotifyPropertyChanged
    {
        private ActionType _type = ActionType.StartProcess;
        private string _targetPath = string.Empty;
        private string _arguments = string.Empty;
        private int _delaySeconds = 0;
        private int _exitDelaySeconds = 0; // NEU: Verzögerung beim Beenden
        private WindowLaunchState _launchState = WindowLaunchState.Normal;
        private bool _isEnabled = true;
        private bool _terminateOnTriggerProcessExit = false;
        private bool _runAsAdmin = false;
        private PowerPlanType _powerPlan = PowerPlanType.HighPerformance;
        private string _webhookUrl = "https://";
        private string _notificationMessage = "Aktion ausgeführt.";

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; OnPropertyChanged(); } }
        public ActionType Type { get => _type; set { _type = value; OnPropertyChanged(); } }
        public string TargetPath { get => _targetPath; set { _targetPath = value; OnPropertyChanged(); } }
        public string Arguments { get => _arguments; set { _arguments = value; OnPropertyChanged(); } }
        public int DelaySeconds { get => _delaySeconds; set { _delaySeconds = value; OnPropertyChanged(); } }
        public int ExitDelaySeconds { get => _exitDelaySeconds; set { _exitDelaySeconds = value; OnPropertyChanged(); } } // NEU
        public WindowLaunchState LaunchState { get => _launchState; set { _launchState = value; OnPropertyChanged(); } }
        public bool TerminateOnTriggerProcessExit { get => _terminateOnTriggerProcessExit; set { _terminateOnTriggerProcessExit = value; OnPropertyChanged(); } }
        public bool RunAsAdmin { get => _runAsAdmin; set { _runAsAdmin = value; OnPropertyChanged(); } }
        public PowerPlanType PowerPlan { get => _powerPlan; set { _powerPlan = value; OnPropertyChanged(); } }
        public string WebhookUrl { get => _webhookUrl; set { _webhookUrl = value; OnPropertyChanged(); } }
        public string NotificationMessage { get => _notificationMessage; set { _notificationMessage = value; OnPropertyChanged(); } }

        [JsonIgnore]
        public int? LaunchedProcessId { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class Preset : INotifyPropertyChanged
    {
        private string _name = "Neues Profil";
        private string _description = "Beschreibung...";
        private bool _isActive = true;

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
        public bool IsActive { get => _isActive; set { _isActive = value; OnPropertyChanged(); } }

        // NEU: Ein Profil kann jetzt mehrere Auslöser besitzen!
        public ObservableCollection<TriggerCondition> Triggers { get; set; } = new ObservableCollection<TriggerCondition>();

        public ObservableCollection<ActionItem> Actions { get; set; } = new ObservableCollection<ActionItem>();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}