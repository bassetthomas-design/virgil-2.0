using System.Globalization;
using System.Text.Json;
using Virgil.Contracts;

namespace Virgil.ElevatedHelper;

internal static class UpdateRestorePoint
{
    private sealed record Point(int SequenceNumber, string Description, DateTimeOffset CreationTime);
    internal const string ReadScript = """
        $ErrorActionPreference='Stop'
        $points=@(Get-ComputerRestorePoint | ForEach-Object {
          [pscustomobject]@{SequenceNumber=[int]$_.SequenceNumber;Description=$_.Description;CreationTime=([Management.ManagementDateTimeConverter]::ToDateTime($_.CreationTime).ToUniversalTime().ToString('o'))}
        })
        ConvertTo-Json -InputObject $points -Compress
        """;

    internal static ElevatedCommandSpec PowerShell(string script) => new("WindowsPowerShell\\v1.0\\powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script]) { NonInterruptible = true };

    internal static async Task<(bool Success, string Output)> EnsureAsync(ElevatedInterventionRequest request,
        IElevatedCommandRunner commands, CancellationToken token, IProgress<InterventionProgress>? progress, TimeProvider timeProvider)
    {
        if (!Guid.TryParseExact(request.RestoreSessionId, "D", out _)) return (false, "Session de restauration invalide.");
        if (request.ActionId == InterventionId.InstallMicrosoftUpdates && request.RestorePointSequenceNumber is not > 0)
            return (false, "Numéro de séquence du point de restauration invalide. Installation bloquée.");
        progress?.Report(new() { Step = "Vérification du point de restauration" });
        token.ThrowIfCancellationRequested();
        var points = await ReadAsync(commands).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (points is null) return (false, Failure);
        var now = timeProvider.GetUtcNow();
        bool Recent(Point point, int hours) => point.SequenceNumber > 0 && point.CreationTime <= now && point.CreationTime >= now.AddHours(-hours);
        if (request.ActionId == InterventionId.InstallMicrosoftUpdates)
        {
            var exact = points.FirstOrDefault(p => p.SequenceNumber == request.RestorePointSequenceNumber && Recent(p, 24));
            return exact is null ? (false, Failure) : (true, Selected(exact));
        }
        var description = "Virgil mises à jour " + request.RestoreSessionId;
        var session = points.Where(p => p.Description == description && Recent(p, 2)).MaxBy(p => p.SequenceNumber);
        if (session is not null) return (true, Selected(session));
        // Reuse is permitted only after a failed/no-effect creation, and is always reported.
        progress?.Report(new() { Step = "Création du point de restauration" });
        token.ThrowIfCancellationRequested();
        await commands.RunAsync(PowerShell("$ErrorActionPreference='Stop'; Checkpoint-Computer -Description '" + description + "' -RestorePointType MODIFY_SETTINGS")).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var after = await ReadAsync(commands).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (after is null) return (false, Failure);
        session = after.Where(p => p.Description == description && Recent(p, 2) && !points.Any(old => old.SequenceNumber == p.SequenceNumber)).MaxBy(p => p.SequenceNumber);
        if (session is not null) return (true, Selected(session));
        // Checkpoint-Computer may return zero without creating anything (24-hour limit).
        var existing = after.Where(p => Recent(p, 24)).MaxBy(p => p.CreationTime);
        if (existing is null) return (false, Failure);
        return (true, Selected(existing) + "\nVIRGIL|restore-point|INFO|point existant du " +
            existing.CreationTime.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("fr-FR")) +
            " (" + Safe(existing.Description) + ") réutilisé");
    }
    private static string Safe(string value) => value.Replace('|', ' ').Replace('\r', ' ').Replace('\n', ' ');
    private static string Selected(Point point) => "VIRGIL|restore-point|OK|" + point.SequenceNumber.ToString(CultureInfo.InvariantCulture);
    private const string Failure = "Point de restauration non vérifié. Activez la protection du système puis réessayez. Installation bloquée.";
    private static async Task<Point[]?> ReadAsync(IElevatedCommandRunner commands)
    {
        var result = await commands.RunAsync(PowerShell(ReadScript)).ConfigureAwait(false);
        if (result.ExitCode != 0) return null;
        try { return JsonSerializer.Deserialize<Point[]>(result.StandardOutput); }
        catch (JsonException) { return null; }
    }
}
