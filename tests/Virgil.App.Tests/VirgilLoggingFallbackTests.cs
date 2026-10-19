using Virgil.App.Hosting;
using Virgil.Contracts;
using Virgil.Infrastructure.Storage;
using Xunit;

namespace Virgil.Tests;

public sealed class VirgilLoggingFallbackTests
{
    [Fact]
    public void ExistingFileAsLogsRootDoesNotPreventLogging()
    {
        var root = Path.Combine(Path.GetTempPath(), "Virgil-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(root, "existing file");
        try
        {
            using var logger = VirgilLogging.CreateLogger(new VirgilPaths(root, root), new VirgilSettings());
            logger.Information("Le demarrage continue sans fichier de logs");
            Assert.Equal("existing file", File.ReadAllText(root));
        }
        finally { File.Delete(root); }
    }
}
