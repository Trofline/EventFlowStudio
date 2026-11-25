using System.IO;
using System.Text.Json;
using EventFlowStudio.Models;
using EventFlowStudio.Services;
using Xunit;

namespace EventFlowStudio.Tests
{
    public sealed class PresetPersistenceTests : IDisposable
    {
        private readonly string _folder;
        private readonly List<string> _log = new();

        public PresetPersistenceTests()
        {
            _folder = Path.Combine(Path.GetTempPath(), "EventFlowStudioTests", Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }

        private ExecutionEngine CreateEngine()
        {
            var engine = new ExecutionEngine(_folder);
            engine.LogMessage += _log.Add;
            return engine;
        }

        private string ConfigFile => Path.Combine(_folder, "presets_config_v4.json");

        [Fact]
        public void Creates_and_saves_a_default_profile_when_no_config_exists()
        {
            var engine = CreateEngine();

            engine.LoadPresets();

            var preset = Assert.Single(engine.Presets);
            Assert.Equal("Standard Profil", preset.Name);
            Assert.Single(preset.Triggers);
            Assert.True(File.Exists(ConfigFile));
        }

        [Fact]
        public void Saved_profiles_load_back_unchanged()
        {
            var engine = CreateEngine();
            var preset = new Preset { Name = "Gaming", Description = "Discord mit Spiel starten", IsActive = false };
            preset.Triggers.Add(new TriggerCondition { Type = TriggerType.ProcessStarted, TargetName = "game" });
            preset.Triggers.Add(new TriggerCondition { Type = TriggerType.GlobalHotkey, HotkeyModifier = 3, HotkeyVK = 0x47 });
            preset.Actions.Add(new ActionItem
            {
                Type = ActionType.StartProcess,
                TargetPath = @"C:\Apps\Discord.exe",
                Arguments = "--minimized",
                DelaySeconds = 5,
                LaunchState = WindowLaunchState.Minimized,
                TerminateOnTriggerProcessExit = true,
                ExitDelaySeconds = 10,
            });
            engine.Presets.Add(preset);
            engine.SavePresets();

            var reloaded = CreateEngine();
            reloaded.LoadPresets();

            var loaded = Assert.Single(reloaded.Presets);
            Assert.Equal(preset.Id, loaded.Id);
            Assert.Equal("Gaming", loaded.Name);
            Assert.Equal("Discord mit Spiel starten", loaded.Description);
            Assert.False(loaded.IsActive);
            Assert.Collection(loaded.Triggers,
                t => { Assert.Equal(TriggerType.ProcessStarted, t.Type); Assert.Equal("game", t.TargetName); },
                t => { Assert.Equal(TriggerType.GlobalHotkey, t.Type); Assert.Equal(3u, t.HotkeyModifier); Assert.Equal(0x47u, t.HotkeyVK); });
            var action = Assert.Single(loaded.Actions);
            Assert.Equal(ActionType.StartProcess, action.Type);
            Assert.Equal(@"C:\Apps\Discord.exe", action.TargetPath);
            Assert.Equal("--minimized", action.Arguments);
            Assert.Equal(5, action.DelaySeconds);
            Assert.Equal(WindowLaunchState.Minimized, action.LaunchState);
            Assert.True(action.TerminateOnTriggerProcessExit);
            Assert.Equal(10, action.ExitDelaySeconds);
        }

        [Fact]
        public void Enums_are_saved_as_names_and_runtime_process_ids_are_not_saved()
        {
            var engine = CreateEngine();
            var preset = new Preset();
            preset.Triggers.Add(new TriggerCondition { Type = TriggerType.UsbDeviceConnected });
            preset.Actions.Add(new ActionItem { Type = ActionType.KillProcess, LaunchedProcessId = 1234 });
            engine.Presets.Add(preset);

            engine.SavePresets();

            string json = File.ReadAllText(ConfigFile);
            Assert.Contains("\"UsbDeviceConnected\"", json);
            Assert.Contains("\"KillProcess\"", json);
            Assert.DoesNotContain("LaunchedProcessId", json);
        }

        [Fact]
        public void A_profile_without_triggers_gets_a_default_trigger()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(ConfigFile, """[ { "Name": "Leer", "Triggers": [], "Actions": [] } ]""");
            var engine = CreateEngine();

            engine.LoadPresets();

            var preset = Assert.Single(engine.Presets);
            Assert.Equal("Leer", preset.Name);
            var trigger = Assert.Single(preset.Triggers);
            Assert.Equal(TriggerType.ProcessStarted, trigger.Type);
        }

        [Fact]
        public void An_unreadable_config_is_logged_and_backed_up_before_defaults_are_written()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(ConfigFile, "{ das ist kein JSON");
            var engine = CreateEngine();

            engine.LoadPresets();

            Assert.Contains(_log, m => m.StartsWith("Fehler beim Laden der Profile"));
            var backup = Assert.Single(Directory.GetFiles(_folder, "presets_config_v4_unreadable_*.json"));
            Assert.Equal("{ das ist kein JSON", File.ReadAllText(backup));
            Assert.Equal("Standard Profil", Assert.Single(engine.Presets).Name);
        }

        [Fact]
        public void Imported_profiles_get_new_ids_and_are_added_to_existing_ones()
        {
            var source = CreateEngine();
            var original = new Preset { Name = "Import" };
            original.Triggers.Add(new TriggerCondition());
            source.Presets.Add(original);
            string exportFile = Path.Combine(_folder, "export.json");
            source.ExportPresets(exportFile);

            var target = CreateEngine();
            target.LoadPresets();
            target.ImportPresets(exportFile);

            Assert.Equal(2, target.Presets.Count);
            var imported = target.Presets.Last();
            Assert.Equal("Import", imported.Name);
            Assert.NotEqual(original.Id, imported.Id);
        }

        [Fact]
        public void Settings_load_from_file_and_fall_back_to_defaults_when_unreadable()
        {
            Directory.CreateDirectory(_folder);
            string settingsFile = Path.Combine(_folder, "app_settings.json");
            File.WriteAllText(settingsFile, """{ "Language": "English", "AutoUpdate": false }""");

            var engine = CreateEngine();
            engine.LoadSettings();
            Assert.Equal("English", engine.Settings.Language);
            Assert.False(engine.Settings.AutoUpdate);

            File.WriteAllText(settingsFile, "kaputt");
            var broken = CreateEngine();
            broken.LoadSettings();
            Assert.Equal("Deutsch", broken.Settings.Language);
            Assert.Contains(_log, m => m.StartsWith("Fehler beim Laden der Einstellungen"));
        }

        [Fact]
        public void Sample_config_in_the_repo_uses_the_triggers_format()
        {
            string sample = Path.Combine(AppContext.BaseDirectory, "TestData", "presets_config_v4.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(sample));
            foreach (var preset in doc.RootElement.EnumerateArray())
            {
                Assert.False(preset.TryGetProperty("Trigger", out _), "Altes Feld 'Trigger' gefunden, erwartet wird 'Triggers'.");
                Assert.Equal(JsonValueKind.Array, preset.GetProperty("Triggers").ValueKind);
            }

            Directory.CreateDirectory(_folder);
            File.Copy(sample, ConfigFile);
            var engine = CreateEngine();
            engine.LoadPresets();
            Assert.Equal("70573c21-bbd6-45e6-a8aa-06cbd7947574", Assert.Single(engine.Presets).Id);
        }

        [Fact]
        public void Toggling_a_profile_saves_only_its_active_state()
        {
            var engine = CreateEngine();
            engine.LoadPresets();
            var preset = Assert.Single(engine.Presets);
            Assert.True(preset.IsActive);

            // Ungespeicherte Bearbeitung, die nicht mit auf die Platte darf
            preset.Name = "Noch nicht gespeichert";
            preset.IsActive = false;

            Assert.True(engine.SavePresetActiveState(preset));

            var reloaded = CreateEngine();
            reloaded.LoadPresets();
            var loaded = Assert.Single(reloaded.Presets);
            Assert.False(loaded.IsActive);
            Assert.Equal("Standard Profil", loaded.Name);
        }

        [Fact]
        public void Toggling_a_never_saved_profile_reports_false()
        {
            var engine = CreateEngine();
            engine.LoadPresets();
            var unsaved = new Preset { Name = "Neu" };
            engine.Presets.Add(unsaved);

            unsaved.IsActive = false;

            Assert.False(engine.SavePresetActiveState(unsaved));
        }
    }
}
