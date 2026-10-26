using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Core.Startup;

public enum StartupApprovalHive { CurrentUser, LocalMachine }
public enum StartupApprovalKind { Run, Run32, StartupFolder }
public sealed record StartupApprovalTarget(StartupApprovalHive Hive, StartupApprovalKind Kind, string ValueName);
public sealed record StartupBackup(StartupApprovalTarget Target, string PreviousValue);
public sealed record StartupChange(StartupApprovalTarget Target, string PreviousValue, string NewValue, string? RestoreBackupId = null);
public sealed record StartupChangeResult(bool Succeeded, string Message, string? BackupId = null);

public interface IStartupApprovalStore
{
    Task<string?> ReadAsync(StartupApprovalTarget target, CancellationToken token);
    Task<string?> SaveVerifiedBackupAsync(StartupBackup backup, CancellationToken token);
    Task<StartupBackup?> ReadBackupAsync(string backupId, CancellationToken token);
    Task<bool> WriteIfUnchangedAsync(StartupApprovalTarget target, string expectedValue, string value, CancellationToken token);
}

public interface IStartupRestorePointService
{
    Task<bool> CreateAndVerifyAsync(CancellationToken token);
}

/// <summary>Only StartupApproved metadata is changed; startup entries are never removed.</summary>
public sealed class StartupChangeService(IStartupApprovalStore store, IStartupRestorePointService restorePoints,
    IConfirmedPlanGate plans)
{
    private readonly SemaphoreSlim _execution = new(1, 1);

    public async Task<StartupChange?> PreviewAsync(StartupApprovalTarget target, bool enabled, CancellationToken token = default)
    {
        if (!IsSupported(target)) return null;
        var previous = await store.ReadAsync(target, token);
        if (!IsValidValue(previous)) return null;
        var bytes = Convert.FromHexString(previous!);
        bytes[0] = enabled ? (byte)2 : (byte)3;
        Array.Clear(bytes, 1, bytes.Length - 1);
        return new(target, previous!, Convert.ToHexString(bytes));
    }

    public async Task<StartupChange?> PreviewRestoreAsync(string backupId, CancellationToken token = default)
    {
        var backup = await store.ReadBackupAsync(backupId, token);
        if (backup is null || !IsSupported(backup.Target) || !IsValidValue(backup.PreviousValue)) return null;
        var current = await store.ReadAsync(backup.Target, token);
        return IsValidValue(current) ? new(backup.Target, current!, backup.PreviousValue, backupId) : null;
    }

    public static ActionPreview CreatePreview(StartupChange change) => new(
        change.RestoreBackupId is null ? "startup.set-approved" : "startup.restore-approved",
        change.RestoreBackupId is null ? "Modifier le démarrage" : "Restaurer le démarrage",
        RiskLevel.Sensitive,
        new[] { change.Target.ValueName },
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hive"] = change.Target.Hive.ToString(), ["kind"] = change.Target.Kind.ToString(),
            ["previous"] = change.PreviousValue, ["new"] = change.NewValue,
            ["backup"] = change.RestoreBackupId ?? string.Empty
        },
        new[] { "Modifier uniquement l’autorisation de démarrage. Conserver l’entrée et sa commande." },
        true, false, Reversibility.Full,
        new[] { "Sauvegarde vérifiée de la valeur précédente", "Point de restauration créé et vérifié" });

    public async Task<StartupChangeResult> ExecuteAsync(StartupChange change, ConfirmedPlan? plan, CancellationToken token = default)
    {
        await _execution.WaitAsync(token);
        try
        {
            if (!IsSupported(change.Target) || !IsValidValue(change.PreviousValue) || !IsValidValue(change.NewValue))
                return new(false, "Cette entrée ne peut pas être modifiée ici.");
            var preview = CreatePreview(change);
            var validation = plans.Validate(plan, preview);
            if (!validation.IsAccepted) return new(false, validation.Message);
            if (await store.ReadAsync(change.Target, token) != change.PreviousValue)
                return new(false, "L’état a changé. Actualisez et confirmez un nouvel aperçu.");
            if (change.RestoreBackupId is not null)
            {
                var original = await store.ReadBackupAsync(change.RestoreBackupId, token);
                if (original is null || original.Target != change.Target || original.PreviousValue != change.NewValue)
                    return new(false, "La sauvegarde de restauration est absente ou différente.");
            }
            // Consume before any backup, restore-point creation or registry write.
            validation = plans.TryConsume(plan, preview);
            if (!validation.IsAccepted) return new(false, validation.Message);
            var backupId = await store.SaveVerifiedBackupAsync(new(change.Target, change.PreviousValue), token);
            if (backupId is null) return new(false, "Sauvegarde non vérifiée : aucune modification du démarrage.");
            // The elevated machine writer creates and verifies its own restore point.
            if (change.Target.Hive == StartupApprovalHive.CurrentUser && !await restorePoints.CreateAndVerifyAsync(token))
                return new(false, "Point de restauration indisponible : aucune modification du démarrage. Activez la protection du système puis réessayez.");
            if (!await store.WriteIfUnchangedAsync(change.Target, change.PreviousValue, change.NewValue, token))
                return new(false, "L’état a changé ou l’écriture a échoué : actualisez le démarrage.", backupId);
            return new(true, "Autorisation de démarrage mise à jour. La sauvegarde permet de restaurer l’état précédent.", backupId);
        }
        finally { _execution.Release(); }
    }

    public static bool IsSupported(StartupApprovalTarget target) => Enum.IsDefined(target.Hive) &&
        Enum.IsDefined(target.Kind) && !string.IsNullOrWhiteSpace(target.ValueName) &&
        target.ValueName.Length <= 255 && target.ValueName.All(c => !char.IsControl(c) && c is not '\\' and not '/');

    private static bool IsValidValue(string? value) => value is { Length: 24 } &&
        value.All(Uri.IsHexDigit) && Convert.FromHexString(value)[0] is 2 or 3 or 6 or 7;
}
