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
    private string _restoreSessionId = string.Empty;
    public void BeginExecution(string restoreSessionId) => _restoreSessionId = restoreSessionId;

    public Task<IReadOnlyList<UpdateSequenceStep>> ScanDriversAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<UpdateSequenceStep>>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var steps = new List<UpdateSequenceStep>();
        try
        {
            dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", true)!)!;
            dynamic searcher = session.CreateUpdateSearcher();
            dynamic search = searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0");
            for (var i = 0; i < search.Updates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                dynamic update = search.Updates.Item(i);
                steps.Add(new((string)update.Identity.UpdateID, (string)update.Title, UpdateComponent.Driver));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { steps.Add(new("unavailable", "Recherche des pilotes indisponible ; réessayez plus tard", UpdateComponent.Driver)); }
        return steps;
    }, cancellationToken);

    public static bool IsSafePackageId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 256 &&
        char.IsAsciiLetterOrDigit(id[0]) && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+' or '\\' or '{' or '}');
    public static bool IsSafeVersion(string version) => !string.IsNullOrWhiteSpace(version) && version.Length <= 128 &&
        char.IsAsciiLetterOrDigit(version[0]) && version.Any(char.IsAsciiDigit) &&
        version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '+' or '~');

    public async Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken cancellationToken)
    {
        if (step.Component == UpdateComponent.Launcher)
            return new(step, true, false, "À mettre à jour depuis le lanceur.");
        if (step.Component == UpdateComponent.Winget)
        {
            var app = step.Application;
            if (app is null || string.IsNullOrWhiteSpace(app.Id) || app.IsIdTruncated || !IsSafePackageId(app.Id) || !IsSafeVersion(app.AvailableVersion))
                return new(step, false, false, "Identité ou version WinGet incomplète. Relancez l’analyse.");
            var detection = await (wingetAvailability ?? new WingetAvailabilityService(runner)).DetectAsync(cancellationToken).ConfigureAwait(false);
            if (!detection.IsAvailable || string.IsNullOrWhiteSpace(detection.ExecutablePath)) return new(step, false, false, "WinGet indisponible.");
            var capabilities = WingetAvailabilityService.GetCapabilities(detection.Version);
            var args = WingetUpdateExecutionService.BuildUpgradeArguments(app.Id, capabilities).ToList();
            args.AddRange(["--version", app.AvailableVersion]);
            var result = await runner.RunAsync(new(detection.ExecutablePath, args, TimeSpan.FromMinutes(20)), cancellationToken).ConfigureAwait(false);
            var success = result.ExitCode == 0 && !result.TimedOut && !result.Cancelled && result.LaunchError is null;
            return new(step, success, result.ExitCode == 3010, success ? "Application mise à jour." : "Mise à jour non terminée.");
        }
        var action = step.Component switch
        {
            UpdateComponent.Windows => InterventionId.InstallWindowsUpdates,
            UpdateComponent.Defender => InterventionId.UpdateDefenderSignatures,
            UpdateComponent.Driver => InterventionId.InstallDriverUpdate,
            _ => throw new InvalidOperationException("Composant inconnu.")
        };
        if (step.Component == UpdateComponent.Driver && !Guid.TryParseExact(step.Id, "D", out _))
            return new(step, false, false, "Identité du pilote invalide.");
        var file = await store.CreateAsync(action, cancellationToken, step.Component == UpdateComponent.Driver ? step.Id : "", _restoreSessionId).ConfigureAwait(false);
        try
        {
            await launcher.RunElevatedAsync(helperPath(), file.RequestPath, cancellationToken).ConfigureAwait(false);
            var result = await store.ReadResultAsync(file, cancellationToken).ConfigureAwait(false);
            return new(step, result?.Status is InterventionStatus.Completed or InterventionStatus.RebootRequired,
                result?.RebootRequired == true, result?.ReadableError ?? result?.SummaryOutput ?? "Assistant élevé sans résultat.");
        }
        finally { store.Cleanup(file); }
    }
}
