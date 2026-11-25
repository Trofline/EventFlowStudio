using System.IO;
using EventFlowStudio.Services;
using Xunit;

namespace EventFlowStudio.Tests
{
    public class AppPauseTests
    {
        [Fact]
        public void ParseTargets_SplitsTrimsAndDeduplicates()
        {
            var list = AppPauseService.ParseTargets(" discord ; \"C:\\Apps\\Spotify.exe\"|DISCORD\nsteam.exe; ");
            Assert.Equal(new[] { "discord", "C:\\Apps\\Spotify.exe", "steam.exe" }, list);
            Assert.Empty(AppPauseService.ParseTargets("  "));
        }

        [Theory]
        [InlineData("discord", "Discord", null, true)]
        [InlineData("discord.exe", "Discord", null, true)]
        [InlineData("disc", "Discord", null, false)] // kein Teilstring: sonst würde zu viel geschlossen
        [InlineData("C:\\Apps\\Spotify.exe", "Spotify", "c:\\apps\\spotify.exe", true)]
        [InlineData("C:\\Apps\\Spotify.exe", "Spotify", "D:\\Other\\Spotify.exe", false)]
        [InlineData("C:\\Apps\\Spotify.exe", "Spotify", null, true)] // Pfad nicht lesbar: Name reicht
        [InlineData("", "Discord", null, false)]
        public void TargetMatches_IsExact(string target, string name, string? path, bool expected)
        {
            Assert.Equal(expected, AppPauseService.TargetMatches(target, name, path));
        }

        [Theory]
        [InlineData("vgc")]
        [InlineData("VGTRAY.exe")]
        [InlineData("EasyAntiCheat_EOS")]
        [InlineData("BEService")]
        [InlineData("explorer")]
        [InlineData("RiotClientServices")]
        public void ProtectedProcesses_AreNeverPaused(string name) => Assert.True(AppPauseService.IsProtected(name));

        [Fact]
        public void NormalApps_AreNotProtected() => Assert.False(AppPauseService.IsProtected("Discord"));

        [Theory]
        [InlineData("\"C:\\Program Files\\App\\app.exe\" --flag \"x y\"", "C:\\Program Files\\App\\app.exe", "--flag \"x y\"")]
        [InlineData("C:\\Tools\\tool.exe -silent", "C:\\Tools\\tool.exe", "-silent")]
        [InlineData("\"C:\\a.exe\"", "C:\\a.exe", "")]
        [InlineData("tool.exe   a  b", null, "a  b")]
        [InlineData(null, null, "")]
        public void ExtractArguments_StripsProgram(string? cmd, string? exe, string expected)
        {
            Assert.Equal(expected, AppPauseService.ExtractArguments(cmd, exe));
        }

        [Fact]
        public void TrayApps_GetKnownTrayArgument_Once()
        {
            var steam = new PausedApp { ProcessName = "steam", State = PausedWindowState.Tray };
            Assert.Equal("-silent", AppPauseService.BuildRestartArguments(steam));
            steam.Arguments = "-silent";
            Assert.Equal("-silent", AppPauseService.BuildRestartArguments(steam));

            var window = new PausedApp { ProcessName = "steam", State = PausedWindowState.Normal, Arguments = "-foo" };
            Assert.Equal("-foo", AppPauseService.BuildRestartArguments(window));
        }

        [Fact]
        public void Session_FinishesOnlyWhenNoTriggerProcessRuns()
        {
            var s = new PauseSession { TriggerNames = { "valorant" } };
            Assert.False(AppPauseService.IsSessionFinished(s, new[] { "explorer", "VALORANT-Win64-Shipping" }));
            Assert.True(AppPauseService.IsSessionFinished(s, new[] { "explorer", "Discord" }));

            // Ohne Prozess-Trigger nie automatisch
            Assert.False(AppPauseService.IsSessionFinished(new PauseSession(), new[] { "explorer" }));
        }

        [Fact]
        public void Merge_KeepsFirstCapturedState()
        {
            var s = new PauseSession();
            s.Apps.Add(new PausedApp { ProcessName = "Discord", ExecutablePath = "C:\\d.exe", State = PausedWindowState.Maximized });
            int added = AppPauseService.MergeApps(s, new[]
            {
                new PausedApp { ProcessName = "Discord", ExecutablePath = "c:\\D.exe", State = PausedWindowState.Tray },
                new PausedApp { ProcessName = "Spotify", ExecutablePath = "C:\\s.exe" },
            });
            Assert.Equal(1, added);
            Assert.Equal(2, s.Apps.Count);
            Assert.Equal(PausedWindowState.Maximized, s.Apps[0].State);
        }

        [Fact]
        public void Sessions_SurviveRestart()
        {
            string dir = Path.Combine(Path.GetTempPath(), "efs_pause_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var a = new AppPauseService(dir, _ => { });
                var session = new PauseSession { ActionId = "x", PresetName = "Gaming", TriggerNames = { "game" }, RestoreDelaySeconds = 3 };
                session.Apps.Add(new PausedApp { ProcessName = "Discord", ExecutablePath = "C:\\d.exe", Arguments = "--x", State = PausedWindowState.Minimized, HasPlacement = true, Left = -10, Top = 20, Right = 800, Bottom = 600 });
                a.Sessions.Add(session);
                a.Save();

                var b = new AppPauseService(dir, _ => { });
                b.Load();
                var loaded = Assert.Single(b.Sessions);
                Assert.Equal("Gaming", loaded.PresetName);
                Assert.Equal(new[] { "game" }, loaded.TriggerNames);
                var app = Assert.Single(loaded.Apps);
                Assert.Equal(PausedWindowState.Minimized, app.State);
                Assert.Equal(-10, app.Left);
                Assert.Equal("--x", app.Arguments);

                // Leere Liste löscht die Datei
                b.Sessions.Clear();
                b.Save();
                Assert.False(File.Exists(Path.Combine(dir, "paused_apps.json")));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
