using EventFlowStudio.Models;
using Xunit;

namespace EventFlowStudio.Tests
{
    public class ObservableModelTests
    {
        [Fact]
        public void PropertyChanged_fires_only_when_the_value_actually_changes()
        {
            var preset = new Preset { Name = "A" };
            var changed = new List<string?>();
            preset.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            preset.Name = "A";
            preset.Name = "B";
            preset.IsActive = true;
            preset.IsActive = false;

            Assert.Equal(new[] { nameof(Preset.Name), nameof(Preset.IsActive) }, changed);
        }

        [Fact]
        public void New_profiles_and_actions_get_unique_ids()
        {
            Assert.NotEqual(new Preset().Id, new Preset().Id);
            Assert.NotEqual(new ActionItem().Id, new ActionItem().Id);
        }
    }
}
