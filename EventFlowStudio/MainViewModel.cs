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

        public string[] AvailableLanguages => new[] { "Deutsch", "English" };

        public int SelectedTabIndex { get => _selectedTabIndex; set { _selectedTabIndex = value; MarkDirty(); OnPropertyChanged(); } }
        public Preset? SelectedPreset { get => _selectedPreset; set { _selectedPreset = value; OnPropertyChanged(); } }
        public string LogOutput { get => _logOutput; set { _logOutput = value; OnPropertyChanged(); } }

        public bool IsDirty { get => _isDirty; set { _isDirty = value; OnPropertyChanged(); } }

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

            _engine.LogMessage += msg => LogOutput = $"[{DateTime.Now:HH:mm:ss}] {msg}\n" + LogOutput;

            _engine.Initialize();

            AppSettings.PropertyChanged += (s, e) =>
            {
                MarkDirty();
                if (e.PropertyName == nameof(AppSettings.Language))
                {
                    Strings.CurrentLanguage = AppSettings.Language;
                }
            };

            Presets.CollectionChanged += (s, e) => MarkDirty();
            foreach (var p in Presets)
            {
                p.PropertyChanged += (s, e) => MarkDirty();
            }

            EnsureAdminState();
            EnsureAutoStart();

            if (Presets.Count > 0) SelectedPreset = Presets[0];
            IsDirty = false;
        }

        public void MarkDirty() => IsDirty = true;

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

    public class InverseBooleanToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            (bool)value ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }
}