using Virgil.Contracts.Actions;
using Virgil.Contracts.Applications;
using Virgil.Core.Actions;
using Virgil.Core.Applications;
using Xunit;

namespace Virgil.Tests;

public sealed class Alpha11DesinstallationTests
{
    [Fact]
    public void Un_guid_ne_remplace_la_commande_officielle_que_pour_une_entree_msi()
    {
        var entry = new ApplicationRegistryEntry
        {
            KeyName = "{11111111-2222-3333-4444-555555555555}",
            DisplayName = "Exemple",
            UninstallString = @"C:\Program Files\Exemple\uninstall.exe"
        };

        var nonMsi = ApplicationRegistryReader.ConvertEntry(entry);
        Assert.Equal(entry.UninstallString, nonMsi.UninstallCommand);
        Assert.Equal(ApplicationUninstallKind.RegistryUninstallString, nonMsi.UninstallKind);
        Assert.Null(nonMsi.MsiProductCode);

        var msi = ApplicationRegistryReader.ConvertEntry(entry with { WindowsInstaller = true });
        Assert.Equal("msiexec.exe /x {11111111-2222-3333-4444-555555555555}", msi.UninstallCommand);
        Assert.Equal(ApplicationUninstallKind.Msi, msi.UninstallKind);
    }

    [Fact]
    public async Task Codes_de_sortie_distingues_en_individuel_et_en_lot()
    {
        const string launched = "Désinstalleur officiel lancé. Statut final potentiellement géré par l'assistant externe.";
        const string completed = "Désinstalleur terminé ; restes examinés en lecture seule.";
        (int? Code, string Individual, string Batch)[] cases =
        [
            (0, launched, completed),
            (3010, launched, completed),
            (null, launched, "Désinstalleur lancé ; état final à vérifier."),
            Failure(1602, "Désinstallation annulée dans l'assistant (code 1602)."),
            Failure(1223, "Désinstallation annulée dans l'assistant (code 1223)."),
            Failure(1605, "Produit introuvable pour Windows Installer (code 1605) : rien n'a été désinstallé."),
            Failure(1, "Le désinstalleur a échoué (code 1).")
        ];
        var application = new InstalledApplication
        {
            Id = "exemple-alpha12", DisplayName = "Exemple", WingetId = "Exemple.Application",
            RiskLevel = ApplicationRiskLevel.SafeToUninstall, CanUninstall = true,
            UninstallKind = ApplicationUninstallKind.Winget
        };
        var confirmation = new ApplicationUninstallConfirmation { ExplicitlyConfirmed = true };
        var authority = new ConfirmationService(TimeProvider.System);
        var consent = new ConfirmationConsent(ConfirmationLevel.Simple, null);

        foreach (var item in cases)
        {
            var service = new ApplicationUninstallService(new ApplicationUninstallCommandValidator(),
                new FakeLauncher(item.Code), new ApplicationRemnantScanner(), authority);
            var plan = service.BuildPlan(application);
            var singlePlan = authority.Issue(ApplicationActionPreviewFactory.Create(plan), consent);
            var batchPlan = authority.Issue(ApplicationBatchPreviewFactory.Create([plan]), consent);
            var single = await service.LaunchOfficialUninstallAsync(application, singlePlan, confirmation, null, CancellationToken.None);
            var batch = Assert.Single(await service.LaunchBatchAsync([application], batchPlan, confirmation, null, CancellationToken.None));
            Assert.Equal(item.Individual, single.Result);
            Assert.Equal(item.Batch, batch.Result);
            Assert.All(new[] { single, batch }, result =>
            {
                Assert.Equal(item.Code, result.ExitCode);
                Assert.Equal(item.Code is 1602 or 1223, result.WasCancelled);
                if (item.Code is not (null or 0 or 3010)) Assert.Contains(item.Individual, result.Errors);
            });
        }
    }

    private static (int, string, string) Failure(int code, string message) => (code, message, message);

    private sealed class FakeLauncher(int? exitCode) : IApplicationProcessLauncher
    {
        public Task<ApplicationLaunchResult> LaunchAsync(string executable, IReadOnlyList<string> arguments,
            bool useShellExecute, CancellationToken cancellationToken) =>
            Task.FromResult(new ApplicationLaunchResult(true, exitCode, null));
    }
}
