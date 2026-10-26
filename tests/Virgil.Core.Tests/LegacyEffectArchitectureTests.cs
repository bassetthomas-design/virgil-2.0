using System.Text.RegularExpressions;
using Xunit;

namespace Virgil.Tests;

/// <summary>Inventaire fermé des frontières à effet ; compléter cet inventaire impose une revue.</summary>
public sealed class LegacyEffectArchitectureTests
{
    private static string Root => TestRepositoryRoot.FindRepositoryRoot();
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, "src", path));
    private static IEnumerable<string> Sources(string project) => Directory.EnumerateFiles(
        Path.Combine(Root, "src", project), "*.cs", SearchOption.AllDirectories)
        .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"));

    [Theory]
    [InlineData("Cleanup/CleanupExecutionService.cs", "_recycleBinService.Empty()")]
    [InlineData("Cleanup/CleanupExecutionService.cs", "_quarantine.MoveToQuarantine(")]
    [InlineData("Cleanup/QuarantineMaintenanceAction.cs", "_store.Purge(")]
    [InlineData("Cleanup/QuarantineMaintenanceAction.cs", "_store.Restore(")]
    [InlineData("Applications/ApplicationUninstallService.cs", ".LaunchAsync(executable")]
    [InlineData("Updates/WingetUpdateExecutionService.cs", ".RunAsync(new ProcessRunRequest(")]
    [InlineData("Interventions/InterventionExecutionService.cs", "_explorerRestarter.RestartAsync(")]
    [InlineData("Interventions/InterventionExecutionService.cs", ".ExecuteAsync(diagnostic.Definition")]
    public void GateRejectionReturnsBeforeEveryEffectBoundary(string path, string effect)
    {
        var source = Read("Virgil.Core/" + path);
        var gate = source.IndexOf("_gate.TryConsume(", StringComparison.Ordinal);
        var operation = source.IndexOf(effect, StringComparison.Ordinal);
        Assert.True(gate >= 0 && operation > gate, path);
        var between = source[gate..operation];
        Assert.Contains("IsAccepted", between);
        Assert.Contains("return ", between);
        Assert.Contains("IConfirmedPlanGate", source);
        // Nettoyage a deux entrées : sélection globale et zone ; chacune refuse avant le noyau partagé.
        if (path == "Cleanup/CleanupExecutionService.cs")
        {
            Assert.Equal(2, Regex.Matches(source, @"_gate\.TryConsume\(").Count);
            Assert.Contains("if (!validation.IsAccepted) return CleanupSelectionResult.Refused", source);
            Assert.Contains("ExecuteConfirmedZone", source);
        }
    }

    [Fact]
    public void ProcessAndDiagnosticEffectsUseGuardedServiceWithoutAlternateRuntime()
    {
        var source = Read("Virgil.Core/Resources/ProcessActionService.cs");
        Assert.Contains("if (!validation.IsAccepted)", source);
        Assert.Contains("return Refusal(", source);
        var close = source.IndexOf("await CloseGracefullyAsync(", StringComparison.Ordinal);
        Assert.True(source.IndexOf("if (rejected is not null) return rejected;", StringComparison.Ordinal) < close);
        Assert.Contains("return refusal ?? await RestartExplorerAsync", source);
        var diagnostic = Read("Virgil.Core/Diagnostic/DiagnosticCloseAction.cs");
        Assert.Contains("service.ExecuteAsync(Kind, target, plan, cancellationToken)", diagnostic);
        Assert.DoesNotContain("IProcessRuntime", diagnostic);
        var navigation = Read("Virgil.Core/Diagnostic/DiagnosticNavigationAction.cs");
        Assert.Contains("TryConsume", navigation);
        Assert.DoesNotContain("IProcessRunner", navigation);
    }

    [Theory]
    [InlineData("IProcessRuntime", "ProcessActionService.cs|ProcessRuntime.cs")]
    [InlineData("IRecycleBinService", "CleanupExecutionService.cs|CleanupPreviewService.cs|ICleanupDisk.cs|RecycleBinService.cs")]
    [InlineData("IQuarantineStore", "CleanupExecutionService.cs|IQuarantineStore.cs|QuarantineMaintenanceAction.cs")]
    [InlineData("IInterventionElevatedHelperClient", "ElevatedHelperClient.cs|IInterventionElevatedHelperClient.cs|InterventionExecutionService.cs")]
    [InlineData("IExplorerRestarter", "ExplorerRestarter.cs|IExplorerRestarter.cs|InterventionExecutionService.cs|ProcessActionService.cs")]
    [InlineData("IApplicationProcessLauncher", "ApplicationContracts.cs|ApplicationUninstallService.cs")]
    [InlineData("IUpdateSequenceBackend", "UpdateSequenceService.cs")]
    [InlineData("IWingetClientModuleInstaller", "IWingetClientModuleInstaller.cs|WingetClientModuleService.cs")]
    public void OnlyReviewedCoreConsumersMayAccessEffectAdapters(string port, string expected)
    {
        var consumers = Sources("Virgil.Core").Where(p => Regex.IsMatch(File.ReadAllText(p), @"\b" + port + @"\b"))
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected.Split('|').Order(StringComparer.Ordinal).ToArray(), consumers);
        // WPF ne reçoit aucun de ces adaptateurs bruts ; la composition est leur seule référence.
        Assert.DoesNotContain(Sources("Virgil.App").Where(p => !p.EndsWith("ServiceCollectionExtensions.cs", StringComparison.Ordinal)),
            p => Regex.IsMatch(File.ReadAllText(p), @"\b" + port + @"\b"));
    }

    [Fact]
    public void ProcessRunnerConsumersRemainExplicitlyInventoried()
    {
        var core = Sources("Virgil.Core").Where(p => Regex.IsMatch(File.ReadAllText(p), @"\bIProcessRunner\b"))
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "ApplicationStoreReader.cs", "ApplicationUninstallService.cs", "ApplicationWingetReader.cs",
            "DriverInformationService.cs", "ElevatedHelperClient.cs", "ExplorerRestarter.cs", "IProcessRunner.cs",
            "ProcessRuntime.cs", "WingetAvailabilityService.cs", "WingetUpdateExecutionService.cs", "WingetUpdateScanService.cs" }, core);
        var app = Sources("Virgil.App").Where(p => Regex.IsMatch(File.ReadAllText(p), @"\bIProcessRunner\b"))
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "ApplicationsViewModel.cs", "CleanupEntryItems.cs", "CleanupViewModel.cs",
            "ServiceCollectionExtensions.cs", "UpdatesViewModel.cs", "VirgilReleaseViewModel.cs" }, app);
    }

    [Fact]
    public void UpdateSequenceConsumesOneSelectionPlanBeforeCallingItsEffectAdapter()
    {
        var source = Read("Virgil.Core/Updates/UpdateSequenceService.cs");
        var gate = source.IndexOf("gate.TryConsume(", StringComparison.Ordinal);
        var effect = source.IndexOf("backend.ExecuteAsync(", StringComparison.Ordinal);
        Assert.True(gate >= 0 && effect > gate);
        Assert.Contains("if (!validation.IsAccepted) return", source[gate..effect]);
        Assert.Single(Regex.Matches(source, @"gate\.TryConsume\("));
        Assert.Contains("CreatePreview(snapshot)", source);

        var module = Read("Virgil.Core/Updates/WingetClientModuleService.cs");
        var moduleGate = module.IndexOf("gate.TryConsume(", StringComparison.Ordinal);
        var moduleEffect = module.IndexOf("installer.InstallAsync(", StringComparison.Ordinal);
        Assert.True(moduleGate >= 0 && moduleEffect > moduleGate);
        Assert.Contains("IsAccepted) return false", module[moduleGate..moduleEffect]);
        Assert.DoesNotContain("IProcessRunner", module);
        Assert.DoesNotContain("powershell.exe", module);
    }

    [Fact]
    public void AllMaintenanceActionImplementationsRemainInReviewedInventory()
    {
        var actions = Sources("Virgil.Core").Where(p => Regex.IsMatch(File.ReadAllText(p), @":\s*IMaintenanceAction\b"))
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "CleanupMaintenanceAction.cs", "CleanupSelectionAction.cs", "DiagnosticCloseAction.cs",
            "DiagnosticNavigationAction.cs", "QuarantineMaintenanceAction.cs" }, actions);
    }

    [Fact]
    public void ChatIsOutputOnlyTrayAbsentAndUnimplementedUiIsBlocked()
    {
        var xaml = Read("Virgil.App/MainWindow.xaml");
        var window = Read("Virgil.App/MainWindow.xaml.cs");
        Assert.Contains("ChatMessagesPanel", xaml);
        Assert.DoesNotContain("TextBox", xaml);
        Assert.DoesNotContain("SendCommand", xaml);
        Assert.DoesNotContain("ChatInput", window);
        Assert.Contains("Action indisponible dans cette alpha : parcours sécurisé de confirmation non livré.", window);
        var repairs = Read("Virgil.App/Views/InterventionsView.xaml");
        Assert.Contains("{Binding VerifyHealthCommand}", repairs);
        Assert.Contains("{Binding ScanRepairCommand}", repairs);
        var applications = Read("Virgil.App/ViewModels/ApplicationsViewModel.cs");
        Assert.Contains("ApplicationBatchPreviewFactory.Create", applications);
        Assert.Contains("_confirmationService.Issue(preview", applications);
        Assert.DoesNotContain("MessageBox", Read("Virgil.App/Services/ApplicationUninstallConfirmationDialogService.cs"));
        var sources = Sources("Virgil.App").Select(File.ReadAllText);
        foreach (var text in sources)
        {
            Assert.DoesNotContain("NotifyIcon", text);
            Assert.DoesNotContain("System.Windows.Forms", text);
        }
        Assert.False(Directory.Exists(Path.Combine(Root, "src", "Virgil.Tray")));
    }
}
