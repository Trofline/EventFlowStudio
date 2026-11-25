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
        public Array MediaKeys => Enum.GetValues(typeof(MediaKey));
        public Array SystemPowerCommands => Enum.GetValues(typeof(SystemPowerCommand));
        public Array PowerPlanTypes => Enum.GetValues(typeof(PowerPlanType));

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

            // Jede Änderung an Profilen, Triggern und Aktionen (auch neu hinzugefügten) markiert "nicht gespeichert".
            // Vorher wurden nur Name/Aktiv bereits vorhandener Profile erkannt.
            TrackCollection(Presets);

            EnsureAdminState();
            EnsureAutoStart();

            if (Presets.Count > 0) SelectedPreset = Presets[0];

            // Setzt alles ganz am Schluss sauber zurück
            IsDirty = false;
        }

        public void MarkDirty() => IsDirty = true;

        private const int MaxLogLines = 300;
        private readonly System.Collections.Generic.LinkedList<string> _logLines = new();

        // Logs kommen auch aus Hintergrund-Threads. Sie werden auf dem UI-Thread angehängt und begrenzt,
        // damit das Log bei tagelangem Laufen im Tray nicht endlos wächst.
        private void AppendLog(string msg)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(() => AppendLog(msg));
                return;
            }

            _logLines.AddFirst($"[{DateTime.Now:HH:mm:ss}] {msg}");
            while (_logLines.Count > MaxLogLines) _logLines.RemoveLast();
            LogOutput = string.Join("\n", _logLines);
        }

        private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => MarkDirty();

        private void TrackCollection<T>(ObservableCollection<T> collection) where T : INotifyPropertyChanged
        {
            collection.CollectionChanged += (s, e) =>
            {
                if (e.OldItems != null) foreach (T item in e.OldItems) Untrack(item);
                if (e.NewItems != null) foreach (T item in e.NewItems) Track(item);
                MarkDirty();
            };
            foreach (var item in collection) Track(item);
        }

        private void Track(INotifyPropertyChanged item)
        {
            item.PropertyChanged -= OnItemChanged;
            item.PropertyChanged += OnItemChanged;
            if (item is Preset preset)
            {
                TrackCollection(preset.Triggers);
                TrackCollection(preset.Actions);
            }
        }

        private void Untrack(INotifyPropertyChanged item) => item.PropertyChanged -= OnItemChanged;

        public void Shutdown() => _engine.Shutdown();

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
                string appPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

                if (AppSettings.AutoStartWindows) key.SetValue(appName, $"\"{appPath}\"");
                else key.DeleteValue(appName, false);
            }
            catch (Exception ex) { _engine.Log($"Autostart konnte nicht gesetzt werden: {ex.Message}"); }
        }

        private void RestartAsAdmin()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName, UseShellExecute = true, Verb = "runas" });
                // Die neue Instanz wartet, bis diese hier ihre Single-Instance-Sperre freigegeben hat
                App.ReleaseSingleInstance();
                Environment.Exit(0);
            }
            catch (Exception ex) { _engine.Log($"Neustart als Administrator fehlgeschlagen: {ex.Message}"); }
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
            if (_engine.SavePresets()) IsDirty = false;
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

    public class InverseBooleanToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            (bool)value ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }

    // Zeigt Enum-Werte übersetzt an. Binding 0 = Wert, Binding 1 = Strings.CurrentLanguage (nur für die Aktualisierung).
    // ConverterParameter "hint" liefert statt des Namens den Eingabe-Hinweis.
    public class EnumTextConverter : System.Windows.Data.IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var value = values.Length > 0 ? values[0] : null;
            if (value == null || value == DependencyProperty.UnsetValue) return string.Empty;
            return (parameter as string) == "hint"
                ? LocalizationService.Instance.GetHint(value)
                : LocalizationService.Instance.GetDisplayName(value);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
