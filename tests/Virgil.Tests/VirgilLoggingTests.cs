using Virgil.App.Hosting;
using Virgil.Contracts;
using Virgil.Infrastructure.Storage;
using Xunit;

namespace Virgil.Tests;

public sealed class VirgilLoggingTests
{
    [Fact]
    public void FlushCreatesRollingLogInLogsDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "VirgilTests", Guid.NewGuid().ToString("N"));
        var paths = new VirgilPaths(root, root);
        try
        {
            using (var logger = VirgilLogging.CreateLogger(paths, new VirgilSettings()))
                logger.ForContext("SourceContext", "LoggingTest").Information("Test de journalisation");
            var log = Assert.Single(Directory.GetFiles(paths.LogsDirectory, "virgil-*.log"));
            var text = File.ReadAllText(log);
            Assert.Contains("Test de journalisation", text);
            Assert.Contains("LoggingTest", text);
            Assert.False(File.Exists(paths.SettingsFile));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
