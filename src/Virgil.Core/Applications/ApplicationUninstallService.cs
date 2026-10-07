using System.Diagnostics;
using Virgil.Domain.Applications;

namespace Virgil.Core.Applications;

public sealed class ApplicationUninstallService
{
    private readonly ApplicationUninstallCommandValidator _validator;
    private readonly IApplicationProcessLauncher _launcher;
    private readonly ApplicationRemnantScanner _remnantScanner;
    private readonly ApplicationUninstallConfirmationPolicy _confirmationPolicy;

    public ApplicationUninstallService()
        : this(
            new ApplicationUninstallCommandValidator(),
            new ShellApplicationProcessLauncher(),
            new ApplicationRemnantScanner())
    {
    }

    public ApplicationUninstallService(
        ApplicationUninstallCommandValidator validator,
        IApplicationProcessLauncher launcher,
        ApplicationRemnantScanner remnantScanner)
        : this(validator, launcher, remnantScanner, new ApplicationUninstallConfirmationPolicy())
    {
    }

    public ApplicationUninstallService(
        ApplicationUninstallCommandValidator validator,
        IApplicationProcessLauncher launcher,
        ApplicationRemnantScanner remnantScanner,
        ApplicationUninstallConfirmationPolicy confirmationPolicy)
    {
        _validator = validator;
        _launcher = launcher;
        _remnantScanner = remnantScanner;
        _confirmationPolicy = confirmationPolicy;
    }

    public ApplicationUninstallPlan BuildPlan(InstalledApplication application)
    {
        var validation = _validator.Validate(application);
        return new ApplicationUninstallPlan
        {
            Application = application,
            Method = application.UninstallKind,
            Validation = validation,
            RequiresCautionConfirmation = application.RiskLevel == ApplicationRiskLevel.Caution
        };
    }

    public async Task<ApplicationUninstallResult> LaunchOfficialUninstallAsync(
        InstalledApplication application,
        bool userConfirmed,
        IProgress<ApplicationUninstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        return await LaunchOfficialUninstallAsync(
            application,
            new ApplicationUninstallConfirmation
            {
                ExplicitlyConfirmed = userConfirmed,
                ReinforcedConfirmed = false,
                Source = "legacy-bool"
            },
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApplicationUninstallResult> LaunchOfficialUninstallAsync(
        InstalledApplication application,
        ApplicationUninstallConfirmation confirmation,
        IProgress<ApplicationUninstallProgress>? progress,
        CancellationToken cancellationToken)
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

        Report(progress, 2, 5, 35, "lancement", "Lancement du desinstalleur officiel.");
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
                Result = "Executable officiel absent apres validation.",
                Errors = ["Executable officiel absent apres validation."]
            };
        }

        var launch = await _launcher
            .LaunchAsync(executable, arguments, useShellExecute: true, cancellationToken)
            .ConfigureAwait(false);

        Report(progress, 3, 5, 55, "assistant officiel", "Suivre l'assistant officiel si une fenetre s'ouvre.");
        Report(progress, 4, 5, 78, "restes", "Analyse lecture seule des restes.");
        var remnants = await _remnantScanner.ScanAsync(application, cancellationToken).ConfigureAwait(false);
        Report(progress, 5, 5, 100, "rapport", "Rapport final prepare.");

        return new ApplicationUninstallResult
        {
            Application = application,
            Method = application.UninstallKind,
            WasLaunched = launch.Started,
            StatusUnknown = launch.Started && launch.ExitCode is null,
            ExitCode = launch.ExitCode,
            WasExplicitlyConfirmed = confirmation.ExplicitlyConfirmed,
            WasReinforcedConfirmed = confirmation.ReinforcedConfirmed,
            Result = launch.Started
                ? "Desinstalleur officiel lance. Statut final potentiellement gere par l'assistant externe."
                : launch.ReadableError ?? "Desinstalleur officiel non lance.",
            Errors = string.IsNullOrWhiteSpace(launch.ReadableError) ? remnants.Errors : remnants.Errors.Append(launch.ReadableError).ToList(),
            Remnants = remnants
        };
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
    public Task<ApplicationLaunchResult> LaunchAsync(
        string executable,
        IReadOnlyList<string> arguments,
        bool useShellExecute,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = useShellExecute
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = Process.Start(startInfo);
            return Task.FromResult(new ApplicationLaunchResult(process is not null, process?.HasExited == true ? process.ExitCode : null, null));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Task.FromResult(new ApplicationLaunchResult(false, null, "Desinstalleur officiel impossible a lancer."));
        }
    }
}
