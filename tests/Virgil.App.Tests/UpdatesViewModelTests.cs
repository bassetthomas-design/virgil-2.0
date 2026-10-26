using Virgil.Core.Actions;
using Virgil.Contracts.Actions;
using Virgil.App.Animation;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Processes;
using Virgil.Core.Reports;
using Virgil.Core.Updates;
using Xunit;
using System.Xml.Linq;

namespace Virgil.Tests;

public sealed class UpdatesViewModelTests
{
    [Fact]
    public void InitialStateAndEmptyCommands()
    {
        var f = new Fixture();
        Idle(f.Vm);
        Assert.Equal("Diagnostic prêt. Aucune action automatique.", f.Vm.StatusText);
        Assert.Equal("NON ANALYSÉ", f.Vm.OverallStatus);
        Assert.Equal("NON DÉTECTÉ", f.Vm.WingetStatus);
        Assert.Equal("OUVERTURE MANUELLE UNIQUEMENT", f.Vm.WindowsUpdateStatus);
        Assert.Equal("NON INVENTORIÉ", f.Vm.DriversStatus);
        Assert.Equal("Aucun scan lancé.", Assert.IsType<UpdateTextCardViewModel>(Assert.Single(f.Vm.Entries)).Text);
        Assert.Single(f.Vm.DriverCards);
        Assert.False(f.Vm.IsRunSelectedEnabled);
        Assert.False(f.Vm.IsReportButtonEnabled);
        Assert.False(f.Vm.IsInstallDriversVisible);
        Assert.False(f.Vm.IsInstallDriversEnabled);
        Assert.False(f.Vm.IsValidationOpen);
        Assert.False(f.Vm.IsReportOpen);
        Assert.False(f.Vm.TryCloseOverlay());
        f.Vm.ViewReportCommand.Execute(null);
        Assert.Equal("Aucun rapport mises à jour disponible.", f.Messages.Last());
        f.Vm.InstallDriversCommand.Execute(null);
        Assert.Equal("Les pilotes disponibles sont proposés dans « Tout mettre à jour ».\nIls restent facultatifs et décochés par défaut.", f.Messages.Last());
        var returned = false;
        f.Vm.ReturnHomeRequested += (_, _) => returned = true;
        f.Vm.ReturnHomeCommand.Execute(null);
        Assert.True(returned);
    }

    [Fact]
    public async Task ScanPersistsRendersAndSelectionTracksAvailableCards()
    {
        var f = new Fixture();
        f.Scan.Report = Report(Item("z", UpdateRiskLevel.Sensitive), Item("b"), Item("a"), Item("critical", UpdateRiskLevel.CriticalInformationOnly), Item("done") with { Status = UpdateItemStatus.Completed });
        await f.Analyze();
        Assert.Equal(UpdateScanRequest.DeepPreview, f.Scan.Request);
        Assert.Equal("PRÊT", f.States.Last().Label);
        Assert.Equal(VirgilCoreState.Success, f.Vm.MiniCoreState);
        Assert.Equal("Scan terminé.\n5 mises à jour a verifier.", f.Messages.Last());
        Assert.Equal(1, f.Persisted);
        Assert.False(f.History.Token.CanBeCanceled);
        Assert.True(f.Vm.IsReportButtonEnabled);
        Assert.Equal(new[] { "a", "b", "done", "z", "critical" }, f.Cards.Select(c => c.Item.Id));
        var card = f.Cards.First();
        Assert.Equal("a - 2", card.Header);
        Assert.Equal(new[] { "ID : a", "Version installée : 1", "Version disponible : 2", "Source : WinGet", "Risque : validation requise - Motif", "Statut : disponible", "Commande : winget upgrade \"app test\"" }, card.Lines);
        card.IsSelected = true;
        Assert.True(f.Vm.IsRunSelectedEnabled);
        card.IsSelected = false;
        Assert.False(f.Vm.IsRunSelectedEnabled);
        Assert.All(f.Cards.Where(c => c.Item.Id is "critical" or "done"), c => Assert.False(c.IsSelectable));
        await f.Run();
        Assert.Equal("Aucune application sélectionnée.", f.Messages.Last());
        Assert.Empty(f.Actions.Order);
        Idle(f.Vm);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScanCancellationAndErrorKeepTextsAndDoNotPersist(bool cancelled)
    {
        var f = new Fixture();
        f.Scan.Handler = _ =>
        {
            if (cancelled) throw new OperationCanceledException();
            throw new InvalidOperationException();
        };
        await f.Analyze();
        Assert.Equal(cancelled ? "Scan annulé." : "Scan mises à jour indisponible.", f.Vm.StatusText);
        Assert.Equal(cancelled ? "Scan annulé.\nAucune action effectuée." : "Scan indisponible.\nAucune action effectuée.", f.Messages.Last());
        Assert.Equal(cancelled ? VirgilCoreState.Idle : VirgilCoreState.Error, f.Vm.MiniCoreState);
        Assert.Equal(0, f.Persisted);
        Assert.False(f.Vm.IsReportButtonEnabled);
        Idle(f.Vm);
    }

    [Fact]
    public async Task MissingWingetEmptyCardsAndScanWarnings()
    {
        var f = new Fixture();
        f.Scan.Report = new() { Errors = new[] { "Lecture impossible" } };
        await f.Analyze();
        Assert.Equal("WinGet non détecté.", f.Vm.WingetStatus);
        Assert.Equal("WinGet non détecté.\nWindows Update reste disponible.", f.Messages.Last());
        Assert.Equal(VirgilCoreState.Warning, f.Vm.MiniCoreState);
        Assert.Equal("Aucune mise à jour applicative prévisualisée.", Assert.IsType<UpdateTextCardViewModel>(Assert.Single(f.Vm.Entries)).Text);
        Assert.Equal(1, f.Persisted);
        f.Vm.ViewReportCommand.Execute(null);
        Assert.Contains("WinGet : non détecté", f.Vm.ReportText);
        Assert.Contains("- Lecture impossible", f.Vm.ReportText);
    }

    [Theory]
    [InlineData(UpdateRiskLevel.Safe, "VALIDATION REQUISE", "INSTALLER", VirgilCoreState.Warning)]
    [InlineData(UpdateRiskLevel.ValidationRequired, "VALIDATION REQUISE", "INSTALLER", VirgilCoreState.Warning)]
    [InlineData(UpdateRiskLevel.Sensitive, "VALIDATION SENSIBLE", "JE CONFIRME", VirgilCoreState.SensitiveAction)]
    public async Task ExecutionRequiresDecisionForCurrentItem(UpdateRiskLevel risk, string title, string label, VirgilCoreState state)
    {
        var f = new Fixture();
        f.Scan.Report = Report(Item("app", risk));
        await f.Analyze(); f.SelectAll();
        var pending = f.Run();
        Assert.True(f.Vm.IsBusy);
        Assert.False(f.Vm.IsIdle);
        Assert.False(f.Vm.IsRunSelectedEnabled);
        Assert.False(f.Vm.IsReportButtonEnabled);
        Assert.True(f.Vm.IsValidationOpen);
        Assert.Equal(title, f.Vm.ValidationTitle);
        Assert.Equal(label, f.Vm.ExecuteLabel);
        Assert.Equal("PASSER", f.Vm.SkipLabel);
        Assert.Equal(state, f.Vm.ValidationCoreState);
        Assert.True(f.Vm.IsCancelSequenceVisible);
        Assert.Contains("Application : app\nID : app\nVersion : 1 -> 2\nSource : WinGet\nRisque :", f.Vm.ValidationDetails);
        Assert.EndsWith("\nMotif : Motif", f.Vm.ValidationDetails);
        Assert.Equal("winget upgrade \"app test\"", f.Vm.ValidationCommand);
        Assert.Equal("Validation individuelle obligatoire.\nAucun --all, aucune installation pilote, aucun redémarrage, aucune élévation demandee par Virgil.", f.Vm.ValidationSafety);
        Assert.Empty(f.Actions.Order);
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(new[] { "Execute:app" }, f.Actions.Order);
        Assert.Equal(1, f.Confirmation.Calls);
        var plan = Assert.Single(f.Actions.Plans);
        Assert.Equal(risk == UpdateRiskLevel.Sensitive ? ConfirmationLevel.Reinforced : ConfirmationLevel.Simple, plan.ConfirmationLevel);
        Assert.True(f.Confirmation.Authority.Validate(plan, UpdateActionPreviewFactory.Create(Item("app", risk))).IsAccepted);
        Assert.False(f.Vm.IsValidationOpen);
        Assert.Equal(VirgilCoreState.Idle, f.Vm.ValidationCoreState);
        Assert.Equal(UpdateItemStatus.Completed, Assert.Single(f.Actions.Report!.Results).Status);
        Assert.Equal("Séquence terminée.", f.Vm.StatusText);
        Assert.Equal(VirgilCoreState.Success, f.Vm.MiniCoreState);
        Assert.True(f.Vm.IsReportOpen);
        Assert.Equal(2, f.Persisted);
        Assert.All(f.Cards, c => Assert.False(c.IsSelected));
        Idle(f.Vm);
    }

    [Theory]
    [InlineData("SkipDecision")]
    [InlineData("CancelDecision")]
    [InlineData("TryCloseOverlay")]
    [InlineData("CancelActiveOperation")]
    public async Task ClosingValidationNeverExecutesAndPersistsReport(string decision)
    {
        var f = new Fixture(); await f.Analyze(); f.SelectAll();
        var pending = f.Run();
        Assert.Empty(f.Actions.Order);
        Decide(f.Vm, decision);
        await pending;
        Assert.Equal(0, f.Confirmation.Calls);
        Assert.DoesNotContain(f.Actions.Order, x => x.StartsWith("Execute:"));
        Assert.Equal(new[] { decision is "SkipDecision" or "TryCloseOverlay" ? "Skip:app" : "Cancel:app" }, f.Actions.Order);
        Assert.False(f.Vm.IsValidationOpen);
        Assert.True(f.Vm.IsReportOpen);
        Assert.Equal(2, f.Persisted);
        Assert.Equal(decision is "SkipDecision" or "TryCloseOverlay" ? "Séquence terminée." : "Séquence annulée.", f.Vm.StatusText);
        Idle(f.Vm);
    }

    [Fact]
    public async Task SkipExecuteCancelUsesOriginalOrderAndPersistsPartialReport()
    {
        var f = new Fixture(); f.Scan.Report = Report(Item("z"), Item("a"), Item("b"), Item("remaining"));
        await f.Analyze(); f.SelectAll();
        var pending = f.Run();
        Assert.StartsWith("Application : z\n", f.Vm.ValidationDetails);
        await NextValidation(f.Vm, () => f.Vm.SkipDecisionCommand.Execute(null), "a");
        Assert.Equal(new[] { "Skip:z" }, f.Actions.Order);
        await NextValidation(f.Vm, () => f.Vm.ExecuteDecisionCommand.Execute(null), "b");
        Assert.Equal(new[] { "Skip:z", "Execute:a" }, f.Actions.Order);
        f.Vm.CancelDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(new[] { "Skip:z", "Execute:a", "Cancel:b" }, f.Actions.Order);
        Assert.Equal(new[] { UpdateItemStatus.Skipped, UpdateItemStatus.Completed, UpdateItemStatus.Cancelled }, f.Actions.Report!.Results.Select(r => r.Status));
        Assert.Contains("Séquence annulée par l'utilisateur.", f.Actions.Report.Errors);
        Assert.Equal(UpdateItemStatus.Available, f.Cards.Single(c => c.Item.Id == "b").Item.Status);
        Assert.Contains("Passées : 1", f.Vm.ReportText);
        Assert.Contains("Terminées : 1", f.Vm.ReportText);
        Assert.Contains("Annulation : oui", f.Vm.ReportText);
        Assert.Equal(2, f.Persisted);
        Assert.True(f.Vm.IsReportOpen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringExecutionAndBusyGuard(bool escape)
    {
        var f = new Fixture(); var executing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Actions.Handler = async (_, token) => { executing.SetResult(true); await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); };
        await f.Analyze(); f.SelectAll(); var pending = f.Run();
        f.Vm.ExecuteDecisionCommand.Execute(null); await executing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Vm.OpenWindowsUpdateCommand.ExecuteAsync(null);
        Assert.Equal("Opération déjà en cours.", f.Messages.Last());
        await f.Vm.InspectDriversCommand.ExecuteAsync(null);
        Assert.Equal("Opération déjà en cours.", f.Messages.Last());
        if (escape) { Assert.True(f.Vm.TryCloseOverlay()); Assert.Contains("Annulation demandée.\nSequence en cours d'arrêt.", f.Messages); }
        else f.Vm.CancelActiveOperation();
        await pending;
        Assert.Contains("Séquence annulée.", f.Actions.Report!.Errors);
        Assert.Equal("Séquence annulée.", f.Vm.StatusText);
        Assert.Equal(VirgilCoreState.Idle, f.Vm.MiniCoreState);
        Assert.Equal(2, f.Persisted);
        Assert.True(f.Vm.IsReportOpen);
    }

    [Theory]
    [InlineData("ExecuteDecision", 0)]
    [InlineData("SkipDecision", 0)]
    [InlineData("CancelDecision", 0)]
    [InlineData("TryCloseOverlay", 0)]
    [InlineData("CancelActiveOperation", 0)]
    [InlineData("ExecuteDecision", 1)]
    [InlineData("ExecuteDecision", 2)]
    public async Task WindowsUpdateOnlyLaunchesAfterOpen(string decision, int failure)
    {
        var f = new Fixture(); f.Runner.Failure = failure; await f.Analyze();
        var pending = f.Vm.OpenWindowsUpdateCommand.ExecuteAsync(null);
        Assert.Equal(0, f.Runner.Calls);
        Assert.False(f.Vm.IsBusy);
        Assert.True(f.Vm.IsValidationOpen);
        Assert.Equal("OUVRIR WINDOWS UPDATE", f.Vm.ValidationTitle);
        Assert.Equal("A jour\nRedemarrage en attente : oui\nURI : ms-settings:windowsupdate", f.Vm.ValidationDetails);
        Assert.Equal("Ouverture des Paramètres Windows uniquement.", f.Vm.ValidationCommand);
        Assert.Equal("Virgil n'appuie sur aucun bouton Windows Update.\nAucune recherche, installation ou redémarrage n'est declenche.", f.Vm.ValidationSafety);
        Assert.Equal("OUVRIR", f.Vm.ExecuteLabel);
        Assert.Equal("ANNULER", f.Vm.SkipLabel);
        Assert.False(f.Vm.IsCancelSequenceVisible);
        Decide(f.Vm, decision); await pending;
        Assert.False(f.Vm.IsValidationOpen);
        Assert.Equal(decision == "ExecuteDecision" ? 1 : 0, f.Runner.Calls);
        Assert.Equal(decision != "ExecuteDecision" ? "Windows Update non ouvert." : failure != 0 ? "Ouverture Windows Update impossible." : "Windows Update ouvert.\nVirgil ne lancé aucune installation.", f.Messages.Last());
        if (f.Runner.Calls != 0)
        {
            Assert.Equal("ms-settings:windowsupdate", f.Runner.Request!.FileName);
            Assert.True(f.Runner.Request.UseShellExecute);
            Assert.Empty(f.Runner.Request.Arguments);
            Assert.False(f.Runner.Token.CanBeCanceled);
        }
        Assert.Empty(f.Actions.Order);
    }

    [Fact]
    public async Task DriversMergePreservesAppsAndDeduplicatesReportLists()
    {
        var f = new Fixture(); f.Scan.Report = Report(Item("app")) with { Errors = new[] { "Commun" }, Recommendations = new[] { "Conseil" } };
        await f.Analyze(); f.SelectAll();
        f.Scan.Report = new() { WindowsUpdate = new() { Status = "Pilotes lus" }, Drivers = new() { WasAnalyzed = true, CanInstallDrivers = true, InstallButtonVisibilityReason = "Lecture seule", Errors = new[] { "1", "2", "3", "4" }, Drivers = Enumerable.Range(1, 10).Select(i => new DriverInformation { PublishedName = i == 1 ? "" : $"oem{i}" }).ToArray() }, Errors = new[] { "COMMUN", "Autre" }, Recommendations = new[] { "CONSEIL", "Autre conseil" } };
        await f.Vm.InspectDriversCommand.ExecuteAsync(null);
        Assert.False(f.Scan.Request!.IncludeApplicationUpdates);
        Assert.True(f.Scan.Request.IncludeDriverInventory);
        Assert.Equal(UpdateScanScope.DeepPreview, f.Scan.Request.Scope);
        Assert.Equal("app", Assert.Single(f.Cards).Item.Id);
        Assert.False(f.Cards.Single().IsSelected);
        Assert.True(f.Vm.IsInstallDriversVisible);
        Assert.True(f.Vm.IsInstallDriversEnabled);
        Assert.Equal("10 pilotes inventoriés. Mises à jour facultatives dans « Tout mettre à jour ».", f.Vm.DriversStatus);
        Assert.Equal("Pilotes lus", f.Vm.WindowsUpdateStatus);
        Assert.Equal(12, f.Vm.DriverCards.Count);
        Assert.Equal(new[] { "Lecture seule", "Erreur lisible : 1", "Erreur lisible : 2", "Erreur lisible : 3" }, f.Vm.DriverCards.Take(4));
        Assert.Equal("Pilote\nFournisseur : Inconnu\nClasse : Inconnu\nVersion : Inconnu\nSignataire : Inconnu".Replace("Inconnu", "N/A"), f.Vm.DriverCards[4]);
        Assert.Contains("oem8", f.Vm.DriverCards.Last());
        Assert.Equal(VirgilCoreState.Warning, f.Vm.MiniCoreState);
        Assert.Equal("Inventaire pilotes terminé.\nLecture seule.", f.Messages.Last());
        f.Vm.ViewReportCommand.Execute(null);
        Assert.DoesNotContain("COMMUN", f.Vm.ReportText);
        Assert.Contains("- Commun", f.Vm.ReportText);
        Assert.Contains("- Autre conseil", f.Vm.ReportText);
        Assert.Equal(2, f.Persisted);
        Assert.True(f.Vm.TryCloseOverlay());
        Assert.False(f.Vm.IsReportOpen);
        Assert.False(f.Vm.TryCloseOverlay());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DriverInventoryFailureTexts(bool cancelled)
    {
        var f = new Fixture(); f.Scan.Handler = _ =>
        {
            if (cancelled) throw new OperationCanceledException();
            throw new InvalidOperationException();
        };
        await f.Vm.InspectDriversCommand.ExecuteAsync(null);
        Assert.Equal(cancelled ? "Inventaire pilotes annulé." : "Inventaire pilotes indisponible.", f.Vm.StatusText);
        Assert.Equal(cancelled ? "Inventaire annulé.\nAucune action effectuée." : "Inventaire indisponible.\nAucune action effectuée.", f.Messages.Last());
        Assert.Equal(cancelled ? VirgilCoreState.Idle : VirgilCoreState.Error, f.Vm.MiniCoreState);
        Assert.Equal(0, f.Persisted); Idle(f.Vm);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "Historique inaccessible.")]
    [InlineData(true, "Avertissement historique.")]
    public async Task PersistenceResultAndEvent(bool success, string? error)
    {
        var f = new Fixture(); await f.Analyze();
        f.History.Result = new() { Success = success, ReadableError = error };
        f.SelectAll(); var pending = f.Run(); f.Vm.SkipDecisionCommand.Execute(null); await pending;
        Assert.Contains(error ?? "Historique local indisponible. Rapport conservé en mémoire.", f.Messages);
        Assert.Equal(success ? 2 : 1, f.Persisted);
        Assert.True(f.Vm.IsReportOpen);
        Assert.True(f.Vm.IsReportButtonEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecutionFailureAndErrorProduceWarningReport(bool throws)
    {
        var f = new Fixture(); f.Actions.Handler = (item, _) => throws ? throw new InvalidOperationException() : Task.FromResult(new UpdateExecutionResult { Item = item, Status = UpdateItemStatus.Failed, UserMessage = "Refus" });
        await f.Analyze(); f.SelectAll(); var pending = f.Run(); f.Vm.ExecuteDecisionCommand.Execute(null); await pending;
        Assert.Equal("Séquence terminée avec erreurs.", f.Vm.StatusText);
        Assert.Equal(VirgilCoreState.Warning, f.Vm.MiniCoreState);
        Assert.True(f.Vm.IsReportOpen); Assert.Equal(2, f.Persisted);
        Assert.Contains(throws ? "Erreur pendant la séquence guidée." : "Refus", f.Vm.ReportText);
    }

    [Fact]
    public async Task ReportHasPriorityOverValidationAndRepeatedDecisionDoesNothing()
    {
        var f = new Fixture(); await f.Analyze(); f.SelectAll(); var pending = f.Run();
        f.Vm.ViewReportCommand.Execute(null);
        Assert.True(f.Vm.TryCloseOverlay()); Assert.True(f.Vm.IsValidationOpen); Assert.False(pending.IsCompleted);
        Assert.True(f.Vm.TryCloseOverlay()); await pending;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        Assert.Equal(new[] { "Skip:app" }, f.Actions.Order);
        f.Vm.CloseReportCommand.Execute(null); Assert.False(f.Vm.TryCloseOverlay());
    }

    [Fact]
    public async Task EntriesNeverContainRawStringsAcrossRenderingStates()
    {
        var f = new Fixture();
        Assert.DoesNotContain(f.Vm.Entries, entry => entry is string);
        Assert.IsType<UpdateTextCardViewModel>(Assert.Single(f.Vm.Entries));

        f.Scan.Report = Report();
        await f.Analyze();
        Assert.DoesNotContain(f.Vm.Entries, entry => entry is string);
        Assert.IsType<UpdateTextCardViewModel>(Assert.Single(f.Vm.Entries));

        f.Scan.Report = Report(Item("app"));
        await f.Analyze();
        Assert.DoesNotContain(f.Vm.Entries, entry => entry is string);
        Assert.IsType<UpdateItemViewModel>(Assert.Single(f.Vm.Entries));
        f.SelectAll();
        var pending = f.Run();
        f.Vm.SkipDecisionCommand.Execute(null);
        await pending;
        Assert.DoesNotContain(f.Vm.Entries, entry => entry is string);
        Assert.IsType<UpdateItemViewModel>(Assert.Single(f.Vm.Entries));
    }

    [Fact]
    public void UpdatesViewScopesTextCardTemplatesToTheirLists()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Virgil.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var xaml = File.ReadAllText(Path.Combine(directory.FullName, "src", "Virgil.App", "Views", "UpdatesView.xaml"));
        Assert.DoesNotMatch(@"<DataTemplate\b[^>]*\bDataType\s*=\s*[""'][^""']*\bString\b", xaml);

        var document = XDocument.Parse(xaml);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var templates = document.Descendants(presentation + "DataTemplate").ToArray();
        var driversTemplate = Assert.Single(templates, t => (string?)t.Attribute(x + "Key") == "UpdatesTextCardTemplate");
        Assert.Null(driversTemplate.Attribute("DataType"));
        var drivers = Assert.Single(document.Descendants(presentation + "ItemsControl"), t => (string?)t.Attribute(x + "Name") == "DriversListPanel");
        Assert.Equal("{StaticResource UpdatesTextCardTemplate}", (string?)drivers.Attribute("ItemTemplate"));
        var textTemplate = Assert.Single(templates, t => (string?)t.Attribute("DataType") == "{x:Type vm:UpdateTextCardViewModel}");
        foreach (var template in new[] { driversTemplate, textTemplate })
        {
            var border = Assert.Single(template.Elements(presentation + "Border"));
            Assert.Equal("{StaticResource VirgilHudCard}", (string?)border.Attribute("Style"));
            Assert.Equal("0,0,0,8", (string?)border.Attribute("Margin"));
            var text = Assert.Single(border.Elements(presentation + "TextBlock"));
            Assert.Equal("{StaticResource VirgilHudSecondaryText}", (string?)text.Attribute("Style"));
            Assert.Equal("Wrap", (string?)text.Attribute("TextWrapping"));
        }
        Assert.Equal("{Binding Text}", (string?)textTemplate.Descendants(presentation + "TextBlock").Single().Attribute("Text"));
        Assert.Equal("{Binding}", (string?)driversTemplate.Descendants(presentation + "TextBlock").Single().Attribute("Text"));
    }

    private static void Idle(UpdatesViewModel vm) { Assert.True(vm.IsIdle); Assert.False(vm.IsBusy); }
    private static void Decide(UpdatesViewModel vm, string decision)
    {
        switch (decision)
        {
            case "ExecuteDecision": vm.ExecuteDecisionCommand.Execute(null); break;
            case "SkipDecision": vm.SkipDecisionCommand.Execute(null); break;
            case "CancelDecision": vm.CancelDecisionCommand.Execute(null); break;
            case "TryCloseOverlay": Assert.True(vm.TryCloseOverlay()); break;
            case "CancelActiveOperation": vm.CancelActiveOperation(); break;
            default: throw new ArgumentOutOfRangeException(nameof(decision));
        }
    }
    private static async Task NextValidation(UpdatesViewModel vm, Action decide, string id)
    {
        var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        { if (args.PropertyName == nameof(vm.IsValidationOpen) && vm.IsValidationOpen && vm.ValidationDetails.StartsWith($"Application : {id}\n")) opened.TrySetResult(true); }
        vm.PropertyChanged += Changed;
        try { decide(); await opened.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { vm.PropertyChanged -= Changed; }
    }
    [Fact]
    public async Task SkipExecuteCancelIssuesOnePlanAndRunsOneUpgrade()
    {
        var f = new Fixture(); var runner = new UpgradeRunner();
        f.Actions.Executor = new WingetUpdateExecutionService(new WingetAvailabilityService(runner, ["winget"]), runner, f.Confirmation.Authority);
        f.Scan.Report = Report(Item("z"), Item("a"), Item("b"));
        await f.Analyze(); f.SelectAll(); var pending = f.Run();
        Assert.Equal(0, f.Confirmation.Calls);
        await NextValidation(f.Vm, () => f.Vm.SkipDecisionCommand.Execute(null), "a");
        Assert.Equal(0, f.Confirmation.Calls);
        await NextValidation(f.Vm, () => f.Vm.ExecuteDecisionCommand.Execute(null), "b");
        f.Vm.CancelDecisionCommand.Execute(null); await pending;
        Assert.Equal(1, f.Confirmation.Calls); Assert.Equal(1, runner.Upgrades);
        Assert.Equal(new[] { "Skip:z", "Execute:a", "Cancel:b" }, f.Actions.Order);
    }

    [Fact]
    public async Task ConfirmationRefusalUsesExistingCancelAllPath()
    {
        var f = new Fixture(); f.Confirmation.Refuse = true;
        await f.Analyze(); f.SelectAll(); var pending = f.Run();
        f.Vm.ExecuteDecisionCommand.Execute(null); await pending;
        Assert.Equal(1, f.Confirmation.Calls); Assert.Empty(f.Actions.Plans);
        Assert.Equal(new[] { "Cancel:app" }, f.Actions.Order);
        Assert.Equal("Séquence annulée.", f.Vm.StatusText);
    }

    private sealed class UpgradeRunner : IProcessRunner
    {
        public int Upgrades { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        { if (request.Arguments[0] == "upgrade") Upgrades++; return Task.FromResult(new ProcessRunResult(0, "v1.8.0", "")); }
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private static UpdateItem Item(string id, UpdateRiskLevel risk = UpdateRiskLevel.ValidationRequired) => new() { Id = id, Name = id, InstalledVersion = "1", AvailableVersion = "2", RiskLevel = risk, RiskReason = "Motif", CommandPreview = new() { ExecutablePath = "winget", Arguments = new[] { "upgrade", "app test" } } };
    private static UpdateScanReport Report(params UpdateItem[] items) => new() { Items = items, OverallStatus = "Pret", Winget = new() { IsAvailable = true, Message = "WinGet disponible" }, WindowsUpdate = new() { Status = "A jour", PendingRebootDetected = true } };
    private sealed class Fixture
    {
        public RecordingConfirmation Confirmation { get; } = new();
        public FakeScan Scan { get; } = new(); public FakeActions Actions { get; } = new(); public FakeHistory History { get; } = new(); public FakeRunner Runner { get; } = new();
        public UpdatesViewModel Vm { get; }
        public List<string> Messages { get; } = new(); public List<(VirgilCoreState State, string Label)> States { get; } = new(); public int Persisted { get; private set; }
        public IEnumerable<UpdateItemViewModel> Cards => Vm.Entries.OfType<UpdateItemViewModel>();
        public Fixture() { Vm = new(Scan, Actions, new WindowsUpdateStatusService(), Runner, History, Confirmation); Vm.VirgilMessageRequested += Messages.Add; Vm.VirgilStateRequested += (s, l) => States.Add((s, l)); Vm.ReportPersisted += (_, _) => Persisted++; }
        public Task Analyze() => Vm.ScanUpdatesCommand.ExecuteAsync(null);
        public Task Run() => Vm.RunSelectedCommand.ExecuteAsync(null);
        public void SelectAll() { foreach (var card in Cards.Where(c => c.IsSelectable)) card.IsSelected = true; }
    }
    private sealed class FakeScan : IUpdateScanService
    {
        public UpdateScanReport Report { get; set; } = UpdatesViewModelTests.Report(Item("app")); public UpdateScanRequest? Request { get; private set; }
        public Func<CancellationToken, Task<UpdateScanReport>>? Handler { get; set; }
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken cancellationToken) { Request = request; return Handler?.Invoke(cancellationToken) ?? Task.FromResult(Report); }
    }
    private sealed class FakeActions : IUpdateExecutionService
    {
        public IUpdateExecutionService? Executor { get; set; }
        public List<ConfirmedPlan> Plans { get; } = new();
        public List<string> Order { get; } = new(); public UpdateSessionReport? Report { get; private set; }
        public Func<UpdateItem, CancellationToken, Task<UpdateExecutionResult>>? Handler { get; set; }
        public Task<UpdateExecutionResult> ExecuteAsync(UpdateItem item, ConfirmedPlan? plan, CancellationToken cancellationToken) { Assert.NotNull(plan); Plans.Add(plan); Order.Add("Execute:" + item.Id); return Executor?.ExecuteAsync(item, plan, cancellationToken) ?? Handler?.Invoke(item, cancellationToken) ?? Task.FromResult(Result(item, UpdateItemStatus.Completed)); }
        public UpdateExecutionResult Skip(UpdateItem item) { Order.Add("Skip:" + item.Id); return Result(item, UpdateItemStatus.Skipped); }
        public UpdateExecutionResult Cancel(UpdateItem item) { Order.Add("Cancel:" + item.Id); return Result(item, UpdateItemStatus.Cancelled); }
        private static UpdateExecutionResult Result(UpdateItem item, UpdateItemStatus status) => new() { Item = item, Status = status };
        public UpdateSessionReport CreateReport(DateTimeOffset startedAt, IReadOnlyList<UpdateExecutionResult> results, IReadOnlyList<string> errors) { Report = new() { StartedAt = startedAt, Results = results.ToArray(), Errors = errors.ToArray() }; return Report; }
    }
    private sealed class FakeRunner : IProcessRunner
    {
        public int Calls { get; private set; } public int Failure { get; set; } public ProcessLaunchRequest? Request { get; private set; } public CancellationToken Token { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException("Aucune execution processus attendue.");
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken) { Calls++; Request = request; Token = cancellationToken; if (Failure == 2) throw new InvalidOperationException(); return Task.FromResult(new ProcessLaunchResult(Failure == 0, null, Failure == 1 ? "Erreur" : null)); }
    }
    private sealed class FakeHistory : IReportHistoryService
    {
        public ReportSaveResult Result { get; set; } = new() { Success = true }; public CancellationToken Token { get; private set; }
        public Task<ReportSaveResult> SaveAsync(ReportEntry report, CancellationToken cancellationToken) { Token = cancellationToken; return Task.FromResult(Result); }
        public Task<ReportHistoryLoadResult> LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetLatestAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
