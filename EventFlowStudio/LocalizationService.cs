using System.Collections.Generic;
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

        public string ArgumentsLabel => De ? "Argumente:" : "Arguments:";
        public string DelayLabel => De ? "Verzögerung vorher (Sek.):" : "Delay before (sec):";
        public string VolumeLabel => De ? "Lautstärke (%):" : "Volume (%):";

        private bool De => CurrentLanguage == "Deutsch";

        // Anzeigenamen für alle Auswahllisten (Trigger, Aktionen, Unteroptionen)
        private static readonly Dictionary<string, (string De, string En)> EnumNames = new()
        {
            ["ProcessStarted"] = ("Prozess gestartet", "Process started"),
            ["ProcessStopped"] = ("Prozess beendet", "Process stopped"),
            ["BluetoothConnected"] = ("Bluetooth verbunden", "Bluetooth connected"),
            ["BluetoothDisconnected"] = ("Bluetooth getrennt", "Bluetooth disconnected"),
            ["UsbDeviceConnected"] = ("USB-Gerät angeschlossen", "USB device connected"),
            ["UsbDeviceDisconnected"] = ("USB-Gerät entfernt", "USB device removed"),
            ["GlobalHotkey"] = ("Tastenkürzel", "Hotkey"),
            ["AppStartup"] = ("Start von EventFlow", "EventFlow startup"),
            ["TimeOfDay"] = ("Uhrzeit", "Time of day"),
            ["Interval"] = ("Alle X Minuten", "Every X minutes"),
            ["UserIdle"] = ("PC inaktiv", "PC idle"),
            ["UserReturned"] = ("Zurück am PC", "User returned"),
            ["ForegroundApp"] = ("App im Vordergrund", "App in foreground"),
            ["NetworkConnected"] = ("Netzwerk/WLAN verbunden", "Network/Wi-Fi connected"),
            ["NetworkDisconnected"] = ("Netzwerk/WLAN getrennt", "Network/Wi-Fi disconnected"),
            ["PowerPluggedIn"] = ("Netzteil angeschlossen", "Charger plugged in"),
            ["PowerUnplugged"] = ("Netzteil getrennt", "Charger unplugged"),
            ["BatteryLow"] = ("Akku niedrig", "Battery low"),
            ["SessionLocked"] = ("PC gesperrt", "PC locked"),
            ["SessionUnlocked"] = ("PC entsperrt", "PC unlocked"),
            ["SystemResumed"] = ("Aus Standby aufgewacht", "Resumed from sleep"),

            ["StartProcess"] = ("Programm starten", "Start program"),
            ["KillProcess"] = ("Prozess beenden", "Kill process"),
            ["CloseProcess"] = ("Fenster schließen", "Close window"),
            ["ChangePowerPlan"] = ("Energieplan", "Power plan"),
            ["SendWebhook"] = ("Webhook senden", "Send webhook"),
            ["SystemNotification"] = ("Benachrichtigung", "Notification"),
            ["OpenUrl"] = ("URL/Datei öffnen", "Open URL/file"),
            ["RunCommand"] = ("PowerShell-Befehl", "PowerShell command"),
            ["AudioControl"] = ("Lautstärke", "Volume"),
            ["MediaControl"] = ("Medientaste", "Media key"),
            ["SystemPower"] = ("Sperren/Energie", "Lock/Power"),

            ["SetVolume"] = ("Lautstärke setzen", "Set volume"),
            ["Mute"] = ("Stumm", "Mute"),
            ["Unmute"] = ("Ton an", "Unmute"),
            ["ToggleMute"] = ("Stumm umschalten", "Toggle mute"),
            ["PlayPause"] = ("Play/Pause", "Play/Pause"),
            ["NextTrack"] = ("Nächster Titel", "Next track"),
            ["PreviousTrack"] = ("Vorheriger Titel", "Previous track"),
            ["Stop"] = ("Stopp", "Stop"),
            ["VolumeUp"] = ("Lauter", "Volume up"),
            ["VolumeDown"] = ("Leiser", "Volume down"),
            ["LockScreen"] = ("PC sperren", "Lock PC"),
            ["MonitorOff"] = ("Bildschirm aus", "Turn off display"),
            ["Sleep"] = ("Energiesparmodus", "Sleep"),
            ["Hibernate"] = ("Ruhezustand", "Hibernate"),
            ["SignOut"] = ("Abmelden", "Sign out"),
            ["Shutdown"] = ("Herunterfahren (15 Sek.)", "Shut down (15 s)"),
            ["Restart"] = ("Neu starten (15 Sek.)", "Restart (15 s)"),
            ["Balanced"] = ("Ausbalanciert", "Balanced"),
            ["HighPerformance"] = ("Höchstleistung", "High performance"),
            ["PowerSaver"] = ("Energiesparen", "Power saver"),
        };

        public string DisplayName(object? value)
        {
            if (value == null) return string.Empty;
            string key = value.ToString() ?? string.Empty;
            return EnumNames.TryGetValue(key, out var n) ? (De ? n.De : n.En) : key;
        }

        // Hinweis unter dem Eingabefeld: was muss man eintragen?
        public string TriggerHint(object? type) => type?.ToString() switch
        {
            "ProcessStarted" or "ProcessStopped" => De ? "Prozessname, z.B. notepad oder steam" : "Process name, e.g. notepad or steam",
            "BluetoothConnected" or "BluetoothDisconnected" => De ? "Teil des Gerätenamens, z.B. WH-1000XM4" : "Part of the device name, e.g. WH-1000XM4",
            "UsbDeviceConnected" or "UsbDeviceDisconnected" => De ? "Teil des Gerätenamens, z.B. Xbox Controller" : "Part of the device name, e.g. Xbox Controller",
            "GlobalHotkey" => De ? "Ins Feld klicken und die Tastenkombination drücken" : "Click the box and press the key combination",
            "AppStartup" => De ? "Löst einmal aus, wenn EventFlow startet" : "Fires once when EventFlow starts",
            "TimeOfDay" => De ? "Uhrzeit(en), z.B. 07:30 oder 08:00, 18:30" : "Time(s), e.g. 07:30 or 08:00, 18:30",
            "Interval" => De ? "Minuten zwischen zwei Ausführungen, z.B. 30" : "Minutes between runs, e.g. 30",
            "UserIdle" => De ? "Minuten ohne Maus/Tastatur, z.B. 10 (leer = 5)" : "Minutes without mouse/keyboard, e.g. 10 (empty = 5)",
            "UserReturned" => De ? "Nach wie vielen Minuten Pause, z.B. 10 (leer = 5)" : "After how many idle minutes, e.g. 10 (empty = 5)",
            "ForegroundApp" => De ? "Prozessname der App, die nach vorne kommt, z.B. chrome" : "Process name of the app that comes to front, e.g. chrome",
            "NetworkConnected" or "NetworkDisconnected" => De ? "WLAN-Name (SSID), leer = jede Internetverbindung" : "Wi-Fi name (SSID), empty = any connection",
            "PowerPluggedIn" => De ? "Löst aus, wenn das Netzteil angeschlossen wird" : "Fires when the charger is plugged in",
            "PowerUnplugged" => De ? "Löst aus, wenn der Laptop auf Akku wechselt" : "Fires when the laptop switches to battery",
            "BatteryLow" => De ? "Prozent, z.B. 15 (leer = 20)" : "Percent, e.g. 15 (empty = 20)",
            "SessionLocked" => De ? "Löst aus, wenn der PC gesperrt wird (Win + L)" : "Fires when the PC is locked (Win + L)",
            "SessionUnlocked" => De ? "Löst aus, wenn du den PC entsperrst" : "Fires when you unlock the PC",
            "SystemResumed" => De ? "Löst aus, wenn der PC aus Energiesparmodus/Ruhezustand aufwacht" : "Fires when the PC wakes from sleep/hibernate",
            _ => string.Empty,
        };

        public string ActionHint(object? type) => type?.ToString() switch
        {
            "StartProcess" => De ? "Pfad zur .exe oder Datei" : "Path to the .exe or file",
            "KillProcess" => De ? "Prozessname oder Pfad, wird sofort beendet" : "Process name or path, terminated immediately",
            "CloseProcess" => De ? "Prozessname oder Pfad, wird wie mit X geschlossen" : "Process name or path, closed like clicking X",
            "OpenUrl" => De ? "z.B. https://youtube.com, spotify: oder C:\\Ordner" : "e.g. https://youtube.com, spotify: or C:\\Folder",
            "RunCommand" => De ? "PowerShell, z.B. Stop-Service Spooler" : "PowerShell, e.g. Stop-Service Spooler",
            _ => string.Empty,
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}