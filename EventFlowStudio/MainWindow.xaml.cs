using System;
using System.Windows;
using System.Windows.Input;
using EventFlowStudio.ViewModels;

namespace EventFlowStudio
{
    public partial class MainWindow : Window
    {
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private bool _isActualClose = false;

        public MainWindow()
        {
            InitializeComponent();
            SetupTrayIcon();
            if (DataContext is MainViewModel notifyVm) notifyVm.NotificationRequested += ShowTrayNotification;

            this.Closing += MainWindow_Closing;
            this.Closed += (s, e) => (DataContext as MainViewModel)?.Shutdown();

            // Beim Abmelden/Herunterfahren darf das Fenster nicht in den Tray "flüchten",
            // sonst blockiert die App das Herunterfahren von Windows.
            System.Windows.Application.Current.SessionEnding += (s, e) => { _isActualClose = true; _notifyIcon?.Dispose(); };

            if (DataContext is MainViewModel vm)
            {
                // Im Konstruktor statt in Loaded: Loaded kommt nie, wenn das Fenster versteckt startet
                vm.ToggleMainWindowRequested += ToggleWindowVisibility;

                if (vm.AppSettings.StartMinimized && !vm.AppSettings.EnableSystemTray)
                    this.WindowState = WindowState.Minimized;
            }
        }

        // Fenster beim Start gar nicht anzeigen (nur Tray-Symbol)
        public bool StartsHiddenInTray => DataContext is MainViewModel vm && vm.AppSettings.StartMinimized && vm.AppSettings.EnableSystemTray && _notifyIcon != null;

        private void ToggleWindowVisibility()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (this.IsVisible)
                {
                    this.Hide();
                }
                else
                {
                    ShowMainWindow();
                }
            });
        }

        private void ShowTrayNotification(string title, string message)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_notifyIcon == null) return;
                _notifyIcon.ShowBalloonTip(5000, title, message, System.Windows.Forms.ToolTipIcon.Info);
            }));
        }

        private void SetupTrayIcon()
        {
            try
            {
                _notifyIcon = new System.Windows.Forms.NotifyIcon();

                // Der absolut sichere Weg, den Pfad der eigenen .exe in Single-File-Apps auszulesen
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
                _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);

                _notifyIcon.Visible = true;
                _notifyIcon.Text = "EventFlow Studio";

                _notifyIcon.DoubleClick += (s, args) => ShowMainWindow();

                var contextMenu = new System.Windows.Forms.ContextMenuStrip();
                contextMenu.Items.Add("EventFlow öffnen", null, (s, e) => ShowMainWindow());
                var restoreItem = new System.Windows.Forms.ToolStripMenuItem("Pausierte Apps wiederherstellen", null, (s, e) => (DataContext as MainViewModel)?.RestorePausedAppsNow());
                contextMenu.Items.Add(restoreItem);
                // Nur anklickbar, wenn gerade Apps pausiert sind
                contextMenu.Opening += (s, e) =>
                {
                    var vm = DataContext as MainViewModel;
                    restoreItem.Enabled = vm?.HasPausedApps == true;
                    if (vm != null) restoreItem.Text = vm.Strings.TrayRestorePaused;
                };
                contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                contextMenu.Items.Add("Beenden", null, (s, e) => QuitApplication());
                _notifyIcon.ContextMenuStrip = contextMenu;
            }
            catch (Exception ex)
            {
                // Verhindert den Totalabsturz, falls das Icon wider Erwarten nicht geladen werden kann
                System.Windows.MessageBox.Show("System-Tray Icon konnte nicht geladen werden: " + ex.Message);
            }
        }

        public void ShowMainWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            this.Focus();
        }

        private void QuitApplication()
        {
            _isActualClose = true;

            if (DataContext is MainViewModel vm && vm.IsDirty)
            {
                var result = System.Windows.MessageBox.Show(
                    vm.Strings.UnsavedWarningMessage,
                    vm.Strings.UnsavedWarningTitle,
                    System.Windows.MessageBoxButton.YesNoCancel,
                    System.Windows.MessageBoxImage.Warning);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    vm.SaveAll();
                }
                else if (result == System.Windows.MessageBoxResult.Cancel)
                {
                    _isActualClose = false;
                    return;
                }
            }

            _notifyIcon?.Dispose();
            System.Windows.Application.Current.Shutdown();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            bool trayEnabled = true;
            if (DataContext is MainViewModel vm)
            {
                trayEnabled = vm.AppSettings.EnableSystemTray;
            }

            if (!_isActualClose && trayEnabled)
            {
                e.Cancel = true;
                this.Hide();
                return;
            }

            // Ohne Tray schließt das X die App. Vorher gingen ungespeicherte Änderungen dabei ohne Nachfrage verloren.
            if (!_isActualClose && DataContext is MainViewModel model && model.IsDirty)
            {
                var result = System.Windows.MessageBox.Show(
                    model.Strings.UnsavedWarningMessage,
                    model.Strings.UnsavedWarningTitle,
                    System.Windows.MessageBoxButton.YesNoCancel,
                    System.Windows.MessageBoxImage.Warning);

                if (result == System.Windows.MessageBoxResult.Cancel) { e.Cancel = true; return; }
                if (result == System.Windows.MessageBoxResult.Yes) model.SaveAll();
            }

            _notifyIcon?.Dispose();
        }

        // Liest eine Tastenkombination aus einem KeyDown-Ereignis.
        // Rückgabe false = nur eine Modifier-Taste (weiter warten). clear = Entf/Rücktaste/Esc ohne Modifier löscht den Hotkey.
        private static bool TryReadHotkey(System.Windows.Input.KeyEventArgs e, out string text, out uint modifiers, out uint vk, out bool clear)
        {
            text = string.Empty; modifiers = 0; vk = 0; clear = false;
            var modifier = Keyboard.Modifiers;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed)
                return false;

            if (modifier == ModifierKeys.None && key is Key.Back or Key.Delete or Key.Escape)
            {
                clear = true;
                return true;
            }

            if (modifier.HasFlag(ModifierKeys.Control)) text += "Ctrl + ";
            if (modifier.HasFlag(ModifierKeys.Alt)) text += "Alt + ";
            if (modifier.HasFlag(ModifierKeys.Shift)) text += "Shift + ";
            if (modifier.HasFlag(ModifierKeys.Windows)) text += "Win + ";
            text += key.ToString();
            // WPF ModifierKeys (Alt=1, Ctrl=2, Shift=4, Win=8) entsprechen den Bits im Keyboard-Hook
            modifiers = (uint)modifier;
            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            return true;
        }

        // Hotkey Einlese-Logik für Profile (PreviewKeyDown, damit keine Zeichen im Feld landen)
        private void HotkeyTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Tab soll weiterhin zum nächsten Feld springen
            if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) return;
            e.Handled = true;
            if (!TryReadHotkey(e, out string hotkeyText, out uint mods, out uint vk, out bool clear)) return;

            if (sender is System.Windows.Controls.TextBox tb && DataContext is MainViewModel vm &&
                tb.DataContext is EventFlowStudio.Models.TriggerCondition trigger)
            {
                trigger.TargetName = clear ? string.Empty : hotkeyText;
                trigger.HotkeyModifier = mods;
                trigger.HotkeyVK = vk;
                vm.MarkDirty();
            }
        }

        // Hotkey Einlese-Logik für die App-Settings (Fenster umschalten)
        private void SettingsHotkey_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) return;
            e.Handled = true;
            if (!TryReadHotkey(e, out string hotkeyText, out uint mods, out uint vk, out bool clear)) return;

            if (DataContext is MainViewModel vm)
            {
                vm.AppSettings.ToggleHotkeyText = clear ? string.Empty : hotkeyText;
                vm.AppSettings.ToggleHotkeyModifier = mods;
                vm.AppSettings.ToggleHotkeyVK = vk;
                vm.MarkDirty();
            }
        }
    }
}