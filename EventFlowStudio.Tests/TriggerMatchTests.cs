using EventFlowStudio.Services;
using Xunit;

namespace EventFlowStudio.Tests
{
    public class TriggerMatchTests
    {
        [Theory]
        [InlineData("notepad", "notepad")]
        [InlineData("Notepad", "NOTEPAD")]
        [InlineData("steam", "steamwebhelper")]
        [InlineData("Headset", "Sony WH-1000XM4 Headset")]
        public void Matches_exact_case_insensitive_and_partial_names(string trigger, string actual)
        {
            Assert.True(ExecutionEngine.IsNameMatch(trigger, actual));
        }

        [Theory]
        [InlineData("", "notepad")]
        [InlineData("   ", "notepad")]
        [InlineData("notepad", "")]
        [InlineData("notepad", "calc")]
        [InlineData("steamwebhelper", "steam")]
        public void Does_not_match_empty_or_unrelated_names(string trigger, string actual)
        {
            Assert.False(ExecutionEngine.IsNameMatch(trigger, actual));
        }
    }
}
