using Virgil.Core.Startup;
using Virgil.Infrastructure.Windows.Startup;
using Xunit;

namespace Virgil.Tests;

public sealed class StartupBackupTests
{
    [Fact]
    public async Task Backup_roundtrip_preserves_target_and_binary_value_and_rejects_tampering()
    {
        var directory = Path.Combine(Path.GetTempPath(), "virgil-startup-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WindowsStartupApprovalStore(directory);
            var backup = new StartupBackup(new(StartupApprovalHive.CurrentUser, StartupApprovalKind.Run32, "Exemple"),
                "03000000FEDCBA9876543210");
            var id = await store.SaveVerifiedBackupAsync(backup, CancellationToken.None);
            Assert.NotNull(id);
            Assert.Equal(backup, await store.ReadBackupAsync(id, CancellationToken.None));
            var path = Path.Combine(directory, id + ".json");
            var contents = await File.ReadAllTextAsync(path);
            await File.WriteAllTextAsync(path, contents.Replace("FEDCBA9876543210", "0000000000000000", StringComparison.Ordinal));
            Assert.Null(await store.ReadBackupAsync(id, CancellationToken.None));
            Assert.Null(await store.ReadBackupAsync("../autre", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
