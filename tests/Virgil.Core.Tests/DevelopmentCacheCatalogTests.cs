using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

public sealed class DevelopmentCacheCatalogTests
{
    [Theory]
    [InlineData("npm-cache")]
    [InlineData("NuGet")]
    [InlineData("v3-cache")]
    [InlineData("plugins-cache")]
    [InlineData("pnpm-cache")]
    [InlineData("pnpm")]
    [InlineData("NUGET")]
    public void Development_cache_is_omitted_even_with_orphan_registry_evidence(string name)
    {
        var now = DateTimeOffset.UtcNow;
        var root = Path.GetFullPath("catalog-sandbox");
        var path = Path.Combine(root, name);
        var candidate = new SoftwareFootprintCandidate(name, path, root,
            [new(Path.Combine(path, "cache.tmp"), 10, now)], [@"HKCU\SOFTWARE\" + name], now);
        Assert.Empty(new LeftoverAnalyzer().Analyze(new([], [], [candidate]), now));
        Assert.False(DevelopmentCacheCatalog.Contains("OldTool"));
    }
}
