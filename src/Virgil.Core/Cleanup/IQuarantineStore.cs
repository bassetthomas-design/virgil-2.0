using Virgil.Contracts;

namespace Virgil.Core.Cleanup;

public sealed record QuarantineEntry(Guid Id, string OriginalPath, CleanupZoneDefinition Zone,
    long SizeBytes, string Sha256, DateTimeOffset QuarantinedAtUtc, DateTimeOffset ExpiresAtUtc);

/// <summary>Stockage Windows : aucun repli vers une suppression en cas d'échec de déplacement.</summary>
public interface IQuarantineStore
{
    IReadOnlyList<QuarantineEntry> List();
    QuarantineEntry MoveToQuarantine(string path, CleanupZoneDefinition zone, DateTimeOffset now, int retentionDays);
    string? ValidateRestore(QuarantineEntry entry);
    void Restore(QuarantineEntry entry);
    void Purge(QuarantineEntry entry);
}
