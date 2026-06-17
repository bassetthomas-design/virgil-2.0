using System.Diagnostics;
using Virgil.Domain;

namespace Virgil.Core.Updates;

public sealed class WingetUpdateExecutionService : IUpdateExecutionService
{
    private readonly WingetAvailabilityService _availabilityService;
    private readonly IProcessRunner _processRunner;

    public WingetUpdateExecutionService()
        : this(CreateDefaultProcessRunner())
    {
    }

    public WingetUpdateExecutionService(IProcessRunner processRunner)
        : this(new WingetAvailabilityService(processRunner), processRunner)
    {
    }

    public WingetUpdateExecutionService(
        WingetAvailabilityService availabilityService,
        IProcessRunner processRunner)
    {
        _availabilityService = availabilityService;
        _processRunner = processRunner;
    }

    public async Task<UpdateExecutionResult> ExecuteAsync(UpdateItem item, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();

        if (item.RiskLevel == UpdateRiskLevel.CriticalInformationOnly ||
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

        var winget = await _availabilityService.DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!winget.IsAvailable || string.IsNullOrWhiteSpace(winget.ExecutablePath))
        {
            stopwatch.Stop();
            return BuildResult(item, UpdateItemStatus.Failed, startedAt, stopwatch.Elapsed,
                "WinGet indisponible. Aucune action effectuee.",
                winget.Message);
        }

        var capabilities = WingetAvailabilityService.GetCapabilities(winget.Version);
        var result = await _processRunner
            .RunAsync(new ProcessRunRequest(
                winget.ExecutablePath,
                BuildUpgradeArguments(item.Id, capabilities),
                TimeSpan.FromMinutes(15)),
                cancellationToken)
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
                $"Mise a jour terminee : {item.Name}.",
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

    public static IReadOnlyList<string> BuildUpgradeArguments(string packageId, WingetCapabilities capabilities)
    {
        var arguments = new List<string>
        {
            "upgrade",
            "--id",
            packageId,
            "--exact"
        };

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

    private static IProcessRunner CreateDefaultProcessRunner()
    {
        return new ProcessRunner();
    }
}
