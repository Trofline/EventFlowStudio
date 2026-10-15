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
        public string SystemTrayCheckbox => CurrentLanguage == "Deutsch" ? "In den System-Tray (neben die Uhr) minimieren beim Schließen" : "Minimize to System Tray on close";
        public string AutoStartCheckbox => CurrentLanguage == "Deutsch" ? "EventFlow Studio automatisch beim Windows-Start öffnen" : "Start EventFlow Studio automatically with Windows";
        public string AlwaysAdminCheckbox => CurrentLanguage == "Deutsch" ? "Immer als Administrator ausführen" : "Always run as Administrator";
        public string RestartAdminBtn => CurrentLanguage == "Deutsch" ? "🔄 Jetzt mit Admin-Rechten neu starten" : "🔄 Restart with Admin rights now";

        public string UpdatesHeader => CurrentLanguage == "Deutsch" ? "Updates" : "Updates";
        public string AutoUpdateCheckbox => CurrentLanguage == "Deutsch" ? "Beim Start automatisch nach Updates suchen und installieren" : "Automatically check for and install updates on startup";
        public string SaveSettingsBtn => CurrentLanguage == "Deutsch" ? "💾 Einstellungen Speichern" : "💾 Save Settings";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}