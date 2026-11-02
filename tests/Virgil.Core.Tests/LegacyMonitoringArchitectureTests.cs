using System.Text.RegularExpressions;
using Xunit;

namespace Virgil.Tests;

public sealed class LegacyMonitoringArchitectureTests
{
    [Fact]
    public void No_legacy_monitoring_types_in_src()
    {
        var source = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "src");
        // Split names so the repository-wide audit does not match this guard itself.
        var pattern = @"\bMonitoring" + @"Service\b|IMonitoring" + "Service|SystemHealth" + "Snapshot|Drive" + "Status";
        var matches = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => Regex.IsMatch(File.ReadAllText(path), pattern))
            .Select(path => Path.GetRelativePath(source, path))
            .Order(StringComparer.Ordinal).ToArray();

        Assert.Empty(matches);
    }
}
