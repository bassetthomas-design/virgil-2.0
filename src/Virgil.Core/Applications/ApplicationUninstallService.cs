using Virgil.Core.Actions;
using Virgil.Core.Processes;
using Virgil.Contracts.Applications;

namespace Virgil.Core.Applications;

public sealed class ApplicationUninstallService
{
    private readonly ApplicationUninstallCommandValidator _validator;
    private readonly IApplicationProcessLauncher _launcher;
    private readonly ApplicationRemnantScanner _remnantScanner;
    private readonly ApplicationUninstallConfirmationPolicy _confirmationPolicy;
    private readonly IConfirmedPlanGate _gate;

    public ApplicationUninstallService(
        ApplicationUninstallCommandValidator validator,
        IApplicationProcessLauncher launcher,
        ApplicationRemnantScanner remnantScanner,
        IConfirmedPlanGate gate)
        : this(validator, launcher, remnantScanner, new ApplicationUninstallConfirmationPolicy(), gate)
    {
    }

    public ApplicationUninstallService(
        ApplicationUninstallCommandValidator validator,
        IApplicationProcessLauncher launcher,
        ApplicationRemnantScanner remnantScanner,
        ApplicationUninstallConfirmationPolicy confirmationPolicy,
        IConfirmedPlanGate gate)
    {
        _validator = validator;
        _launcher = launcher;
        _remnantScanner = remnantScanner;
        _confirmationPolicy = confirmationPolicy;
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public ApplicationUninstallPlan BuildPlan(InstalledApplication application)
    {
        var validation = _validator.Validate(application);
        return new ApplicationUninstallPlan
        {
            Application = application,
            Method = application.UninstallKind,
            Validation = validation,
            RequiresCautionConfirmation = application.RiskLevel == ApplicationRiskLevel.Caution || application.UninstallKind == ApplicationUninstallKind.StorePackage
        };
    }

    public Task<ApplicationUninstallResult> LaunchOfficialUninstallAsync(
        InstalledApplication application,
        ConfirmedPlan? confirmedPlan,
        ApplicationUninstallConfirmation confirmation,
        IProgress<ApplicationUninstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        return LaunchOfficialUninstallAsync(application, confirmation, progress, cancellationToken, confirmedPlan);
    }

    private async Task<ApplicationUninstallResult> LaunchOfficialUninstallAsync(
        InstalledApplication application,
        ApplicationUninstallConfirmation confirmation,
        IProgress<ApplicationUninstallProgress>? progress,
        CancellationToken cancellationToken,
        ConfirmedPlan? confirmedPlan)
    {
        Report(progress, 1, 5, 10, "validation", "Validation stricte de la commande.");
        var plan = BuildPlan(application);
        if (!plan.CanLaunch)
        {
            return new ApplicationUninstallResult
            {
                Application = application,
                Method = application.UninstallKind,
                Result = plan.Validation.Reason,
                Errors = [plan.Validation.Reason]
            };
        }

        var confirmationDecision = _confirmationPolicy.Validate(plan, confirmation);
        if (!confirmationDecision.CanProceed)
        {
            return new ApplicationUninstallResult
            {
                Application = application,
                Method = application.UninstallKind,
                WasCancelled = confirmationDecision.WasCancelled,
                WasExplicitlyConfirmed = confirmation.ExplicitlyConfirmed,
                WasReinforcedConfirmed = confirmation.ReinforcedConfirmed,
                Result = confirmationDecision.Reason,
                Errors = [confirmationDecision.Reason]
            };
        }

        Report(progress, 2, 5, 35, "lancement", "Lancement du désinstalleur officiel.");
        var executable = plan.Validation.Executable;
        var arguments = plan.Validation.Arguments;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return new ApplicationUninstallResult
            {
                Application = application,
                Method = application.UninstallKind,
                WasExplicitlyConfirmed = confirmation.ExplicitlyConfirmed,
                WasReinforcedConfirmed = confirmation.ReinforcedConfirmed,
                Result = "Executable officiel absent après validation.",
                Errors = ["Executable officiel absent après validation."]
            };
        }

        var gateDecision = _gate.TryConsume(confirmedPlan, ApplicationActionPreviewFactory.Create(plan));
        if (!gateDecision.IsAccepted)
        {
            var message = "Désinstallation annulée.\n" + gateDecision.Message;
            return new ApplicationUninstallResult
            {
                Application = application,
                Method = application.UninstallKind,
                WasCancelled = true,
                WasExplicitlyConfirmed = confirmation.ExplicitlyConfirmed,
                WasReinforcedConfirmed = confirmation.ReinforcedConfirmed,
                Result = message,
                Errors = [message]
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        var launch = await _launcher
            .LaunchAsync(executable, arguments, useShellExecute: true, CancellationToken.None)
            .ConfigureAwait(false);

        Report(progress, 3, 5, 55, "assistant officiel", "Suivre l'assistant officiel si une fenêtre s'ouvre.");
        Report(progress, 4, 5, 78, "restes", "Analyse lecture seule des restes.");
        var remnants = cancellationToken.IsCancellationRequested
            ? new ApplicationRemnantScanReport { Application = application }
            : await _remnantScanner.ScanAsync(application, cancellationToken).ConfigureAwait(false);
        Report(progress, 5, 5, 100, "rapport", "Rapport final préparé.");

        return new ApplicationUninstallResult
        {
            Application = application,
            Method = application.UninstallKind,
            WasLaunched = launch.Started,
            StatusUnknown = launch.Started && launch.ExitCode is null,
            ExitCode = launch.ExitCode,
            WasExplicitlyConfirmed = confirmation.ExplicitlyConfirmed,
            WasReinforcedConfirmed = confirmation.ReinforcedConfirmed,
            Result = cancellationToken.IsCancellationRequested && launch.Started && launch.ExitCode.HasValue ? "Désinstalleur terminé après annulation. Aucune étape suivante lancée." : launch.Started
                ? "Désinstalleur officiel lancé. Statut final potentiellement géré par l'assistant externe."
                : launch.ReadableError ?? "Désinstalleur officiel non lancé.",
            Errors = string.IsNullOrWhiteSpace(launch.ReadableError) ? remnants.Errors : remnants.Errors.Append(launch.ReadableError).ToList(),
            Remnants = remnants
        };
    }

    public async Task<IReadOnlyList<ApplicationUninstallResult>> LaunchBatchAsync(
        IReadOnlyList<InstalledApplication> applications, ConfirmedPlan? confirmedPlan,
        ApplicationUninstallConfirmation confirmation, IProgress<ApplicationUninstallProgress>? progress,
        CancellationToken cancellationToken, Func<InstalledApplication, bool>? skip = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Freeze the selection and exact commands before consuming the one-use batch plan.
        var plans = applications.Select(BuildPlan).Select(plan => plan with
        {
            Validation = plan.Validation with { Arguments = plan.Validation.Arguments.ToArray() }
        }).ToArray();
        var invalid = plans.Length == 0 || plans.Select(plan => plan.Application.Id).Distinct(StringComparer.Ordinal).Count() != plans.Length ||
            plans.Any(plan => !plan.Application.CanUninstall || plan.Application.IsSystemComponent ||
                plan.Application.RiskLevel == ApplicationRiskLevel.Protected || !plan.CanLaunch ||
                !_confirmationPolicy.Validate(plan, confirmation).CanProceed);
        if (invalid) return [new ApplicationUninstallResult { WasCancelled = true, Result = "Sélection refusée : élément protégé, commande invalide ou confirmation absente." }];
        var validation = _gate.TryConsume(confirmedPlan, ApplicationBatchPreviewFactory.Create(plans));
        if (!validation.IsAccepted)
            return [new ApplicationUninstallResult { WasCancelled = true, Result = validation.Message, Errors = [validation.Message] }];

        var results = new List<ApplicationUninstallResult>();
        for (var index = 0; index < plans.Length; index++)
        {
            var plan = plans[index];
            if (cancellationToken.IsCancellationRequested) break;
            if (skip?.Invoke(plan.Application) == true)
            {
                results.Add(new ApplicationUninstallResult { Application = plan.Application, WasCancelled = true, Result = "Élément passé par l’utilisateur." });
                continue;
            }
            Report(progress, index + 1, plans.Length, index * 100 / plans.Length,
                plan.Application.DisplayName, $"Désinstallation {index + 1}/{plans.Length} : {plan.Application.DisplayName}");
            try
            {
                // Les callbacks peuvent demander l’arrêt : vérifier après eux, avant l’effet OS.
                if (cancellationToken.IsCancellationRequested) break;
                var launch = await _launcher.LaunchAsync(plan.Validation.Executable!, plan.Validation.Arguments,
                    useShellExecute: true, CancellationToken.None).ConfigureAwait(false);
                var remnants = !cancellationToken.IsCancellationRequested && launch.Started && launch.ExitCode is 0 or 3010
                    ? await _remnantScanner.ScanAsync(plan.Application, cancellationToken).ConfigureAwait(false)
                    : new ApplicationRemnantScanReport { Application = plan.Application };
                results.Add(new ApplicationUninstallResult
                {
                    Application = plan.Application, Method = plan.Method, WasLaunched = launch.Started,
                    ExitCode = launch.ExitCode, StatusUnknown = launch.Started && launch.ExitCode is null,
                    WasExplicitlyConfirmed = confirmation.ExplicitlyConfirmed, WasReinforcedConfirmed = confirmation.ReinforcedConfirmed,
                    Result = cancellationToken.IsCancellationRequested && launch.Started && launch.ExitCode.HasValue ? "Désinstalleur terminé après annulation ; éléments suivants non lancés." : launch.ReadableError ?? (launch.ExitCode is 0 or 3010 ? "Désinstalleur terminé ; restes examinés en lecture seule."
                        : launch.Started ? "Désinstalleur lancé ; état final à vérifier." : "Désinstalleur non lancé."),
                    Errors = launch.ReadableError is null ? remnants.Errors : [launch.ReadableError], Remnants = remnants
                });
                // Never overlap installers when an external bootstrapper's final state is unknown.
                if (launch.Started && launch.ExitCode is null) break;
            }
            catch (OperationCanceledException)
            {
                results.Add(new ApplicationUninstallResult { Application = plan.Application, WasCancelled = true,
                    StatusUnknown = true, Result = "Attente annulée. L’assistant officiel peut rester ouvert ; aucune autre application ne sera lancée." });
                break;
            }
            catch (Exception ex)
            {
                results.Add(new ApplicationUninstallResult { Application = plan.Application, Result = "Désinstallation indisponible.", Errors = [ex.Message] });
            }
        }
        if (results.Count < plans.Length)
            results.AddRange(plans.Skip(results.Count).Select(plan => new ApplicationUninstallResult
            { Application = plan.Application, WasCancelled = true, Result = "Non lancée : parcours interrompu." }));
        Report(progress, plans.Length, plans.Length, 100, "Rapport", "Parcours terminé ; consultez le rapport.");
        return results;
    }

    private static void Report(
        IProgress<ApplicationUninstallProgress>? progress,
        int step,
        int total,
        int percent,
        string label,
        string status)
    {
        progress?.Report(new ApplicationUninstallProgress
        {
            StepNumber = step,
            TotalSteps = total,
            Percent = percent,
            Step = label,
            Status = status
        });
    }
}

public sealed class ShellApplicationProcessLauncher : IApplicationProcessLauncher
{
    private readonly IProcessRunner _runner;

    public ShellApplicationProcessLauncher(IProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task<ApplicationLaunchResult> LaunchAsync(
        string executable,
        IReadOnlyList<string> arguments,
        bool useShellExecute,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _runner.LaunchAsync(
                new ProcessLaunchRequest(executable, arguments, useShellExecute, WaitForExit: true), cancellationToken)
                .ConfigureAwait(false);
            return result.LaunchError is not null
                ? new ApplicationLaunchResult(false, null, "Désinstalleur officiel impossible à lancer.")
                : new ApplicationLaunchResult(result.Started, result.ExitCode, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new ApplicationLaunchResult(false, null, "Désinstalleur officiel impossible à lancer.");
        }
    }
}
