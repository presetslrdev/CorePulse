using System.Reflection;
using CpuMonitorNotifier.Localization;
using Xunit;

namespace CorePulse.Tests;

public class LocalizationTests
{
    private static IEnumerable<(AppLanguage Lang, Dictionary<string, string> Table)> Tables()
    {
        var field = typeof(Loc).GetField("Tables", BindingFlags.NonPublic | BindingFlags.Static)!;
        var tables = (Dictionary<AppLanguage, Dictionary<string, string>>)field.GetValue(null)!;
        return tables.Select(kv => (kv.Key, kv.Value));
    }

    private static Dictionary<string, string> English()
    {
        var field = typeof(Loc).GetField("English", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Dictionary<string, string>)field.GetValue(null)!;
    }

    [Fact]
    public void EveryLanguageHasEveryEnglishKey()
    {
        var expected = English().Keys.OrderBy(k => k).ToArray();
        foreach (var (lang, table) in Tables())
        {
            string[] missing = expected.Except(table.Keys).OrderBy(k => k).ToArray();
            Assert.True(missing.Length == 0, $"{lang} is missing: {string.Join(", ", missing)}");
        }
    }

    [Theory]
    [InlineData("menu.checkUpdates")]
    [InlineData("settings.updates")]
    [InlineData("update.available")]
    [InlineData("update.available.body")]
    [InlineData("update.button.update")]
    [InlineData("update.button.notes")]
    [InlineData("update.downloading")]
    [InlineData("update.upToDate")]
    [InlineData("update.failed")]
    public void UpdateKeysExistInEnglish(string key) => Assert.Contains(key, English().Keys);
}
