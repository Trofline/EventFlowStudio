using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using EventFlowStudio.Models;
using EventFlowStudio.Services;

namespace EventFlowStudio.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ExecutionEngine _engine = new ExecutionEngine();
        private Preset? _selectedPreset;
        private string _logOutput = string.Empty;
        private int _selectedTabIndex = 0;
        private bool _isDirty = false;

        public ObservableCollection<Preset> Presets => _engine.Presets;
        public AppSettings AppSettings => _engine.Settings;
        public LocalizationService Strings => LocalizationService.Instance;

        public Array TriggerTypes => Enum.GetValues(typeof(TriggerType));
        public Array ActionTypes => Enum.GetValues(typeof(ActionType));
        public Array LaunchStates => Enum.GetValues(typeof(WindowLaunchState));
        public Array PowerPlanTypes => Enum.GetValues(typeof(PowerPlanType));
        public Array AudioCommands => Enum.GetValues(typeof(AudioCommand));
        public Array MediaKeys => Enum.GetValues(typeof(MediaKey));
        public Array SystemPowerCommands => Enum.GetValues(typeof(SystemPowerCommand));

        public string[] AvailableLanguages => new[] { "Deutsch", "English" };

        // Die Werte werden jetzt nur noch gesetzt, wenn sie sich TATSÄCHLICH geändert haben.
        // Außerdem wurde hier das pauschale MarkDirty() beim Tab-Wechsel komplett entfernt.
        public int SelectedTabIndex { get => _selectedTabIndex; set { if (_selectedTabIndex != value) { _selectedTabIndex = value; OnPropertyChanged(); } } }
        public Preset? SelectedPreset { get => _selectedPreset; set { if (_selectedPreset != value) { _selectedPreset = value; OnPropertyChanged(); } } }
        public string LogOutput { get => _logOutput; set { if (_logOutput != value) { _logOutput = value; OnPropertyChanged(); } } }
        public bool IsDirty { get => _isDirty; set { if (_isDirty != value) { _isDirty = value; OnPropertyChanged(); } } }

        public event Action? ToggleMainWindowRequested;
        // Benachrichtigungen, die vor dem Anmelden des Fensters ausgelöst werden (z. B. AppStartup-Trigger),
        // werden zwischengespeichert und beim Anmelden nachgereicht.
        private readonly System.Collections.Generic.List<(string Title, string Message)> _pendingNotifications = new();
        private Action<string, string>? _notificationHandler;
        public event Action<string, string>? NotificationRequested
        {
            add
            {
                _notificationHandler += value;
                if (value == null) return;
                foreach (var (title, message) in _pendingNotifications) value(title, message);
                _pendingNotifications.Clear();
            }
            remove => _notificationHandler -= value;
        }

        public ICommand AddPresetCommand { get; }
        public ICommand DeletePresetCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand AddTriggerCommand { get; }
        public ICommand DeleteTriggerCommand { get; }
        public ICommand AddActionCommand { get; }
        public ICommand DeleteActionCommand { get; }
        public ICommand BrowseFileCommand { get; }
        public ICommand TestRunCommand { get; }
        public ICommand SwitchTabCommand { get; }
        public ICommand RestartAsAdminCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ImportCommand { get; }

        public MainViewModel()
        {
            AddPresetCommand = new RelayCommand(_ => { AddNewPreset(); MarkDirty(); });
            DeletePresetCommand = new RelayCommand(p => { DeletePreset(p as Preset ?? SelectedPreset); MarkDirty(); });
            SaveCommand = new RelayCommand(_ => SaveAll());

            AddTriggerCommand = new RelayCommand(_ => { SelectedPreset?.Triggers.Add(new TriggerCondition()); MarkDirty(); });
            DeleteTriggerCommand = new RelayCommand(trig => { if (trig is TriggerCondition t && SelectedPreset != null) SelectedPreset.Triggers.Remove(t); MarkDirty(); });

            AddActionCommand = new RelayCommand(_ => { SelectedPreset?.Actions.Add(new ActionItem()); MarkDirty(); });
            DeleteActionCommand = new RelayCommand(action => { if (action is ActionItem a) SelectedPreset?.Actions.Remove(a); MarkDirty(); });

            BrowseFileCommand = new RelayCommand(action => { BrowseFile(action as ActionItem); MarkDirty(); });
            TestRunCommand = new RelayCommand(_ => { if (SelectedPreset != null) _engine.ExecutePresetActions(SelectedPreset); });
            SwitchTabCommand = new RelayCommand(index => SelectedTabIndex = Convert.ToInt32(index));
            RestartAsAdminCommand = new RelayCommand(_ => RestartAsAdmin());
            ExportCommand = new RelayCommand(_ => ExportPresets());
            ImportCommand = new RelayCommand(_ => { ImportPresets(); MarkDirty(); });

            _engine.LogMessage += AppendLog;
            _engine.ToggleMainWindow += () => ToggleMainWindowRequested?.Invoke();
            _engine.NotificationRequested += (title, message) =>
            {
                if (_notificationHandler != null) _notificationHandler.Invoke(title, message);
                else _pendingNotifications.Add((title, message));
            };

            _engine.Initialize();

            AppSettings.PropertyChanged += (s, e) =>
            {
                MarkDirty();
                if (e.PropertyName == nameof(AppSettings.Language))
                {
                    Strings.CurrentLanguage = AppSettings.Language;
                }
            };

            // Änderungen an Profilen, Triggern und Aktionen (auch neu hinzugefügten) als ungespeichert markieren
            Presets.CollectionChanged += OnTrackedCollectionChanged;
            foreach (var p in Presets) Track(p);

            EnsureAdminState();
            EnsureAutoStart();

            if (Presets.Count > 0) SelectedPreset = Presets[0];

            // Setzt alles ganz am Schluss sauber zurück
            IsDirty = false;
        }

        public void MarkDirty() => IsDirty = true;

        public void Shutdown() => _engine.Shutdown();

        private const int MaxLogLines = 300;
        private readonly object _logLock = new();

        // Das Log wächst sonst über Tage unbegrenzt und wird bei jeder Zeile komplett neu gezeichnet
        private void AppendLog(string msg)
        {
            lock (_logLock)
            {
                string text = $"[{DateTime.Now:HH:mm:ss}] {msg}\n" + _logOutput;
                int idx = -1;
                for (int i = 0; i < MaxLogLines; i++) { idx = text.IndexOf('\n', idx + 1); if (idx < 0) break; }
                if (idx >= 0 && idx < text.Length - 1) text = text.Substring(0, idx + 1);
                LogOutput = text;
            }
        }

        private void Track(object? item)
        {
            switch (item)
            {
                case Preset p:
                    p.PropertyChanged -= OnTrackedPropertyChanged; p.PropertyChanged += OnTrackedPropertyChanged;
                    p.Triggers.CollectionChanged -= OnTrackedCollectionChanged; p.Triggers.CollectionChanged += OnTrackedCollectionChanged;
                    p.Actions.CollectionChanged -= OnTrackedCollectionChanged; p.Actions.CollectionChanged += OnTrackedCollectionChanged;
                    foreach (var t in p.Triggers) Track(t);
                    foreach (var a in p.Actions) Track(a);
                    break;
                case INotifyPropertyChanged npc:
                    npc.PropertyChanged -= OnTrackedPropertyChanged; npc.PropertyChanged += OnTrackedPropertyChanged;
                    break;
            }
        }

        private void OnTrackedPropertyChanged(object? sender, PropertyChangedEventArgs e) => MarkDirty();

        private void OnTrackedCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null) foreach (var item in e.NewItems) Track(item);
            MarkDirty();
        }

        private void ExportPresets()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON Profile (*.json)|*.json", FileName = "EventFlow_Profiles.json" };
            if (dialog.ShowDialog() == true) _engine.ExportPresets(dialog.FileName);
        }

        private void ImportPresets()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON Profile (*.json)|*.json" };
            if (dialog.ShowDialog() == true)
            {
                _engine.ImportPresets(dialog.FileName);
                if (SelectedPreset == null && Presets.Count > 0) SelectedPreset = Presets[0];
            }
        }

        private void EnsureAdminState()
        {
            bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            if (AppSettings.RunAsAdmin && !isAdmin) RestartAsAdmin();
        }

        private void EnsureAutoStart()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true)!;
                string appName = "EventFlowStudio";
                string appPath = Process.GetCurrentProcess().MainModule!.FileName;

                if (AppSettings.AutoStartWindows) key.SetValue(appName, $"\"{appPath}\"");
                else key.DeleteValue(appName, false);
            }
            catch { }
        }

        private void RestartAsAdmin()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = Process.GetCurrentProcess().MainModule!.FileName, UseShellExecute = true, Verb = "runas" });
                Environment.Exit(0);
            }
            catch { }
        }

        private void AddNewPreset() { Presets.Add(new Preset { Name = "⚡ Neues Profil" }); SelectedPreset = Presets.Last(); }
        private void DeletePreset(Preset? preset) { if (preset != null) { Presets.Remove(preset); SelectedPreset = Presets.FirstOrDefault(); } }

        private void BrowseFile(ActionItem? action)
        {
            if (action == null) return;
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Anwendungen (*.exe)|*.exe|Alle Dateien (*.*)|*.*" };
            if (dialog.ShowDialog() == true) action.TargetPath = dialog.FileName;
        }

        public void SaveAll()
        {
            EnsureAutoStart();
            _engine.SavePresets();
            IsDirty = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        public RelayCommand(Action<object?> execute) => _execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

    /// <summary>
    /// Übersetzt Enum-Werte und Hinweise. values[0] = Wert, values[1] = aktuelle Sprache (nur damit die Anzeige beim Sprachwechsel neu lädt).
    /// ConverterParameter: "name" (Standard), "triggerHint" oder "actionHint".
    /// </summary>
    public class LocalizedTextConverter : System.Windows.Data.IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var value = values.Length > 0 ? values[0] : null;
            if (value == DependencyProperty.UnsetValue) return string.Empty;
            var loc = LocalizationService.Instance;
            return (parameter as string) switch
            {
                "triggerHint" => loc.TriggerHint(value),
                "actionHint" => loc.ActionHint(value),
                _ => loc.DisplayName(value),
            };
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    public class InverseBooleanToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            (bool)value ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }
}