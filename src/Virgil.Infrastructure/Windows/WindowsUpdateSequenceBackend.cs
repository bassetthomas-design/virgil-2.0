using Virgil.Contracts;
using Virgil.Core.Interventions;
using Virgil.Core.Processes;
using Virgil.Core.Updates;

namespace Virgil.Infrastructure.Windows;

public sealed class WindowsUpdateSequenceBackend(
    IProcessRunner runner,
    ElevatedHelperRequestStore store,
    IElevatedProcessLauncher launcher,
    Func<string> helperPath,
    WingetAvailabilityService? wingetAvailability = null) : IUpdateSequenceBackend
{
    private int? _restorePointSequenceNumber;
    private bool _prepared;
    private bool _sourceUpdated;
    private string _restoreSessionId = string.Empty;
    public async Task<UpdateSourceVerification> VerifyAsync(UpdateComponent component, CancellationToken token)
    {
        if (component == UpdateComponent.Store)
            return new(component, "bloquée", "Recherche Store lancée — reste possible hors WinGet ; installation UWP non prouvée.");
        // Scripts fermés, en lecture seule, sans élévation ni commande d'installation.
        var query = component switch
        {
            UpdateComponent.Defender => "$s=Get-MpComputerStatus -ErrorAction Stop; if ($null -eq $s.AntivirusSignatureLastUpdated -or $null -eq $s.AntivirusSignatureAge) { throw 'État absent' }; if ($s.AntivirusEnabled -and -not $s.DefenderSignaturesOutOfDate -and $s.AntivirusSignatureAge -eq 0) { 'VIRGIL_CURRENT' } else { 'VIRGIL_REMAINING' }",
            UpdateComponent.MicrosoftUpdate => "$m=New-Object -ComObject Microsoft.Update.ServiceManager; if (@($m.Services | Where-Object { $_.ServiceID -eq '7971f918-a847-4430-9279-4a52d1efe18d' }).Count -gt 0) { 'VIRGIL_CURRENT' } else { 'VIRGIL_REMAINING' }",
            UpdateComponent.Windows or UpdateComponent.WindowsFeatureUpgrade => "$s=New-Object -ComObject Microsoft.Update.Session; $q=$s.CreateUpdateSearcher(); $m=New-Object -ComObject Microsoft.Update.ServiceManager; if (@($m.Services | Where-Object { $_.ServiceID -eq '7971f918-a847-4430-9279-4a52d1efe18d' }).Count -gt 0) { $q.ServerSelection=3; $q.ServiceID='7971f918-a847-4430-9279-4a52d1efe18d' }; $r=$q.Search(\"IsInstalled=0 and IsHidden=0 and Type='Software'\"); if ($r.ResultCode -ne 2) { throw 'VIRGIL_WUA_001' }; if ($r.Updates.Count -eq 0) { 'VIRGIL_CURRENT' } else { 'VIRGIL_REMAINING' }",
            UpdateComponent.Driver => "$s=New-Object -ComObject Microsoft.Update.Session; $q=$s.CreateUpdateSearcher(); $m=New-Object -ComObject Microsoft.Update.ServiceManager; if (@($m.Services | Where-Object { $_.ServiceID -eq '7971f918-a847-4430-9279-4a52d1efe18d' }).Count -gt 0) { $q.ServerSelection=3; $q.ServiceID='7971f918-a847-4430-9279-4a52d1efe18d' }; $r=$q.Search(\"IsInstalled=0 and IsHidden=0 and Type='Driver'\"); if ($r.ResultCode -ne 2) { throw 'VIRGIL_WUA_001' }; if ($r.Updates.Count -eq 0) { 'VIRGIL_CURRENT' } else { 'VIRGIL_REMAINING' }",
            _ => null
        };
        if (query is null) return new(component, "indisponible", "Contrôle final non disponible.");
        var result = await runner.RunAsync(new("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command",
            "$ErrorActionPreference='Stop'; " + query], TimeSpan.FromSeconds(40)), token).ConfigureAwait(false);
        if (result.StandardError.Contains("VIRGIL_WUA_001", StringComparison.Ordinal)
            || result.StandardOutput.Contains("VIRGIL_WUA_001", StringComparison.Ordinal))
            return new(component, "indisponible", "Recherche finale incomplète ; état de la source non prouvé.");
        if (result.ExitCode != 0 || result.TimedOut || result.Cancelled || result.LaunchError is not null)
            return new(component, "indisponible", "Lecture de la source impossible ou délai dépassé.");
        return result.StandardOutput.Trim() switch
        {
            "VIRGIL_CURRENT" => new(component, "à jour", "Contrôle final : aucun reste détecté pour cette source."),
            "VIRGIL_REMAINING" => new(component, "bloquée", "Contrôle final : reste détecté pour cette source."),
            _ => new(component, "indisponible", "Contrôle final sans résultat exploitable.")
        };
    }
    public void BeginExecution(string restoreSessionId)
    {
        _restoreSessionId = restoreSessionId;
        _restorePointSequenceNumber = null;
        _prepared = false;
        _sourceUpdated = false;
    }

    public Task<IReadOnlyList<string>> DetectLaunchersAsync(CancellationToken cancellationToken) => Task.Run(async () =>
    {
        var inventory = await new Virgil.Core.Applications.ApplicationRegistryReader().ReadAsync(cancellationToken).ConfigureAwait(false);
        return LauncherCatalog.Detect(inventory.Applications.Select(app => app.DisplayName));
    }, cancellationToken);

    public static bool IsSafePackageId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 256 &&
        char.IsAsciiLetterOrDigit(id[0]) && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+' or '\\' or '{' or '}');
    public static bool IsSafeVersion(string version) => !string.IsNullOrWhiteSpace(version) && version.Length <= 128 &&
        char.IsAsciiLetterOrDigit(version[0]) && version.Any(char.IsAsciiDigit) &&
        version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '+' or '~');

    public async Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken cancellationToken)
    {
        if (step.Component == UpdateComponent.Launcher)
            return new(step, false, false, "À faire dans le lanceur");
        if (step.Component is UpdateComponent.Winget or UpdateComponent.WingetFinal)
        {
            var app = step.Application;
            if (step.Component == UpdateComponent.Winget && (app is null || string.IsNullOrWhiteSpace(app.Id) || app.IsIdTruncated || !IsSafePackageId(app.Id) || !IsSafeVersion(app.AvailableVersion)))
                return new(step, false, false, "Identité ou version WinGet incomplète. Relancez l’analyse.");
            var detection = await (wingetAvailability ?? new WingetAvailabilityService(runner)).DetectAsync(cancellationToken).ConfigureAwait(false);
            if (!detection.IsAvailable || string.IsNullOrWhiteSpace(detection.ExecutablePath)) return new(step, false, false, "WinGet indisponible.");
            cancellationToken.ThrowIfCancellationRequested();
            var capabilities = WingetAvailabilityService.GetCapabilities(detection.Version);
            var notes = new List<string>();
            if (!_sourceUpdated || step.Component == UpdateComponent.WingetFinal)
            {
                var source = await runner.RunAsync(new(detection.ExecutablePath, ["source", "update"], TimeSpan.FromMinutes(2)), cancellationToken).ConfigureAwait(false);
                notes.Add(source.ExitCode == 0 ? "Sources WinGet actualisées." : "Actualisation des sources WinGet impossible ; poursuite de la passe.");
                _sourceUpdated = true;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var args = step.Component == UpdateComponent.WingetFinal ? BuildFinalArguments(capabilities).ToList() : WingetUpdateExecutionService.BuildUpgradeArguments(app!.Id, capabilities).ToList();
            if (step.Component == UpdateComponent.Winget) args.AddRange(["--version", app!.AvailableVersion]);
            if (step.Component == UpdateComponent.WingetFinal && !capabilities.SupportsIncludePinned) notes.Add("WinGet antérieur à 1.5 : option --include-pinned retirée.");
            var result = await runner.RunAsync(new(detection.ExecutablePath, args, TimeSpan.FromMinutes(20)), CancellationToken.None).ConfigureAwait(false);
            var success = result.ExitCode == 0 && !result.TimedOut && !result.Cancelled && result.LaunchError is null;
            return new(step, success, result.ExitCode == 3010, string.Join("\n", notes.Append(success ? "Application mise à jour." : "Mise à jour non terminée.")));
        }
        var grouped = await ExecuteGroupAsync([step], cancellationToken).ConfigureAwait(false);
        return grouped.First(result => result.Step == step);
    }

    public static IReadOnlyList<string> BuildFinalArguments(WingetCapabilities capabilities)
    {
        var args = new List<string> { "upgrade", "--all", "--include-unknown" };
        if (capabilities.SupportsIncludePinned) args.Add("--include-pinned");
        args.AddRange(["--force", "--accept-source-agreements", "--accept-package-agreements", "--silent", "--disable-interactivity"]);
        return args;
    }

    public async Task<IReadOnlyList<UpdateSequenceStepResult>> ExecuteGroupAsync(IReadOnlyList<UpdateSequenceStep> steps, CancellationToken token)
    {
        var prepare = steps.All(s => s.Component is UpdateComponent.Defender or UpdateComponent.MicrosoftUpdate);
        if (!_prepared && !prepare)
        {
            var protection = await RunHelperAsync(InterventionId.PrepareMicrosoftUpdates, "", token).ConfigureAwait(false);
            if (!_prepared || token.IsCancellationRequested) return steps.Select(s => new UpdateSequenceStepResult(s, false, protection.Reboot, ReadableOutput(protection.Message))).ToArray();
        }
        if (!prepare && _restorePointSequenceNumber is null) return steps.Select(s => new UpdateSequenceStepResult(s, false, false, "Point de restauration non vérifié. Installation bloquée.")).ToArray();
        var identity = string.Join(",", steps.Select(s => s.Component switch
        {
            UpdateComponent.Defender => "defender",
            UpdateComponent.MicrosoftUpdate => "microsoft-update",
            UpdateComponent.Windows => "software",
            UpdateComponent.Driver => "drivers",
            UpdateComponent.Store => "store",
            UpdateComponent.WindowsFeatureUpgrade => "windows-feature-upgrade",
            _ => throw new InvalidOperationException("Composant inconnu.")
        }));
        var result = await RunHelperAsync(prepare ? InterventionId.PrepareMicrosoftUpdates : InterventionId.InstallMicrosoftUpdates, identity, token).ConfigureAwait(false);
        var output = ParseOutput(result.Message);
        var results = new List<UpdateSequenceStepResult>();
        if (prepare)
        {
            var restoreNotes = output.Where(line => line.Step == "restore-point" && line.Status != "OK").Select(line => line.Message);
            var restoreMessage = _prepared ? string.Join("\n", restoreNotes.Prepend($"Point de restauration vérifié (numéro {_restorePointSequenceNumber}).")) : ReadableOutput(result.Message);
            results.Add(new(new("restore", "Point de restauration", UpdateComponent.Restore), _prepared, false, restoreMessage) { RestorePointVerified = _prepared });
        }
        foreach (var step in steps)
        {
            var lines = output.Where(line => line.Step == Token(step.Component)).ToArray();
            var wasUnavailable = step.Component == UpdateComponent.Store && lines.Any(line => line.Status == "UNAVAILABLE") &&
                !lines.Any(line => line.Status is "FAILED" or "CANCELLED");
            var succeeded = lines.Length == 0 ? result.Success : !lines.Any(line => line.Status is "FAILED" or "CANCELLED" or "UNAVAILABLE") && lines.Any(line => line.Status == "OK");
            var message = lines.Length == 0
                ? (output.Count == 0 ? ReadableOutput(result.Message) : string.Join("\n", output.Where(line => line.Step == "maintenance").Select(line => line.Message)))
                : string.Join("\n", lines.Select(line => line.Message).Distinct(StringComparer.Ordinal));
            if (wasUnavailable) message = "Scan Store non disponible sur ce PC (information, best effort).";
            if (string.IsNullOrWhiteSpace(message)) message = succeeded ? "Étape terminée." : "Étape non terminée.";
            if (prepare && !_prepared)
            {
                succeeded = false;
                message = "Préparation Microsoft bloquée : point de restauration non vérifié.";
            }
            results.Add(new(step, succeeded, result.Reboot, message) { RestorePointVerified = prepare ? _prepared : null, WasUnavailable = wasUnavailable });
        }
        return results;
    }

    private sealed record OutputLine(string Step, string Status, string Message);
    private static IReadOnlyList<OutputLine> ParseOutput(string message) => message.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Where(line => line.StartsWith("VIRGIL|", StringComparison.Ordinal))
        .Select(line => line.Split('|', 4)).Where(parts => parts.Length == 4)
        .Select(parts => new OutputLine(parts[1], parts[2], parts[3].TrimEnd('\r'))).ToArray();
    private static string ReadableOutput(string message)
    {
        var lines = ParseOutput(message);
        if (lines.Count == 0) return message.Replace("VIRGIL|", "", StringComparison.Ordinal);
        return string.Join("\n", lines.Where(line => line.Step != "restore-point" || line.Status != "OK").Select(line => line.Message));
    }
    private static string Token(UpdateComponent component) => component switch
    {
        UpdateComponent.Defender => "defender", UpdateComponent.MicrosoftUpdate => "microsoft-update",
        UpdateComponent.Windows => "software", UpdateComponent.Driver => "drivers", UpdateComponent.Store => "store",
        UpdateComponent.WindowsFeatureUpgrade => "windows-feature-upgrade", _ => ""
    };

    private async Task<(bool Success, bool Reboot, string Message)> RunHelperAsync(InterventionId action, string identity, CancellationToken cancellationToken)
    {
        var file = await store.CreateAsync(action, cancellationToken, identity, _restoreSessionId, _restorePointSequenceNumber).ConfigureAwait(false);
        var helperFinished = false;
        try
        {
            var cancellationRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(() => cancellationRequested.TrySetResult());
            var launch = launcher.RunElevatedAsync(helperPath(), file.RequestPath, CancellationToken.None);
            await Task.WhenAny(launch, cancellationRequested.Task).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) store.RequestCancel(file);
            await launch.ConfigureAwait(false);
            helperFinished = true;
            if (cancellationToken.IsCancellationRequested) store.RequestCancel(file);
            var result = await store.ReadResultAsync(file, CancellationToken.None).ConfigureAwait(false);
            var message = result?.ReadableError ?? result?.SummaryOutput ?? "Assistant élevé sans résultat.";
            var success = result?.Status is InterventionStatus.Completed or InterventionStatus.RebootRequired;
            if (action == InterventionId.PrepareMicrosoftUpdates)
            {
                var number = message.Split('\n').FirstOrDefault(line => line.StartsWith("VIRGIL|restore-point|OK|", StringComparison.Ordinal))?.Split('|').Last();
                _restorePointSequenceNumber = int.TryParse(number, out var parsed) && parsed > 0 ? parsed : null;
                _prepared = _restorePointSequenceNumber is not null;
            }
            return (success, result?.RebootRequired == true, message);
        }
        finally { if (helperFinished) store.Cleanup(file); }
    }
}
