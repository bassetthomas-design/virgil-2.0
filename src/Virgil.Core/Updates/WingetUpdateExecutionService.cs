using Virgil.Core.Actions;
using Virgil.Core.Processes;
using System.Diagnostics;
using Virgil.Contracts;

namespace Virgil.Core.Updates;

public sealed class WingetUpdateExecutionService : IUpdateExecutionService
{
    private readonly IConfirmedPlanGate _gate;
    private readonly WingetAvailabilityService _availabilityService;
    private readonly IProcessRunner _processRunner;

    public WingetUpdateExecutionService(IProcessRunner processRunner, IConfirmedPlanGate gate)
        : this(new WingetAvailabilityService(processRunner), processRunner, gate)
    {
    }

    public WingetUpdateExecutionService(
        WingetAvailabilityService availabilityService,
        IProcessRunner processRunner,
        IConfirmedPlanGate gate)
    {
        _gate = gate;
        _availabilityService = availabilityService;
        _processRunner = processRunner;
    }

    public async Task<UpdateExecutionResult> ExecuteAsync(UpdateItem item, ConfirmedPlan? plan, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();

        if (item.IsIdTruncated || item.Id.Contains('…') || item.RiskLevel == UpdateRiskLevel.CriticalInformationOnly ||
            item.Source is UpdateSource.Driver or UpdateSource.FirmwareInformation)
        {
            stopwatch.Stop();
            return BuildResult(item, UpdateItemStatus.InformationOnly, startedAt, stopwatch.Elapsed,
                "Information uniquement. Aucune installation effectuee.",
                "Execution bloquee par garde-fou.");
        }

        if (string.IsNullOrWhiteSpace(item.Id))
        {
            stopwatch.Stop();
            return BuildResult(item, UpdateItemStatus.Failed, startedAt, stopwatch.Elapsed,
                "Mise a jour ignoree : identifiant manquant.",
                "Aucun --id exact disponible.");
        }

        // Le hash du plan valide AvailableVersion ; l'execution epingle cette valeur sans re-scan.
        if (!IsValidPackageVersion(item.AvailableVersion))
        {
            stopwatch.Stop();
            return BuildResult(item, UpdateItemStatus.Failed, startedAt, stopwatch.Elapsed,
                "Mise a jour bloquee : version confirmee manquante ou invalide. Aucune installation effectuee.",
                "Aucun --version exact disponible.");
        }

        var winget = await _availabilityService.DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!winget.IsAvailable || string.IsNullOrWhiteSpace(winget.ExecutablePath))
        {
            stopwatch.Stop();
            return BuildResult(item, UpdateItemStatus.Failed, startedAt, stopwatch.Elapsed,
                "WinGet indisponible. Aucune action effectuee.",
                winget.Message);
        }

        var validation = _gate.TryConsume(plan, UpdateActionPreviewFactory.Create(item));
        if (!validation.IsAccepted)
        {
            stopwatch.Stop();
            return BuildResult(item, UpdateItemStatus.Cancelled, startedAt, stopwatch.Elapsed,
                "Installation annulee.\n" + validation.Message, validation.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var capabilities = WingetAvailabilityService.GetCapabilities(winget.Version);
        var result = await _processRunner
            .RunAsync(new ProcessRunRequest(
                winget.ExecutablePath,
                BuildPinnedUpgradeArguments(item.Id, item.AvailableVersion!, capabilities),
                TimeSpan.FromMinutes(15)),
                CancellationToken.None)
            .ConfigureAwait(false);

        stopwatch.Stop();

        if (result.Cancelled)
        {
            return BuildResult(item, UpdateItemStatus.Cancelled, startedAt, stopwatch.Elapsed,
                "Installation annulee. Aucune autre mise a jour lancee.",
                "Execution WinGet annulee.");
        }

        if (result.TimedOut)
        {
            return BuildResult(item, UpdateItemStatus.Failed, startedAt, stopwatch.Elapsed,
                "Installation interrompue par timeout.",
                "Execution WinGet depassee.");
        }

        if (!string.IsNullOrWhiteSpace(result.LaunchError))
        {
            return BuildResult(item, UpdateItemStatus.Failed, startedAt, stopwatch.Elapsed,
                "WinGet n'a pas pu etre lance.",
                result.LaunchError);
        }

        if (result.ExitCode == 0)
        {
            return BuildResult(item, UpdateItemStatus.Completed, startedAt, stopwatch.Elapsed,
                cancellationToken.IsCancellationRequested ? $"Mise à jour terminée après annulation : {item.Name}." : $"Mise à jour terminée : {item.Name}.",
                CompactTechnicalOutput(result));
        }

        return BuildResult(item, UpdateItemStatus.Failed, startedAt, stopwatch.Elapsed,
            $"Mise a jour non terminee : {item.Name}.",
            CompactTechnicalOutput(result));
    }

    public UpdateExecutionResult Skip(UpdateItem item)
    {
        return BuildResult(item, UpdateItemStatus.Skipped, DateTimeOffset.Now, TimeSpan.Zero,
            $"Mise a jour passee : {item.Name}.",
            "Choix utilisateur.");
    }

    public UpdateExecutionResult Cancel(UpdateItem item)
    {
        return BuildResult(item, UpdateItemStatus.Cancelled, DateTimeOffset.Now, TimeSpan.Zero,
            "Sequence annulee par l'utilisateur.",
            "Annulation utilisateur.");
    }

    public UpdateSessionReport CreateReport(
        DateTimeOffset startedAt,
        IReadOnlyList<UpdateExecutionResult> results,
        IReadOnlyList<string> errors)
    {
        return new UpdateSessionReport
        {
            StartedAt = startedAt,
            Duration = DateTimeOffset.Now - startedAt,
            Results = results,
            Errors = errors
        };
    }

    private static bool IsValidPackageVersion(string? version)
    {
        // Un seul jeton explicite : pas d'option, de guillemet ou de valeur inconnue.
        return !string.IsNullOrWhiteSpace(version)
            && version.Length <= 128
            && char.IsAsciiLetterOrDigit(version[0])
            && version.Any(char.IsAsciiDigit)
            && version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '+' or '~');
    }

    private static IReadOnlyList<string> BuildPinnedUpgradeArguments(
        string packageId, string version, WingetCapabilities capabilities)
    {
        var arguments = BuildUpgradeArguments(packageId, capabilities).ToList();
        arguments.InsertRange(3, new[] { "--version", version });
        return arguments;
    }

    public static IReadOnlyList<string> BuildUpgradeArguments(string packageId, WingetCapabilities capabilities)
    {
        var arguments = new List<string>
        {
            "upgrade",
            "--id",
            packageId,
            "--exact"
        };

        if (capabilities.SupportsIncludeUnknown) arguments.Add("--include-unknown");
        if (capabilities.SupportsIncludePinned) arguments.Add("--include-pinned");

        if (capabilities.SupportsAcceptPackageAgreements)
        {
            arguments.Add("--accept-package-agreements");
        }

        if (capabilities.SupportsAcceptSourceAgreements)
        {
            arguments.Add("--accept-source-agreements");
        }

        if (capabilities.SupportsDisableInteractivity)
        {
            arguments.Add("--disable-interactivity");
        }

        return arguments;
    }

    private static UpdateExecutionResult BuildResult(
        UpdateItem item,
        UpdateItemStatus status,
        DateTimeOffset startedAt,
        TimeSpan duration,
        string userMessage,
        string technicalDetails)
    {
        return new UpdateExecutionResult
        {
            Item = item with { Status = status },
            Status = status,
            StartedAt = startedAt,
            Duration = duration,
            UserMessage = userMessage,
            TechnicalDetails = technicalDetails
        };
    }

    private static string CompactTechnicalOutput(ProcessRunResult result)
    {
        var lines = string.Join("\n", new[] { result.StandardOutput, result.StandardError }
                .Where(value => !string.IsNullOrWhiteSpace(value)))
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(8);

        var output = string.Join("\n", lines);
        return string.IsNullOrWhiteSpace(output)
            ? $"WinGet exit code {result.ExitCode}."
            : $"WinGet exit code {result.ExitCode}.\n{output}";
    }
}
