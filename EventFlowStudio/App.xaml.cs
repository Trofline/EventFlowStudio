using System;
using System.Threading;
using System.Windows;

namespace EventFlowStudio
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        // Pro Windows-Benutzer nur eine Instanz. Zwei Instanzen (z.B. Autostart + manueller Start)
        // hätten jedes Profil doppelt ausgeführt.
        private const string MutexName = @"Local\EventFlowStudio_SingleInstance";
        private const string ShowEventName = @"Local\EventFlowStudio_ShowWindow";

        private static Mutex? _instanceMutex;
        private static bool _ownsMutex;
        private EventWaitHandle? _showEvent;
        private RegisteredWaitHandle? _showWait;

        protected override void OnStartup(StartupEventArgs e)
        {
            _instanceMutex = new Mutex(false, MutexName);
            try
            {
                // Beim Neustart als Admin läuft die alte Instanz noch kurz weiter, daher etwas warten
                _ownsMutex = _instanceMutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                _ownsMutex = true; // Vorherige Instanz ist abgestürzt
            }

            if (!_ownsMutex)
            {
                // Andere Instanz läuft: deren Fenster nach vorne holen und selbst beenden
                try
                {
                    if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
                    {
                        using (existing) existing.Set();
                    }
                }
                catch { }
                Shutdown();
                return;
            }

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) =>
                Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowMainWindow()), null, Timeout.Infinite, false);

            base.OnStartup(e);

            // Fenster erst nach der Instanz-Prüfung erzeugen (statt StartupUri), sonst würde auch
            // die zweite Instanz kurz die Engine starten und Profile auslösen.
            var window = new MainWindow();
            MainWindow = window;
            // "Minimiert starten" mit Tray: Fenster gar nicht erst anzeigen (vorher blitzte es kurz auf)
            if (!window.StartsHiddenInTray) window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _showWait?.Unregister(null);
            _showEvent?.Dispose();
            ReleaseSingleInstance();
            base.OnExit(e);
        }

        public static void ReleaseSingleInstance()
        {
            if (_ownsMutex)
            {
                _ownsMutex = false;
                try { _instanceMutex?.ReleaseMutex(); } catch { }
            }
        }
    }

}
