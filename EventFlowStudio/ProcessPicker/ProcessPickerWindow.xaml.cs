using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace EventFlowStudio.ProcessPicker
{
    /// <summary>
    /// Dialog, der die gerade laufenden Apps/Prozesse auflistet.
    /// Nach "Übernehmen" steht die Auswahl in <see cref="SelectedProcess"/>.
    /// </summary>
    public partial class ProcessPickerWindow : Window
    {
        private List<RunningProcessInfo> _all = new();
        private ICollectionView? _view;

        public RunningProcessInfo? SelectedProcess { get; private set; }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            DarkTitleBar.Apply(this);
        }

        public ProcessPickerWindow(string? header = null, bool showAllProcesses = false)
        {
            InitializeComponent();
            if (!string.IsNullOrWhiteSpace(header)) { HeaderText.Text = header; Title = header; }
            if (showAllProcesses) AllToggle.IsChecked = true;

            Loaded += async (_, _) =>
            {
                SearchBox.Focus();
                await LoadAsync();
            };
        }

        /// <summary>Öffnet den Dialog und gibt die Auswahl zurück (oder null bei Abbruch).</summary>
        public static RunningProcessInfo? Pick(Window? owner, string? header = null, bool showAllProcesses = false)
        {
            var dialog = new ProcessPickerWindow(header, showAllProcesses);
            if (owner != null && owner.IsVisible) dialog.Owner = owner;
            else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return dialog.ShowDialog() == true ? dialog.SelectedProcess : null;
        }

        private async Task LoadAsync()
        {
            StatusText.Text = "Lade laufende Prozesse…";
            StatusText.Visibility = Visibility.Visible;
            ProcessList.ItemsSource = null;

            try
            {
                _all = await Task.Run(() => RunningProcessService.GetRunningProcesses());
            }
            catch (Exception ex)
            {
                _all = new List<RunningProcessInfo>();
                StatusText.Text = "Prozesse konnten nicht gelesen werden: " + ex.Message;
                return;
            }

            _view = CollectionViewSource.GetDefaultView(_all);
            _view.Filter = FilterItem;
            ProcessList.ItemsSource = _view;
            UpdateStatus();

            // Icons im Hintergrund nachladen, damit die Liste sofort da ist
            var items = _all.ToList();
            _ = Task.Run(() =>
            {
                foreach (var item in items)
                {
                    var icon = RunningProcessService.TryLoadIcon(item.ExecutablePath);
                    if (icon != null) Dispatcher.InvokeAsync(() => item.Icon = icon);
                }
            });
        }

        private bool FilterItem(object obj)
        {
            if (obj is not RunningProcessInfo p) return false;
            if (AppsOnlyToggle.IsChecked == true && !p.HasWindow) return false;

            string q = SearchBox.Text.Trim();
            if (q.Length == 0) return true;

            return Contains(p.ProcessName, q) || Contains(p.DisplayName, q) || Contains(p.WindowTitle, q) || Contains(p.ExecutablePath, q);
        }

        private static bool Contains(string? text, string query) =>
            text != null && text.Contains(query, StringComparison.CurrentCultureIgnoreCase);

        private void UpdateStatus()
        {
            if (_view == null) return;
            int visible = _view.Cast<object>().Count();
            CountText.Text = $"{visible} von {_all.Count} Einträgen";

            if (visible == 0)
            {
                StatusText.Text = AppsOnlyToggle.IsChecked == true && SearchBox.Text.Length > 0
                    ? "Nichts gefunden. Tipp: Unter „Alle Prozesse“ sind auch Hintergrundprozesse dabei."
                    : "Nichts gefunden.";
                StatusText.Visibility = Visibility.Visible;
            }
            else
            {
                StatusText.Visibility = Visibility.Collapsed;
                if (ProcessList.SelectedItem == null || !_view.Contains(ProcessList.SelectedItem))
                    ProcessList.SelectedIndex = 0;
            }
        }

        private void Refresh()
        {
            _view?.Refresh();
            UpdateStatus();
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Refresh();

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            Refresh();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private void ProcessList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            SelectButton.IsEnabled = ProcessList.SelectedItem != null;
            if (ProcessList.SelectedItem != null) ProcessList.ScrollIntoView(ProcessList.SelectedItem);
        }

        private void ProcessList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ProcessList.SelectedItem != null) Confirm();
        }

        private void Select_Click(object sender, RoutedEventArgs e) => Confirm();

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter when ProcessList.SelectedItem != null:
                    Confirm();
                    e.Handled = true;
                    break;
                // Pfeiltasten im Suchfeld bewegen die Auswahl in der Liste
                case Key.Down when SearchBox.IsKeyboardFocused:
                    MoveSelection(+1);
                    e.Handled = true;
                    break;
                case Key.Up when SearchBox.IsKeyboardFocused:
                    MoveSelection(-1);
                    e.Handled = true;
                    break;
            }
        }

        private void MoveSelection(int delta)
        {
            int count = ProcessList.Items.Count;
            if (count == 0) return;
            ProcessList.SelectedIndex = Math.Clamp(ProcessList.SelectedIndex + delta, 0, count - 1);
        }

        private void Confirm()
        {
            SelectedProcess = ProcessList.SelectedItem as RunningProcessInfo;
            if (SelectedProcess == null) return;
            DialogResult = true;
        }
    }
}
