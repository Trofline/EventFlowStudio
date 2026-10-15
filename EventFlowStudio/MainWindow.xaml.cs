using EventFlowStudio.ViewModels;
using System;
using System.Windows;
using System.Windows.Input;

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
            this.Closing += MainWindow_Closing;
        }

        private void SetupTrayIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            _notifyIcon.Visible = true;
            _notifyIcon.Text = "EventFlow Studio";

            _notifyIcon.DoubleClick += (s, args) => ShowMainWindow();

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("EventFlow öffnen", null, (s, e) => ShowMainWindow());
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            contextMenu.Items.Add("Beenden", null, (s, e) => QuitApplication());
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void ShowMainWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
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
            // Prüft, ob System-Tray in den Settings aktiviert ist
            bool trayEnabled = true;
            if (DataContext is MainViewModel vm)
            {
                trayEnabled = vm.AppSettings.EnableSystemTray;
            }

            if (!_isActualClose && trayEnabled)
            {
                e.Cancel = true;
                this.Hide();
            }
            else
            {
                _notifyIcon?.Dispose();
            }
        }

        // Hotkey Einlese-Logik für den Hotkey Recorder
        // Hotkey Einlese-Logik für den Hotkey Recorder
        private void HotkeyTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true;

            var modifier = System.Windows.Input.Keyboard.Modifiers;
            var key = (e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key);

            if (key == System.Windows.Input.Key.LeftCtrl || key == System.Windows.Input.Key.RightCtrl ||
                key == System.Windows.Input.Key.LeftAlt || key == System.Windows.Input.Key.RightAlt ||
                key == System.Windows.Input.Key.LeftShift || key == System.Windows.Input.Key.RightShift ||
                key == System.Windows.Input.Key.LWin || key == System.Windows.Input.Key.RWin)
            {
                return;
            }

            string hotkeyText = "";
            if (modifier.HasFlag(System.Windows.Input.ModifierKeys.Control)) hotkeyText += "Ctrl + ";
            if (modifier.HasFlag(System.Windows.Input.ModifierKeys.Alt)) hotkeyText += "Alt + ";
            if (modifier.HasFlag(System.Windows.Input.ModifierKeys.Shift)) hotkeyText += "Shift + ";
            if (modifier.HasFlag(System.Windows.Input.ModifierKeys.Windows)) hotkeyText += "Win + ";

            hotkeyText += key.ToString();

            if (sender is System.Windows.Controls.TextBox tb)
            {
                tb.Text = hotkeyText;

                // K Korrigiert: Zugriff auf den aktuellen Trigger der Liste (z.B. den ersten Trigger)
                if (DataContext is MainViewModel vm && vm.SelectedPreset != null)
                {
                    // Wir suchen das passende Trigger-Element aus der Liste des DataContexts der TextBox
                    if (tb.DataContext is EventFlowStudio.Models.TriggerCondition trigger)
                    {
                        trigger.TargetName = hotkeyText;
                        trigger.HotkeyModifier = (uint)modifier;
                        trigger.HotkeyVK = (uint)KeyInterop.VirtualKeyFromKey(key);
                        vm.MarkDirty();
                    }
                }
            }
        }
    }
}