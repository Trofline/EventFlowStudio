using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace EventFlowStudio.Services
{
    /// <summary>Wie eine App beim Pausieren aussah.</summary>
    public enum PausedWindowState { Normal, Minimized, Maximized, Tray }

    /// <summary>Eine geschlossene App mit allem, was zum Wiederherstellen nötig ist.</summary>
    public class PausedApp
    {
        public string ProcessName { get; set; } = string.Empty;
        public string? ExecutablePath { get; set; }
        public string Arguments { get; set; } = string.Empty;
        public PausedWindowState State { get; set; } = PausedWindowState.Normal;

        // Fensterposition (aus GetWindowPlacement). Nur gültig, wenn HasPlacement.
        public bool HasPlacement { get; set; }
        public int PlacementFlags { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }
        public string? WindowTitle { get; set; }
    }

    /// <summary>Alle Apps, die eine "Apps pausieren"-Aktion geschlossen hat und die noch auf den Neustart warten.</summary>
    public class PauseSession
    {
        public string ActionId { get; set; } = string.Empty;
        public string PresetName { get; set; } = string.Empty;
        // Prozessnamen aus den "Prozess gestartet"-Triggern. Läuft keiner davon mehr, wird wiederhergestellt.
        public List<string> TriggerNames { get; set; } = new();
        public int RestoreDelaySeconds { get; set; }
        public DateTime PausedAt { get; set; } = DateTime.Now;
        public List<PausedApp> Apps { get; set; } = new();
    }

    /// <summary>
    /// Schließt ausgewählte Apps, solange ein Trigger-Programm läuft, und startet sie danach
    /// so wieder, wie sie waren (Pfad, Argumente, minimiert/maximiert/normal/Tray, Position).
    /// Der Zustand liegt in paused_apps.json, damit er einen Neustart/Absturz von EventFlow übersteht.
    /// Zugriff auf fremde Prozesse: nur Fenster-Nachrichten (WM_CLOSE), Prozessliste über WMI,
    /// Beenden über PROCESS_TERMINATE. Kein Lesen von Speicher, keine Module, keine Injektion.
    /// </summary>
    public class AppPauseService
    {
        public static readonly TimeSpan CloseGracePeriod = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan WindowWaitTime = TimeSpan.FromSeconds(30);

        // Wird nie geschlossen: Anti-Cheat (Spiel würde sofort beendet), Windows-Kernprozesse, EventFlow selbst.
        private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            // Anti-Cheat
            "vgc", "vgk", "vgtray", "EasyAntiCheat", "EasyAntiCheat_EOS", "EasyAntiCheat_EOS_Setup", "EasyAntiCheat_Setup",
            "BEService", "BEService_x64", "BEDaisy", "BattlEye", "BELauncher",
            "FACEIT", "faceitservice", "faceit_ac", "EAAntiCheat.GameService", "EAAntiCheat.GameServiceLauncher",
            "GameGuard", "GameMon", "GameMon64", "PnkBstrA", "PnkBstrB", "xhunter1", "ricochet", "mhyprot2", "nProtect",
            // Riot-Dienst, ohne den Valorant/LoL sofort beendet werden
            "RiotClientServices", "RiotClientCrashHandler",
            // Windows
            "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm",
            "explorer", "sihost", "ctfmon", "fontdrvhost", "conhost", "taskhostw", "RuntimeBroker", "ShellExperienceHost",
            "StartMenuExperienceHost", "SearchHost", "SearchApp", "TextInputHost", "audiodg", "MsMpEng", "SecurityHealthSystray",
            "SecurityHealthService", "LsaIso", "Memory Compression", "Secure System", "dllhost", "WmiPrvSE",
            "EventFlowStudio",
        };

        // Bekannte Schalter, mit denen Apps direkt im Tray starten
        private static readonly Dictionary<string, string> TrayStartArguments = new(StringComparer.OrdinalIgnoreCase)
        {
            { "steam", "-silent" },
            { "Discord", "--start-minimized" },
            { "DiscordPTB", "--start-minimized" },
            { "DiscordCanary", "--start-minimized" },
            { "Spotify", "--minimized" },
        };

        private readonly string _stateFile;
        private readonly Action<string> _log;
        private readonly object _fileLock = new();

        public List<PauseSession> Sessions { get; } = new();

        public AppPauseService(string appDataFolder, Action<string> log)
        {
            _stateFile = Path.Combine(appDataFolder, "paused_apps.json");
            _log = log;
        }

        // ---------- Reine Hilfsfunktionen (getestet) ----------

        /// <summary>Zerlegt "discord; C:\Programme\Spotify\Spotify.exe" in einzelne Einträge.</summary>
        public static List<string> ParseTargets(string? text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (var raw in text.Split(new[] { ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = raw.Trim().Trim('"').Trim();
                if (t.Length > 0 && !result.Contains(t, StringComparer.OrdinalIgnoreCase)) result.Add(t);
            }
            return result;
        }

        public static string JoinTargets(IEnumerable<string> targets) => string.Join("; ", targets);

        public static bool IsProtected(string processName)
        {
            string name = StripExe(processName);
            return ProtectedNames.Contains(name);
        }

        /// <summary>Exakter Treffer: Pfad-Einträge vergleichen den Pfad, sonst den Prozessnamen (ohne .exe). Kein Teilstring.</summary>
        public static bool TargetMatches(string target, string processName, string? exePath)
        {
            string t = (target ?? string.Empty).Trim().Trim('"');
            if (t.Length == 0) return false;
            string name = StripExe(processName);

            if (t.Contains('\\') || t.Contains('/'))
            {
                if (exePath != null) return string.Equals(NormalizePath(t), NormalizePath(exePath), StringComparison.OrdinalIgnoreCase);
                return string.Equals(StripExe(t[(t.LastIndexOfAny(new[] { '\\', '/' }) + 1)..]), name, StringComparison.OrdinalIgnoreCase);
            }
            return string.Equals(StripExe(t), name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Holt die Argumente aus einer Kommandozeile (alles nach dem Programmpfad).</summary>
        public static string ExtractArguments(string? commandLine, string? exePath)
        {
            string cmd = (commandLine ?? string.Empty).Trim();
            if (cmd.Length == 0) return string.Empty;

            if (cmd[0] == '"')
            {
                int end = cmd.IndexOf('"', 1);
                return end < 0 ? string.Empty : cmd[(end + 1)..].Trim();
            }
            if (!string.IsNullOrEmpty(exePath) && cmd.StartsWith(exePath, StringComparison.OrdinalIgnoreCase))
                return cmd[exePath.Length..].Trim();

            int space = cmd.IndexOf(' ');
            return space < 0 ? string.Empty : cmd[(space + 1)..].Trim();
        }

        /// <summary>Argumente für den Neustart. Tray-Apps bekommen, wenn bekannt, ihren "im Tray starten"-Schalter.</summary>
        public static string BuildRestartArguments(PausedApp app)
        {
            string args = app.Arguments ?? string.Empty;
            if (app.State == PausedWindowState.Tray && TrayStartArguments.TryGetValue(StripExe(app.ProcessName), out var trayArg) &&
                !args.Contains(trayArg, StringComparison.OrdinalIgnoreCase))
            {
                args = (args + " " + trayArg).Trim();
            }
            return args;
        }

        /// <summary>Eine Sitzung ist vorbei, wenn sie Trigger-Namen hat und keiner davon mehr läuft.</summary>
        public static bool IsSessionFinished(PauseSession session, IEnumerable<string> runningProcessNames)
        {
            if (session.TriggerNames.Count == 0) return false;
            var running = runningProcessNames as ICollection<string> ?? runningProcessNames.ToList();
            return !session.TriggerNames.Any(t => running.Any(r => ExecutionEngine.IsNameMatch(t, r)));
        }

        /// <summary>Neue Apps in eine bestehende Sitzung übernehmen, ohne den schon gemerkten Zustand zu überschreiben.</summary>
        public static int MergeApps(PauseSession session, IEnumerable<PausedApp> apps)
        {
            int added = 0;
            foreach (var app in apps)
            {
                bool known = session.Apps.Any(a => string.Equals(a.ExecutablePath ?? a.ProcessName, app.ExecutablePath ?? app.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                                                   string.Equals(a.Arguments, app.Arguments, StringComparison.OrdinalIgnoreCase));
                if (known) continue;
                session.Apps.Add(app);
                added++;
            }
            return added;
        }

        private static string NormalizePath(string path)
        {
            try { return Path.GetFullPath(path.Replace('/', '\\')); }
            catch { return path; }
        }

        private static string StripExe(string name)
        {
            string n = (name ?? string.Empty).Trim();
            return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && n.Length > 4 ? n[..^4] : n;
        }

        // ---------- Speichern / Laden ----------

        private static JsonSerializerOptions JsonOptions()
        {
            var o = new JsonSerializerOptions { WriteIndented = true };
            o.Converters.Add(new JsonStringEnumConverter());
            return o;
        }

        public void Load()
        {
            lock (_fileLock)
            {
                Sessions.Clear();
                if (!File.Exists(_stateFile)) return;
                try
                {
                    var loaded = JsonSerializer.Deserialize<List<PauseSession>>(File.ReadAllText(_stateFile), JsonOptions());
                    if (loaded != null) Sessions.AddRange(loaded.Where(s => s != null && s.Apps != null));
                    foreach (var s in Sessions) s.TriggerNames ??= new List<string>();
                }
                catch (Exception ex) { _log($"Pausierte Apps konnten nicht gelesen werden: {ex.Message}"); }
            }
        }

        public void Save()
        {
            lock (_fileLock)
            {
                try
                {
                    if (Sessions.Count == 0)
                    {
                        if (File.Exists(_stateFile)) File.Delete(_stateFile);
                        return;
                    }
                    string temp = _stateFile + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(Sessions, JsonOptions()));
                    if (File.Exists(_stateFile)) File.Replace(temp, _stateFile, null);
                    else File.Move(temp, _stateFile);
                }
                catch (Exception ex) { _log($"Pausierte Apps konnten nicht gespeichert werden: {ex.Message}"); }
            }
        }

        // ---------- Prozess-Snapshot ----------

        public record ProcInfo(int Pid, int ParentPid, string Name, string? Path, string? CommandLine);

        /// <summary>Alle Prozesse mit Eltern-PID, Pfad und Kommandozeile (über WMI, ohne eigene Prozess-Handles).</summary>
        public static List<ProcInfo> SnapshotProcesses()
        {
            var list = new List<ProcInfo>();
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process");
            using var results = searcher.Get();
            foreach (ManagementObject mo in results)
            {
                using (mo)
                {
                    try
                    {
                        int pid = Convert.ToInt32(mo["ProcessId"]);
                        int parent = Convert.ToInt32(mo["ParentProcessId"]);
                        string name = StripExe(mo["Name"] as string ?? string.Empty);
                        list.Add(new ProcInfo(pid, parent, name, mo["ExecutablePath"] as string, mo["CommandLine"] as string));
                    }
                    catch { }
                }
            }
            return list;
        }

        // ---------- Pausieren ----------

        private class CaptureGroup
        {
            public required ProcInfo Root;
            public required HashSet<int> Pids;
            public required PausedApp App;
        }

        /// <summary>
        /// Merkt sich den Zustand der passenden Apps, speichert ihn und schließt die Apps dann.
        /// Muss auf dem UI-Thread aufgerufen werden (Sessions wird dort verwaltet).
        /// </summary>
        public async Task PauseAsync(string actionId, string presetName, IReadOnlyList<string> triggerNames, IReadOnlyList<string> targets, int restoreDelaySeconds)
        {
            if (targets.Count == 0) { _log("Apps pausieren übersprungen: keine Apps ausgewählt."); return; }

            var groups = await Task.Run(() => Capture(targets, triggerNames));
            if (groups.Count == 0) { _log("Apps pausieren: keine der ausgewählten Apps läuft gerade."); return; }

            var session = Sessions.FirstOrDefault(s => s.ActionId == actionId);
            if (session == null)
            {
                session = new PauseSession { ActionId = actionId, PresetName = presetName, PausedAt = DateTime.Now };
                Sessions.Add(session);
            }
            session.TriggerNames = triggerNames.ToList();
            session.RestoreDelaySeconds = Math.Max(0, restoreDelaySeconds);
            MergeApps(session, groups.Select(g => g.App));

            // Erst speichern, dann schließen: Stürzt EventFlow dazwischen ab, ist der Zustand trotzdem gesichert.
            Save();

            foreach (var g in groups)
                _log($"Pausiere {g.App.ProcessName} ({StateText(g.App.State)}{(string.IsNullOrEmpty(g.App.Arguments) ? "" : ", mit Argumenten")})");

            await Task.Run(() => CloseGroups(groups));

            if (triggerNames.Count == 0)
                _log("Hinweis: Das Profil hat keinen \"Prozess gestartet\"-Trigger. Wiederherstellen über die Aktion \"Pausierte Apps wiederherstellen\" oder das Tray-Menü.");
        }

        private List<CaptureGroup> Capture(IReadOnlyList<string> targets, IReadOnlyList<string> triggerNames)
        {
            var all = SnapshotProcesses();
            var byPid = all.GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First());
            int ownPid = Environment.ProcessId;

            // Prozesse, die zum Trigger gehören (z.B. das Spiel) und alle ihre Vorfahren (Launcher, Steam, Riot Client).
            // Die werden nie geschlossen, sonst beendet man das Spiel gleich mit.
            var triggerPids = all.Where(p => triggerNames.Any(t => ExecutionEngine.IsNameMatch(t, p.Name))).Select(p => p.Pid).ToHashSet();
            var shielded = new HashSet<int>(triggerPids);
            foreach (int pid in triggerPids)
            {
                int cur = pid;
                for (int depth = 0; depth < 32 && byPid.TryGetValue(cur, out var info) && info.ParentPid != 0 && info.ParentPid != cur; depth++)
                {
                    if (!shielded.Add(info.ParentPid)) break;
                    cur = info.ParentPid;
                }
            }

            var matched = new List<ProcInfo>();
            var skippedLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in all)
            {
                if (p.Pid == ownPid || p.Pid <= 4) continue;
                if (!targets.Any(t => TargetMatches(t, p.Name, p.Path))) continue;
                if (IsProtected(p.Name))
                {
                    if (skippedLogged.Add(p.Name)) _log($"{p.Name} ist geschützt (Anti-Cheat/Windows) und wird nicht geschlossen.");
                    continue;
                }
                if (shielded.Contains(p.Pid))
                {
                    if (skippedLogged.Add(p.Name)) _log($"{p.Name} gehört zum Trigger-Programm (startet es oder ist es selbst) und bleibt offen.");
                    continue;
                }
                matched.Add(p);
            }
            if (matched.Count == 0) return new List<CaptureGroup>();

            var matchedPids = matched.Select(p => p.Pid).ToHashSet();
            string Identity(ProcInfo p) => (p.Path ?? p.Name).ToLowerInvariant();

            // Wurzeln: Prozesse, deren Eltern nicht dieselbe App sind (Chrome/Discord haben Dutzende Kindprozesse)
            var roots = matched.Where(p => !(matchedPids.Contains(p.ParentPid) && byPid.TryGetValue(p.ParentPid, out var parent) && Identity(parent) == Identity(p))).ToList();

            var windows = NativeWindows.GetTopLevelWindows();
            var groups = new List<CaptureGroup>();
            foreach (var root in roots)
            {
                // Alle Nachfahren derselben App gehören zur Gruppe
                var pids = new HashSet<int> { root.Pid };
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    foreach (var p in matched)
                        if (!pids.Contains(p.Pid) && pids.Contains(p.ParentPid) && Identity(p) == Identity(root)) { pids.Add(p.Pid); grew = true; }
                }

                var app = new PausedApp
                {
                    ProcessName = root.Name,
                    ExecutablePath = root.Path,
                    Arguments = ExtractArguments(root.CommandLine, root.Path),
                };

                // EnumWindows liefert von oben nach unten: das erste Fenster ist das zuletzt benutzte
                var main = windows.FirstOrDefault(w => pids.Contains(w.Pid) && w.IsAppWindow);
                if (main == null)
                {
                    app.State = PausedWindowState.Tray;
                }
                else
                {
                    app.WindowTitle = main.Title;
                    if (NativeWindows.TryGetPlacement(main.Handle, out var wp))
                    {
                        app.HasPlacement = true;
                        app.PlacementFlags = (int)wp.flags;
                        app.Left = wp.rcNormalPosition.Left; app.Top = wp.rcNormalPosition.Top;
                        app.Right = wp.rcNormalPosition.Right; app.Bottom = wp.rcNormalPosition.Bottom;
                    }
                    app.State = NativeWindows.IsIconic(main.Handle) ? PausedWindowState.Minimized
                              : NativeWindows.IsZoomed(main.Handle) ? PausedWindowState.Maximized
                              : PausedWindowState.Normal;
                }

                if (app.ExecutablePath == null)
                    _log($"Pfad von {root.Name} nicht lesbar (läuft evtl. als Admin). Wird geschlossen, kann aber nur mit EventFlow als Admin wieder gestartet werden.");

                // Doppelte Instanzen mit gleichen Argumenten nur einmal merken
                if (groups.Any(g => Identity(g.Root) == Identity(root) && g.App.Arguments == app.Arguments))
                {
                    groups.First(g => Identity(g.Root) == Identity(root) && g.App.Arguments == app.Arguments).Pids.UnionWith(pids);
                    continue;
                }
                groups.Add(new CaptureGroup { Root = root, Pids = pids, App = app });
            }
            return groups;
        }

        private void CloseGroups(List<CaptureGroup> groups)
        {
            // 1. Sanft: WM_CLOSE an alle Fenster der App (wie "Task beenden" ohne Zwang)
            var windows = NativeWindows.GetTopLevelWindows();
            foreach (var g in groups)
                foreach (var w in windows.Where(w => g.Pids.Contains(w.Pid)))
                    NativeWindows.PostClose(w.Handle);

            // 2. Warten
            var deadline = DateTime.Now + CloseGracePeriod;
            while (DateTime.Now < deadline && groups.Any(g => g.Pids.Any(IsAlive)))
                System.Threading.Thread.Sleep(300);

            // 3. Was noch läuft, wird beendet. Außer es zeigt gerade einen Dialog (z.B. "Speichern?"), dann bleibt es offen.
            windows = NativeWindows.GetTopLevelWindows();
            foreach (var g in groups)
            {
                var alive = g.Pids.Where(IsAlive).ToList();
                if (alive.Count == 0) { _log($"Geschlossen: {g.App.ProcessName}"); continue; }

                if (windows.Any(w => alive.Contains(w.Pid) && w.IsVisible && w.HasOwner))
                {
                    _log($"{g.App.ProcessName} wartet auf eine Rückfrage (z.B. Speichern) und wird nicht erzwungen beendet.");
                    continue;
                }

                foreach (int pid in alive)
                {
                    try
                    {
                        using var p = Process.GetProcessById(pid);
                        p.Kill(entireProcessTree: pid == g.Root.Pid);
                    }
                    catch (ArgumentException) { /* schon weg */ }
                    catch (Exception ex) { _log($"{g.App.ProcessName} konnte nicht beendet werden: {ex.Message}"); }
                }
                _log($"Beendet (reagierte nicht auf Schließen): {g.App.ProcessName}");
            }
        }

        private static bool IsAlive(int pid)
        {
            try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
            catch { return false; }
        }

        // ---------- Wiederherstellen ----------

        /// <summary>Entfernt die Sitzung (UI-Thread) und startet ihre Apps wieder.</summary>
        public async Task RestoreAsync(PauseSession session, bool ignoreDelay = false)
        {
            if (!Sessions.Remove(session)) return;
            Save();

            if (!ignoreDelay && session.RestoreDelaySeconds > 0)
            {
                _log($"Warte {session.RestoreDelaySeconds} Sek., dann werden {session.Apps.Count} App(s) wiederhergestellt...");
                await Task.Delay(TimeSpan.FromSeconds(session.RestoreDelaySeconds));
            }

            _log($"Stelle {session.Apps.Count} pausierte App(s) wieder her ({session.PresetName}).");
            await Task.Run(() => RestoreApps(session.Apps));
        }

        private void RestoreApps(List<PausedApp> apps)
        {
            List<ProcInfo> running;
            try { running = SnapshotProcesses(); }
            catch { running = new List<ProcInfo>(); }

            var placementTasks = new List<Task>();
            foreach (var app in apps)
            {
                try
                {
                    if (string.IsNullOrEmpty(app.ExecutablePath)) { _log($"{app.ProcessName}: Pfad unbekannt, kann nicht neu gestartet werden."); continue; }
                    if (!File.Exists(app.ExecutablePath)) { _log($"{app.ProcessName}: {app.ExecutablePath} gibt es nicht mehr (Update?)."); continue; }

                    // Läuft schon (z.B. selbst gestartet oder hat das Schließen abgelehnt): nicht doppelt starten
                    if (running.Any(p => p.Path != null && string.Equals(p.Path, app.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
                    {
                        _log($"{app.ProcessName} läuft bereits, wird nicht erneut gestartet.");
                        continue;
                    }

                    var psi = new ProcessStartInfo
                    {
                        FileName = app.ExecutablePath,
                        Arguments = BuildRestartArguments(app),
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(app.ExecutablePath) ?? string.Empty,
                        WindowStyle = app.State switch
                        {
                            PausedWindowState.Minimized => ProcessWindowStyle.Minimized,
                            PausedWindowState.Maximized => ProcessWindowStyle.Maximized,
                            PausedWindowState.Tray => ProcessWindowStyle.Hidden,
                            _ => ProcessWindowStyle.Normal,
                        },
                    };
                    var started = DateTime.Now;
                    using (Process.Start(psi)) { }
                    _log($"Wiederhergestellt: {app.ProcessName} ({StateText(app.State)})");

                    placementTasks.Add(Task.Run(() => ApplyWindowState(app, started)));
                    System.Threading.Thread.Sleep(400); // nicht alles im selben Moment starten
                }
                catch (Exception ex) { _log($"{app.ProcessName} konnte nicht gestartet werden: {ex.Message}"); }
            }
            Task.WaitAll(placementTasks.ToArray(), WindowWaitTime + TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Wartet auf das Fenster der neu gestarteten App und setzt Zustand und Position.
        /// Viele Apps zeigen erst einen Splash-Screen: Verschwindet das zuerst gefundene Fenster, wird das nächste genommen.
        /// </summary>
        private void ApplyWindowState(PausedApp app, DateTime started)
        {
            var deadline = started + WindowWaitTime;
            IntPtr applied = IntPtr.Zero;
            var pathCache = new Dictionary<int, string?>();

            while (DateTime.Now < deadline)
            {
                System.Threading.Thread.Sleep(500);
                if (applied != IntPtr.Zero && NativeWindows.IsWindow(applied) && NativeWindows.IsWindowVisible(applied)) continue;

                var window = NativeWindows.GetTopLevelWindows().FirstOrDefault(w =>
                {
                    if (!w.IsAppWindow || w.Handle == applied) return false;
                    if (!pathCache.TryGetValue(w.Pid, out var path)) pathCache[w.Pid] = path = NativeWindows.TryGetProcessPath(w.Pid);
                    return path != null && string.Equals(path, app.ExecutablePath, StringComparison.OrdinalIgnoreCase);
                });
                if (window == null) continue;

                if (app.State == PausedWindowState.Tray)
                {
                    // App hat "unsichtbar starten" ignoriert: minimieren statt verstecken, damit sie erreichbar bleibt
                    NativeWindows.ShowWindow(window.Handle, NativeWindows.SW_SHOWMINNOACTIVE);
                }
                else if (app.HasPlacement)
                {
                    NativeWindows.SetPlacement(window.Handle, app);
                }
                else if (app.State == PausedWindowState.Minimized) NativeWindows.ShowWindow(window.Handle, NativeWindows.SW_SHOWMINNOACTIVE);
                else if (app.State == PausedWindowState.Maximized) NativeWindows.ShowWindow(window.Handle, NativeWindows.SW_SHOWMAXIMIZED);

                applied = window.Handle;
            }
        }

        private static string StateText(PausedWindowState s) => s switch
        {
            PausedWindowState.Minimized => "minimiert",
            PausedWindowState.Maximized => "maximiert",
            PausedWindowState.Tray => "nur Tray/Hintergrund",
            _ => "normales Fenster",
        };
    }

    /// <summary>Win32-Fensterfunktionen. Nur Fenster-APIs, kein Zugriff auf Prozessspeicher.</summary>
    internal static class NativeWindows
    {
        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_SHOWMAXIMIZED = 3;
        public const int SW_SHOWMINNOACTIVE = 7;
        private const uint WM_CLOSE = 0x0010;
        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TOOLWINDOW = 0x00000080L;
        private const uint GW_OWNER = 4;

        public record WindowInfo(IntPtr Handle, int Pid, string Title, bool IsVisible, bool HasOwner, bool IsToolWindow)
        {
            // Ein "richtiges" App-Fenster wie in der Taskleiste
            public bool IsAppWindow => IsVisible && !HasOwner && !IsToolWindow && Title.Length > 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public uint length;
            public uint flags;
            public uint showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public static List<WindowInfo> GetTopLevelWindows()
        {
            var list = new List<WindowInfo>();
            EnumWindows((h, _) =>
            {
                try
                {
                    GetWindowThreadProcessId(h, out uint pid);
                    int len = GetWindowTextLength(h);
                    string title = string.Empty;
                    if (len > 0)
                    {
                        var sb = new StringBuilder(len + 1);
                        GetWindowText(h, sb, sb.Capacity);
                        title = sb.ToString();
                    }
                    long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
                    list.Add(new WindowInfo(h, (int)pid, title, IsWindowVisible(h), GetWindow(h, GW_OWNER) != IntPtr.Zero, (ex & WS_EX_TOOLWINDOW) != 0));
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static bool TryGetPlacement(IntPtr hWnd, out WINDOWPLACEMENT wp)
        {
            wp = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
            return GetWindowPlacement(hWnd, ref wp);
        }

        public static void SetPlacement(IntPtr hWnd, PausedApp app)
        {
            var wp = new WINDOWPLACEMENT
            {
                length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>(),
                flags = (uint)app.PlacementFlags,
                showCmd = app.State switch
                {
                    PausedWindowState.Minimized => SW_SHOWMINNOACTIVE,
                    PausedWindowState.Maximized => SW_SHOWMAXIMIZED,
                    _ => SW_SHOWNORMAL,
                },
                // Liegt das Rechteck außerhalb aller Bildschirme, schiebt Windows es selbst zurück
                rcNormalPosition = new RECT { Left = app.Left, Top = app.Top, Right = app.Right, Bottom = app.Bottom },
            };
            SetWindowPlacement(hWnd, ref wp);
        }

        public static void PostClose(IntPtr hWnd) => PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder exeName, ref int size);

        /// <summary>Pfad eines Prozesses, nur mit PROCESS_QUERY_LIMITED_INFORMATION (wie der Prozess-Picker).</summary>
        public static string? TryGetProcessPath(int pid)
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
    }
}
