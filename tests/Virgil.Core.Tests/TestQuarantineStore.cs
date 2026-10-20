using Virgil.Contracts;
using Virgil.Core.Cleanup;
using Virgil.Infrastructure.Windows;

namespace Virgil.Tests;

/// <summary>Quarantaine réelle confinée au bac à sable de chaque ancien test disque.</summary>
internal sealed class TestQuarantineStore : IQuarantineStore
{
    public QuarantineEntry MoveToQuarantine(string path, CleanupZoneDefinition zone, DateTimeOffset now, int retentionDays) =>
        new QuarantineStore(Path.Combine(zone.RootPath, ".quarantine")).MoveToQuarantine(path, zone, now, retentionDays);
    public IReadOnlyList<QuarantineEntry> List() => throw new NotSupportedException();
    public string? ValidateRestore(QuarantineEntry entry) => throw new NotSupportedException();
    public void Restore(QuarantineEntry entry) => throw new NotSupportedException();
    public void Purge(QuarantineEntry entry) => throw new NotSupportedException();
}
