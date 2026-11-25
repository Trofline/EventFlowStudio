using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EventFlowStudio.ProcessPicker
{
    /// <summary>Ein Eintrag in der Prozess-Auswahl (gleiche Prozesse werden zusammengefasst).</summary>
    public class RunningProcessInfo : INotifyPropertyChanged
    {
        private ImageSource? _icon;
        private bool _isChecked;
        public event PropertyChangedEventHandler? PropertyChanged;

        // Haken in der Mehrfachauswahl
        public bool IsChecked { get => _isChecked; set { if (_isChecked == value) return; _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); } }

        public string ProcessName { get; init; } = string.Empty;
        public string? ExecutablePath { get; init; }
        public string? WindowTitle { get; init; }
        public string? Description { get; init; }
        public bool HasWindow { get; init; }
        public int InstanceCount { get; init; }
        public long WorkingSetBytes { get; init; }
        public ImageSource? Icon { get => _icon; set { _icon = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon))); } }

        public string DisplayName => !string.IsNullOrWhiteSpace(Description) ? Description! : ProcessName;
        public string FileName => ExecutablePath != null ? Path.GetFileName(ExecutablePath) : ProcessName + ".exe";
        public string PathText => ExecutablePath ?? "(Pfad nicht lesbar – evtl. Admin-Rechte nötig)";
        public string Subtitle => !string.IsNullOrWhiteSpace(WindowTitle) ? WindowTitle! : FileName;
        public string InstanceText => InstanceCount > 1 ? $"{InstanceCount}×" : string.Empty;
        public string MemoryText => $"{WorkingSetBytes / (1024 * 1024)} MB";
    }

    /// <summary>Liest die gerade laufenden Prozesse inkl. Pfad, Fenstertitel und Icon aus.</summary>
    public static class RunningProcessService
    {
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder exeName, ref int size);

        private static readonly HashSet<string> SystemProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Idle", "System", "Registry", "Secure System", "Memory Compression", "smss", "csrss", "wininit", "services", "lsass", "svchost", "fontdrvhost", "dwm", "LsaIso"
        };

        /// <summary>Holt alle Prozesse. Läuft synchron, daher aus einem Hintergrund-Thread aufrufen.</summary>
        public static List<RunningProcessInfo> GetRunningProcesses()
        {
            int ownPid = Environment.ProcessId;
            var raw = new List<(string Name, string? Path, string? Title, long Mem)>();

            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        if (p.Id == ownPid || p.Id == 0 || p.Id == 4) continue;
                        string? title = null;
                        try { if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.MainWindowTitle)) title = p.MainWindowTitle; } catch { }
                        long mem = 0;
                        try { mem = p.WorkingSet64; } catch { }
                        raw.Add((p.ProcessName, TryGetPath(p.Id), title, mem));
                    }
                    catch { /* Prozess ist zwischenzeitlich beendet */ }
                }
            }

            return raw
                .Where(r => !SystemProcessNames.Contains(r.Name))
                .GroupBy(r => (r.Name.ToLowerInvariant(), (r.Path ?? string.Empty).ToLowerInvariant()))
                .Select(g =>
                {
                    var first = g.First();
                    string? path = first.Path;
                    string? description = null;
                    if (path != null)
                    {
                        try { description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim(); } catch { }
                    }
                    return new RunningProcessInfo
                    {
                        ProcessName = first.Name,
                        ExecutablePath = path,
                        WindowTitle = g.Select(x => x.Title).FirstOrDefault(t => t != null),
                        Description = description,
                        HasWindow = g.Any(x => x.Title != null),
                        InstanceCount = g.Count(),
                        WorkingSetBytes = g.Sum(x => x.Mem),
                    };
                })
                .OrderByDescending(i => i.HasWindow)
                .ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Funktioniert auch für viele Prozesse, bei denen Process.MainModule eine Exception wirft
        /// (z.B. 32/64-Bit-Mix). Für Admin-Prozesse braucht man trotzdem Admin-Rechte.
        /// </summary>
        public static string? TryGetPath(int pid)
        {
            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString(0, size) : null;
            }
            finally { CloseHandle(handle); }
        }

        /// <summary>Lädt das Icon einer EXE als eingefrorenes WPF-Bild (thread-sicher).</summary>
        public static ImageSource? TryLoadIcon(string? exePath)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon == null) return null;
                var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                source.Freeze();
                return source;
            }
            catch { return null; }
        }
    }
}
