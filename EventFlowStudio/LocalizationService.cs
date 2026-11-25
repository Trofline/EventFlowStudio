using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EventFlowStudio.Services
{
    public class LocalizationService : INotifyPropertyChanged
    {
        private static LocalizationService? _instance;
        public static LocalizationService Instance => _instance ??= new LocalizationService();

        private string _currentLanguage = "Deutsch";

        public string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    OnPropertyChanged(null);
                }
            }
        }

        public string AppTitle => "⚡ EventFlow Studio";
        public string TabProfiles => CurrentLanguage == "Deutsch" ? "📁 Meine Profile" : "📁 My Profiles";
        public string TabSettings => CurrentLanguage == "Deutsch" ? "⚙️ Einstellungen" : "⚙️ Settings";
        public string LiveLogsHeader => CurrentLanguage == "Deutsch" ? "LIVE LOGS" : "LIVE LOGS";

        public string BannerTitle => CurrentLanguage == "Deutsch" ? "💡 Willkommen bei EventFlow Studio!" : "💡 Welcome to EventFlow Studio!";
        public string BannerText => CurrentLanguage == "Deutsch" ? " Erstelle Profile und weise mehrere Auslöser zu. Die Ausführung blockiert deine Eingaben im Spiel nicht!" : " Create profiles and assign multiple triggers. Execution will not block your in-game inputs!";
        public string NewProfileBtn => CurrentLanguage == "Deutsch" ? "+ Neues Profil" : "+ New Profile";
        public string ImportBtn => CurrentLanguage == "Deutsch" ? "📥 Importieren" : "📥 Import";
        public string ExportBtn => CurrentLanguage == "Deutsch" ? "📤 Exportieren" : "📤 Export";
        public string ProfileEditorHeader => CurrentLanguage == "Deutsch" ? "PROFIL-EDITOR" : "PROFILE EDITOR";

        public string UnsavedText => CurrentLanguage == "Deutsch" ? "* (Nicht gespeichert)" : "* (Unsaved)";
        public string TriggerBtn => "▶ Trigger";
        public string SaveBtn => CurrentLanguage == "Deutsch" ? "💾 Speichern" : "💾 Save";

        public string TriggersHeader => CurrentLanguage == "Deutsch" ? "1. AUSLÖSER (TRIGGER)" : "1. TRIGGERS";
        public string AddTriggerBtn => CurrentLanguage == "Deutsch" ? "+ Auslöser hinzufügen" : "+ Add Trigger";
        public string ActionsHeader => CurrentLanguage == "Deutsch" ? "2. AKTIONEN" : "2. ACTIONS";
        public string AddActionBtn => CurrentLanguage == "Deutsch" ? "+ Aktion hinzufügen" : "+ Add Action";
        public string RunAsAdminCheckbox => CurrentLanguage == "Deutsch" ? "Als Admin ausführen" : "Run as Admin";
        public string TerminateCheckbox => CurrentLanguage == "Deutsch" ? "Bei Trigger-Ende töten" : "Kill on Trigger Exit";
        public string ExitDelayLabel => CurrentLanguage == "Deutsch" ? "Exit-Verzögerung (Sek.):" : "Exit Delay (sec):";

        public string UnsavedWarningTitle => CurrentLanguage == "Deutsch" ? "Ungespeicherte Änderungen" : "Unsaved Changes";
        public string UnsavedWarningMessage => CurrentLanguage == "Deutsch" ? "Du hast ungespeicherte Änderungen.\nMöchtest du diese vor dem Schließen speichern?" : "You have unsaved changes.\nDo you want to save them before closing?";

        public string SettingsHeader => CurrentLanguage == "Deutsch" ? "⚙️ GLOBALE EINSTELLUNGEN" : "⚙️ GLOBAL SETTINGS";
        public string UiSettingsHeader => CurrentLanguage == "Deutsch" ? "Benutzeroberfläche" : "User Interface";
        public string HideBannerCheckbox => CurrentLanguage == "Deutsch" ? "Hinweis-Banner auf der Hauptseite ausblenden" : "Hide instruction banner on the main page";
        public string LanguageLabel => CurrentLanguage == "Deutsch" ? "Anzeigesprache:" : "Display Language:";

        public string SystemIntegrationHeader => CurrentLanguage == "Deutsch" ? "System Integration & Tray" : "System Integration & Tray";
        public string SystemTrayCheckbox => CurrentLanguage == "Deutsch" ? "In den System-Tray minimieren beim Schließen" : "Minimize to System Tray on close";
        // NEU:
        public string StartMinimizedCheckbox => CurrentLanguage == "Deutsch" ? "Minimiert im Hintergrund starten" : "Start minimized in background";
        public string ToggleHotkeyLabel => CurrentLanguage == "Deutsch" ? "Hotkey zum Anzeigen/Verstecken:" : "Show/Hide Window Hotkey:";

        public string AutoStartCheckbox => CurrentLanguage == "Deutsch" ? "EventFlow Studio automatisch beim Windows-Start öffnen" : "Start EventFlow Studio automatically with Windows";
        public string AlwaysAdminCheckbox => CurrentLanguage == "Deutsch" ? "Immer als Administrator ausführen" : "Always run as Administrator";
        public string RestartAdminBtn => CurrentLanguage == "Deutsch" ? "🔄 Jetzt mit Admin-Rechten neu starten" : "🔄 Restart with Admin rights now";

        public string UpdatesHeader => CurrentLanguage == "Deutsch" ? "Updates" : "Updates";
        public string AutoUpdateCheckbox => CurrentLanguage == "Deutsch" ? "Beim Start automatisch nach Updates suchen und installieren" : "Automatically check for and install updates on startup";
        public string SaveSettingsBtn => CurrentLanguage == "Deutsch" ? "💾 Einstellungen Speichern" : "💾 Save Settings";

        // NEU: Zusatzfelder für Aktionen und Profile
        public string ArgumentsLabel => CurrentLanguage == "Deutsch" ? "Argumente:" : "Arguments:";
        public string WindowStateLabel => CurrentLanguage == "Deutsch" ? "Fenster:" : "Window:";
        public string StartDelayLabel => CurrentLanguage == "Deutsch" ? "Start-Verzögerung (Sek.):" : "Start delay (sec):";
        public string CooldownLabel => CurrentLanguage == "Deutsch" ? "Cooldown (Sek.):" : "Cooldown (sec):";
        public string CooldownHint => CurrentLanguage == "Deutsch" ? "Mindestabstand zwischen zwei Ausführungen dieses Profils. 0 = aus." : "Minimum time between two runs of this profile. 0 = off.";

        private bool De => CurrentLanguage == "Deutsch";

        // Anzeigename eines Trigger-/Aktions-Typs bzw. Fensterzustands in der aktuellen Sprache
        public string GetDisplayName(object? value) => value switch
        {
            Models.TriggerType.ProcessStarted => De ? "Programm gestartet" : "Program started",
            Models.TriggerType.ProcessStopped => De ? "Programm beendet" : "Program stopped",
            Models.TriggerType.BluetoothConnected => De ? "Bluetooth verbunden" : "Bluetooth connected",
            Models.TriggerType.BluetoothDisconnected => De ? "Bluetooth getrennt" : "Bluetooth disconnected",
            Models.TriggerType.UsbDeviceConnected => De ? "USB-Gerät angesteckt" : "USB device plugged in",
            Models.TriggerType.UsbDeviceDisconnected => De ? "USB-Gerät abgesteckt" : "USB device removed",
            Models.TriggerType.GlobalHotkey => De ? "Tastenkombination" : "Hotkey",
            Models.TriggerType.AppStartup => De ? "Beim Start von EventFlow" : "When EventFlow starts",
            Models.TriggerType.TimeOfDay => De ? "Uhrzeit (täglich)" : "Time of day (daily)",
            Models.TriggerType.Interval => De ? "Alle X Minuten" : "Every X minutes",
            Models.TriggerType.UserIdle => De ? "PC inaktiv seit X Min." : "PC idle for X min",
            Models.TriggerType.SessionLocked => De ? "PC gesperrt" : "PC locked",
            Models.TriggerType.SessionUnlocked => De ? "PC entsperrt" : "PC unlocked",

            Models.ActionType.StartProcess => De ? "Programm starten" : "Start program",
            Models.ActionType.KillProcess => De ? "Programm beenden" : "Kill program",
            Models.ActionType.ChangePowerPlan => De ? "Energieplan ändern" : "Change power plan",
            Models.ActionType.SendWebhook => De ? "Webhook senden" : "Send webhook",
            Models.ActionType.SystemNotification => De ? "Benachrichtigung" : "Notification",
            Models.ActionType.OpenUrl => De ? "Webseite/Datei öffnen" : "Open website/file",
            Models.ActionType.RunCommand => De ? "Befehl ausführen" : "Run command",
            Models.ActionType.LockWorkstation => De ? "PC sperren" : "Lock PC",
            Models.ActionType.ToggleMute => De ? "Ton stumm/laut" : "Toggle mute",
            Models.ActionType.EnableProfile => De ? "Profil aktivieren" : "Enable profile",
            Models.ActionType.DisableProfile => De ? "Profil deaktivieren" : "Disable profile",

            Models.WindowLaunchState.Normal => De ? "Normal" : "Normal",
            Models.WindowLaunchState.Minimized => De ? "Minimiert" : "Minimized",
            Models.WindowLaunchState.Maximized => De ? "Maximiert" : "Maximized",
            Models.WindowLaunchState.Hidden => De ? "Unsichtbar" : "Hidden",

            null => string.Empty,
            _ => value.ToString() ?? string.Empty,
        };

        // Kurzer Hinweis, was ins Textfeld gehört
        public string GetHint(object? value) => value switch
        {
            Models.TriggerType.ProcessStarted or Models.TriggerType.ProcessStopped => De ? "Prozessname, z.B. ac_client oder spiel.exe (Teil des Namens reicht)" : "Process name, e.g. ac_client or game.exe (part of the name is enough)",
            Models.TriggerType.BluetoothConnected or Models.TriggerType.BluetoothDisconnected => De ? "Gerätename oder ein Teil davon, z.B. Headset" : "Device name or part of it, e.g. Headset",
            Models.TriggerType.UsbDeviceConnected or Models.TriggerType.UsbDeviceDisconnected => De ? "Gerätename oder ein Teil davon, z.B. Controller" : "Device name or part of it, e.g. Controller",
            Models.TriggerType.GlobalHotkey => De ? "Reinklicken und Tasten drücken. Entf/Esc löscht." : "Click and press the keys. Del/Esc clears.",
            Models.TriggerType.AppStartup => De ? "Läuft einmal, wenn EventFlow startet." : "Runs once when EventFlow starts.",
            Models.TriggerType.TimeOfDay => De ? "Uhrzeit HH:mm, mehrere mit Komma: 07:30, 18:00" : "Time HH:mm, several with commas: 07:30, 18:00",
            Models.TriggerType.Interval => De ? "Minuten, z.B. 30 (Dezimal erlaubt: 0,5)" : "Minutes, e.g. 30 (decimals allowed: 0.5)",
            Models.TriggerType.UserIdle => De ? "Minuten ohne Maus/Tastatur, z.B. 10" : "Minutes without mouse/keyboard, e.g. 10",
            Models.TriggerType.SessionLocked or Models.TriggerType.SessionUnlocked => De ? "Kein Wert nötig (Win + L)." : "No value needed (Win + L).",

            Models.ActionType.StartProcess => De ? "Pfad zur .exe (oder Datei/Verknüpfung)" : "Path to the .exe (or file/shortcut)",
            Models.ActionType.KillProcess => De ? "Prozessname oder Pfad, z.B. discord" : "Process name or path, e.g. discord",
            Models.ActionType.OpenUrl => De ? "URL oder Datei, z.B. https://twitch.tv" : "URL or file, e.g. https://twitch.tv",
            Models.ActionType.RunCommand => De ? "Befehl für cmd, z.B. ipconfig /flushdns (läuft unsichtbar)" : "cmd command, e.g. ipconfig /flushdns (runs hidden)",
            Models.ActionType.LockWorkstation or Models.ActionType.ToggleMute => De ? "Kein Wert nötig." : "No value needed.",
            Models.ActionType.EnableProfile or Models.ActionType.DisableProfile => De ? "Name des Profils" : "Profile name",
            _ => string.Empty,
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}