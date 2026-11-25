using System.Configuration;
using System.Data;
using System.Windows;

namespace EventFlowStudio
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private static readonly string CrashLog = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "EventFlowStudio", "crash.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            // Ein Fehler in einer Aktion oder einem Trigger soll die Tray-App nicht still beenden
            DispatcherUnhandledException += (s, args) =>
            {
                WriteCrashLog(args.Exception);
                args.Handled = true;
                System.Windows.MessageBox.Show($"Unerwarteter Fehler: {args.Exception.Message}\n\nDetails: {CrashLog}",
                    "EventFlow Studio", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            System.AppDomain.CurrentDomain.UnhandledException += (s, args) => WriteCrashLog(args.ExceptionObject as System.Exception);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) => { WriteCrashLog(args.Exception); args.SetObserved(); };

            base.OnStartup(e);
        }

        private static void WriteCrashLog(System.Exception? ex)
        {
            if (ex == null) return;
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CrashLog)!);
                System.IO.File.AppendAllText(CrashLog, $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
            }
            catch { }
        }
    }

}
