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
            Models.TriggerType.UserReturned => De ? "Zurück am PC (nach X Min.)" : "Back at PC (after X min)",
            Models.TriggerType.ForegroundApp => De ? "App im Vordergrund" : "App in foreground",
            Models.TriggerType.NetworkConnected => De ? "Netzwerk/WLAN verbunden" : "Network/Wi-Fi connected",
            Models.TriggerType.NetworkDisconnected => De ? "Netzwerk/WLAN getrennt" : "Network/Wi-Fi disconnected",
            Models.TriggerType.PowerPluggedIn => De ? "Netzteil angeschlossen" : "Charger plugged in",
            Models.TriggerType.PowerUnplugged => De ? "Netzteil getrennt (Akku)" : "Charger unplugged (battery)",
            Models.TriggerType.BatteryLow => De ? "Akku unter X %" : "Battery below X %",
            Models.TriggerType.SystemResumed => De ? "Aus Standby aufgewacht" : "Resumed from sleep",

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
            Models.ActionType.CloseProcess => De ? "Fenster schließen (sanft)" : "Close window (gently)",
            Models.ActionType.SetVolume => De ? "Lautstärke setzen" : "Set volume",
            Models.ActionType.MuteAudio => De ? "Ton stumm" : "Mute",
            Models.ActionType.UnmuteAudio => De ? "Ton an" : "Unmute",
            Models.ActionType.MediaControl => De ? "Medientaste" : "Media key",
            Models.ActionType.SystemPower => De ? "Energie (Standby, Aus ...)" : "Power (sleep, shut down ...)",
            Models.ActionType.PauseApps => De ? "Apps pausieren (solange Trigger läuft)" : "Pause apps (while trigger runs)",
            Models.ActionType.RestorePausedApps => De ? "Pausierte Apps wiederherstellen" : "Restore paused apps",

            Models.MediaKey.PlayPause => "Play/Pause",
            Models.MediaKey.NextTrack => De ? "Nächster Titel" : "Next track",
            Models.MediaKey.PreviousTrack => De ? "Vorheriger Titel" : "Previous track",
            Models.MediaKey.Stop => De ? "Stopp" : "Stop",
            Models.MediaKey.VolumeUp => De ? "Lauter" : "Volume up",
            Models.MediaKey.VolumeDown => De ? "Leiser" : "Volume down",

            Models.SystemPowerCommand.MonitorOff => De ? "Bildschirm aus" : "Display off",
            Models.SystemPowerCommand.Sleep => De ? "Energiesparmodus" : "Sleep",
            Models.SystemPowerCommand.Hibernate => De ? "Ruhezustand" : "Hibernate",
            Models.SystemPowerCommand.SignOut => De ? "Abmelden" : "Sign out",
            Models.SystemPowerCommand.Shutdown => De ? "Herunterfahren (15 Sek.)" : "Shut down (15 s)",
            Models.SystemPowerCommand.Restart => De ? "Neu starten (15 Sek.)" : "Restart (15 s)",

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
            Models.TriggerType.UserReturned => De ? "Wie lange du mindestens weg warst, in Minuten, z.B. 10" : "How long you were away at least, in minutes, e.g. 10",
            Models.TriggerType.ForegroundApp => De ? "Prozessname der App, die nach vorne kommt, z.B. chrome" : "Process name of the app that comes to front, e.g. chrome",
            Models.TriggerType.NetworkConnected or Models.TriggerType.NetworkDisconnected => De ? "WLAN-Name (oder Teil davon). Leer = jede Internetverbindung." : "Wi-Fi name (or part of it). Empty = any connection.",
            Models.TriggerType.PowerPluggedIn or Models.TriggerType.PowerUnplugged => De ? "Kein Wert nötig (Laptop)." : "No value needed (laptop).",
            Models.TriggerType.BatteryLow => De ? "Prozent, z.B. 20. Löst einmal aus, bis wieder geladen wird." : "Percent, e.g. 20. Fires once until charged again.",
            Models.TriggerType.SystemResumed => De ? "Kein Wert nötig. Nach Energiesparmodus oder Ruhezustand." : "No value needed. After sleep or hibernate.",

            Models.ActionType.StartProcess => De ? "Pfad zur .exe (oder Datei/Verknüpfung)" : "Path to the .exe (or file/shortcut)",
            Models.ActionType.KillProcess => De ? "Prozessname oder Pfad, z.B. discord" : "Process name or path, e.g. discord",
            Models.ActionType.OpenUrl => De ? "URL oder Datei, z.B. https://twitch.tv" : "URL or file, e.g. https://twitch.tv",
            Models.ActionType.RunCommand => De ? "Befehl für cmd, z.B. ipconfig /flushdns (läuft unsichtbar)" : "cmd command, e.g. ipconfig /flushdns (runs hidden)",
            Models.ActionType.LockWorkstation or Models.ActionType.ToggleMute => De ? "Kein Wert nötig." : "No value needed.",
            Models.ActionType.EnableProfile or Models.ActionType.DisableProfile => De ? "Name des Profils" : "Profile name",
            Models.ActionType.CloseProcess => De ? "Prozessname oder Pfad. Schließt wie mit X, die App kann noch speichern." : "Process name or path. Closes like clicking X, so the app can still save.",
            Models.ActionType.SetVolume => De ? "Lautstärke in Prozent, z.B. 30" : "Volume in percent, e.g. 30",
            Models.ActionType.MuteAudio or Models.ActionType.UnmuteAudio => De ? "Kein Wert nötig." : "No value needed.",
            Models.ActionType.MediaControl => De ? "Steuert Spotify, YouTube & Co. wie die Medientasten der Tastatur." : "Controls Spotify, YouTube etc. like the keyboard media keys.",
            Models.ActionType.ChangePowerPlan => De ? "Windows-Energieplan, der aktiviert wird." : "Windows power plan to activate.",
            Models.ActionType.SendWebhook => De ? "URL, an die ein JSON-POST geschickt wird (http/https)." : "URL that receives a JSON POST (http/https).",
            Models.ActionType.SystemNotification => De ? "Text der Benachrichtigung im Infobereich." : "Text of the tray notification.",
            Models.ActionType.PauseApps => De
                ? "Apps mit ; trennen oder über 🔍 mehrere auswählen. Sie werden geschlossen, solange das Trigger-Programm läuft, und danach wieder so gestartet, wie sie waren (minimiert, maximiert, Fenster oder Tray)."
                : "Separate apps with ; or pick several via 🔍. They are closed while the trigger program runs and restarted afterwards as they were (minimized, maximized, window or tray).",
            Models.ActionType.RestorePausedApps => De ? "Startet alle pausierten Apps sofort wieder (auch im Tray-Menü)." : "Restarts all paused apps right away (also in the tray menu).",
            Models.ActionType.SystemPower => De ? "Herunterfahren/Neustart lassen sich 15 Sek. lang mit \"shutdown /a\" abbrechen." : "Shut down/restart can be cancelled for 15 s with \"shutdown /a\".",
            _ => string.Empty,
        };

        public string PickRunningTooltip => CurrentLanguage == "Deutsch" ? "Aus laufenden Apps und Prozessen auswählen" : "Pick from running apps and processes";
        public string PickProcessTriggerTitle => CurrentLanguage == "Deutsch" ? "Prozess für den Trigger auswählen" : "Pick a process for the trigger";
        public string PickProcessPauseTitle => CurrentLanguage == "Deutsch" ? "Apps auswählen, die pausiert werden" : "Pick the apps to pause";
        public string RestoreDelayLabel => CurrentLanguage == "Deutsch" ? "Wiederherstellen nach (Sek.):" : "Restore after (sec):";
        public string TrayRestorePaused => CurrentLanguage == "Deutsch" ? "Pausierte Apps wiederherstellen" : "Restore paused apps";
        public string PickProcessActionTitle => CurrentLanguage == "Deutsch" ? "Programm für die Aktion auswählen" : "Pick a program for the action";
        public string PickProcessNoPathWarning => CurrentLanguage == "Deutsch"
            ? "Der Pfad dieses Prozesses ist ohne Admin-Rechte nicht lesbar. Es wurde nur der Name eingetragen. Zum Starten bitte den Pfad über 📁 auswählen oder EventFlow Studio als Administrator starten."
            : "This process's path can't be read without admin rights, so only its name was filled in. To start it, pick the file via 📁 or run EventFlow Studio as administrator.";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}