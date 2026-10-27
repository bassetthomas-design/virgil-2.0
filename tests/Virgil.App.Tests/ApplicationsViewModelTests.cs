using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using System.Xml.Linq;
using Virgil.App.Animation;
using Virgil.App.Services;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Contracts.Applications;
using Virgil.Core.Applications;
using Virgil.Core.Processes;
using Virgil.Core.Reports;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class ApplicationsViewModelTests
{
    [Theory]
    [InlineData(InstallationLocationOpenResult.Missing, "Emplacement introuvable.")]
    [InlineData(InstallationLocationOpenResult.Opened, "Emplacement ouvert.\nAucune suppression executee.")]
    [InlineData(InstallationLocationOpenResult.Failed, "Ouverture emplacement impossible.")]
    public async Task LocationUsesInjectedPortWithoutAccessingRealFileSystem(
        InstallationLocationOpenResult result, string message)
    {
        var location = new FakeLocation { Result = result };
        var f = new Fixture(location: location);
        const string path = @"Z:\virtual-only\application";
        f.Inventory.Report = new() { Applications = [Safe() with { InstallLocation = path }] };
        await f.Analyze();
        f.OpenDetails();
        await f.Vm.OpenApplicationLocationCommand.ExecuteAsync(null);
        Assert.Equal(path, Assert.Single(location.Paths));
        Assert.Contains(message, f.Messages);
        Assert.Empty(f.Runner.Requests);
        Assert.Empty(f.Dialog.Calls);
    }

    private sealed class FakeLocation : IInstallationLocationService
    {
        public InstallationLocationOpenResult Result { get; init; }
        public List<string> Paths { get; } = new();
        public Task<InstallationLocationOpenResult> OpenAsync(string path, CancellationToken cancellationToken)
        {
            Paths.Add(path);
            Assert.False(cancellationToken.CanBeCanceled);
            return Task.FromResult(Result);
        }
    }

    [Fact]
    public void InitialStateIsReadOnlyAndHasNoSelection()
    {
        var f = new Fixture();
        Assert.Equal("Inventaire prêt. Sélectionnez les applications à désinstaller.", f.Vm.StatusText);
        Assert.Equal("Applications", f.Vm.SelectedFilter);
        Assert.Empty(f.Vm.SearchText);
        Assert.False(f.Vm.IsBusy);
        Assert.True(f.Vm.IsIdle);
        Assert.False(f.Vm.IsReportButtonEnabled);
        Assert.False(f.Vm.IsLaunchUninstallEnabled);
        Assert.False(f.Vm.IsOpenApplicationLocationEnabled);
        Assert.False(f.Vm.IsDetailsOpen);
        Assert.False(f.Vm.IsReportOpen);
        Assert.False(f.Vm.TryCloseOverlay());
        Assert.Equal("Lancez un inventaire. Aucune action ne sera executee.", Assert.IsType<ApplicationTextCardViewModel>(Assert.Single(f.Vm.Entries)).Text);
        Assert.Empty(f.Runner.Requests);
    }

    [Fact]
    public void ConstructorRequiresEveryDependency()
    {
        var f = new Fixture();
        Assert.Throws<ArgumentNullException>(() => new ApplicationsViewModel(f.Inventory, f.Uninstall, f.Runner, f.History, f.Dialog, f.Confirmations, null!));
        Assert.Throws<ArgumentNullException>(() => new ApplicationsViewModel(null!, f.Uninstall, f.Runner, f.History, f.Dialog, f.Confirmations, f.Location));
        Assert.Throws<ArgumentNullException>(() => new ApplicationsViewModel(f.Inventory, null!, f.Runner, f.History, f.Dialog, f.Confirmations, f.Location));
        Assert.Throws<ArgumentNullException>(() => new ApplicationsViewModel(f.Inventory, f.Uninstall, null!, f.History, f.Dialog, f.Confirmations, f.Location));
        Assert.Throws<ArgumentNullException>(() => new ApplicationsViewModel(f.Inventory, f.Uninstall, f.Runner, null!, f.Dialog, f.Confirmations, f.Location));
        Assert.Throws<ArgumentNullException>(() => new ApplicationsViewModel(f.Inventory, f.Uninstall, f.Runner, f.History, null!, f.Confirmations, f.Location));
    }

    [Fact]
    public async Task InventoryRendersAndPersistsWithoutLaunching()
    {
        var f = new Fixture();
        await f.Analyze();
        Assert.Equal("Inventaire termine.", f.Vm.StatusText);
        Assert.Equal("1 detectees, 1 desinstallables, 0 avertissement(s).", f.Vm.InventorySummary);
        Assert.Equal("1 simples, 0 attention, 0 protegees, 0 inconnues.", f.Vm.RiskSummary);
        var card = Assert.Single(f.Cards);
        Assert.Equal(f.Inventory.Report.Applications[0].DisplayName, card.DisplayName);
        Assert.Contains("Editeur :", card.Lines);
        Assert.True(f.Vm.IsReportButtonEnabled);
        Assert.Single(f.History.Reports);
        Assert.Equal(1, f.Persisted);
        Assert.False(f.History.Token.CanBeCanceled);
        Assert.Contains((VirgilCoreState.Success, "APPLICATIONS"), f.States);
        Assert.Empty(f.Dialog.Calls);
        Assert.Empty(f.Runner.Requests);
        Idle(f.Vm);
    }

    [Fact]
    public async Task InventoryWarningsAndEmptyInventoryRemainReportable()
    {
        var f = new Fixture();
        f.Inventory.Report = new() { Errors = ["Source indisponible."] };
        await f.Analyze();
        Assert.Equal("Aucune application inventoriee.", Assert.IsType<ApplicationTextCardViewModel>(Assert.Single(f.Vm.Entries)).Text);
        Assert.Contains((VirgilCoreState.Warning, "APPLICATIONS"), f.States);
        f.Vm.ViewReportCommand.Execute(null);
        Assert.Equal("Source indisponible.", f.Vm.ReportErrors);
        Assert.True(f.Vm.IsReportOpen);
    }

    [Fact]
    public async Task CancelInventoryReleasesBusyStateWithoutReportOrLaunch()
    {
        var f = new Fixture();
        f.Inventory.Handler = async token => { await Task.Delay(Timeout.Infinite, token); return new(); };
        var analyze = f.Analyze();
        Assert.True(f.Vm.IsBusy);
        Assert.False(f.Vm.IsLaunchUninstallEnabled);
        Assert.True(f.Vm.TryCloseOverlay());
        await analyze;
        Assert.Equal("Inventaire annule.", f.Vm.StatusText);
        Assert.Contains("Inventaire applications annule.\nAucune action effectuee.", f.Messages);
        Assert.Empty(f.History.Reports);
        Assert.Empty(f.Runner.Requests);
        Idle(f.Vm);
    }

    [Fact]
    public async Task InventoryExceptionKeepsTheViewUsable()
    {
        var f = new Fixture();
        f.Inventory.Handler = _ => throw new InvalidOperationException("Inventaire test.");
        await f.Analyze();
        Assert.Equal("Inventaire applications indisponible.", f.Vm.StatusText);
        Assert.Contains("Inventaire applications indisponible.\nAucune action effectuee.", f.Messages);
        Assert.Contains((VirgilCoreState.Error, "ERREUR"), f.States);
        Assert.Empty(f.History.Reports);
        Idle(f.Vm);
    }

    [Fact]
    public async Task InventoryProgressIsObservableWhileBusy()
    {
        var f = new Fixture();
        f.Inventory.Handler = async token => { await Task.Delay(Timeout.Infinite, token); return new(); };
        var analyze = f.Analyze();
        Assert.True(f.Vm.IsBusy);
        f.Inventory.Progress!.Report(new() { Percent = 37, Status = "Source test" });
        Assert.Equal(37, f.Vm.Progress);
        Assert.Equal("Source test 37 %", f.Vm.StatusText);
        f.Vm.CancelActiveOperation();
        await analyze;
        Idle(f.Vm);
    }

    [Theory]
    [InlineData("  MEDIA  ", "TOUT", 1)]
    [InlineData("example.publisher", "TOUT", 1)]
    [InlineData("videolan.vlc", "TOUT", 1)]
    [InlineData("", "DESINSTALLABLE", 2)]
    [InlineData("", "ATTENTION", 1)]
    [InlineData("", "PROTEGE", 1)]
    [InlineData("", "INCONNU", 1)]
    [InlineData("media", "ATTENTION", 0)]
    public async Task SearchAndFilterCombineWithoutAddingSelection(string search, string filter, int count)
    {
        var f = new Fixture();
        f.Inventory.Report = new() { Applications = [Safe(), Safe() with { Id = "caution", DisplayName = "Project Tool", WingetId = "Example.ProjectTool", Publisher = "Other", RiskLevel = ApplicationRiskLevel.Caution }, Safe() with { Id = "protected", DisplayName = "Runtime", Publisher = "Other", WingetId = null, CanUninstall = false, RiskLevel = ApplicationRiskLevel.Protected }, Safe() with { Id = "unknown", DisplayName = "Unknown", Publisher = "Other", WingetId = null, CanUninstall = false, RiskLevel = ApplicationRiskLevel.Unknown }] };
        await f.Analyze();
        f.Vm.SearchText = search;
        f.Vm.SelectedFilter = filter;
        Assert.Equal(count, f.Cards.Count());
        Assert.Equal(count, f.Vm.FilteredApplications().Count());
        if (count == 0) Assert.Equal("Aucune application ne correspond au filtre.", Assert.IsType<ApplicationTextCardViewModel>(Assert.Single(f.Vm.Entries)).Text);
        Assert.False(f.Vm.IsDetailsOpen);
        Assert.Empty(f.Dialog.Calls);
        Assert.Empty(f.Runner.Requests);
    }

    [Fact]
    public async Task DetailsAndEscapeNeverLaunchAndClearTheCurrentSelection()
    {
        var f = new Fixture();
        await f.Analyze();
        f.OpenDetails();
        Assert.True(f.Vm.IsDetailsOpen);
        Assert.Equal(Safe().DisplayName, f.Vm.DetailsTitle);
        Assert.Contains("Architecture :", f.Vm.DetailsBody);
        Assert.Equal("winget uninstall --id VideoLAN.VLC --exact", f.Vm.DetailsCommand);
        Assert.Contains("Confirmation explicite obligatoire", f.Vm.DetailsSafety);
        Assert.Equal("Disponibles apres lancement du desinstallateur officiel. Analyse lecture seule uniquement.", Assert.Single(f.Vm.Remnants).Text);
        Assert.True(f.Vm.TryCloseOverlay());
        Assert.False(f.Vm.IsDetailsOpen);
        await f.Launch();
        Assert.Contains("Aucune application selectionnee.\nOuvrez les details d'une application avant de lancer une action.", f.Messages);
        Assert.Empty(f.Dialog.Calls);
        Assert.Empty(f.Runner.Requests);
        Assert.False(f.Vm.TryCloseOverlay());
    }

    [Fact]
    public async Task ReportClosesBeforeDetailsAndReturnsHomeThroughEvent()
    {
        var f = new Fixture();
        f.Vm.ViewReportCommand.Execute(null);
        Assert.Equal("Aucun rapport applications disponible.", f.Vm.ReportSummary);
        f.Vm.CloseReportCommand.Execute(null);
        await f.Analyze();
        f.OpenDetails();
        f.Vm.ViewReportCommand.Execute(null);
        Assert.Equal("1 application(s), 1 desinstallable(s).", f.Vm.ReportSummary);
        Assert.Contains("Aucune action exécutée depuis l'inventaire.", f.Vm.ReportDetails);
        Assert.Equal("Aucune", f.Vm.ReportErrors);
        Assert.True(f.Vm.TryCloseOverlay());
        Assert.False(f.Vm.IsReportOpen);
        Assert.True(f.Vm.IsDetailsOpen);
        f.Vm.CloseDetailsCommand.Execute(null);
        Assert.False(f.Vm.TryCloseOverlay());
        var returned = false;
        f.Vm.ReturnHomeRequested += (_, _) => returned = true;
        f.Vm.ReturnHomeCommand.Execute(null);
        Assert.True(returned);
    }

    [Theory]
    [InlineData(false, null, 0)]
    [InlineData(false, "Historique test indisponible.", 0)]
    [InlineData(true, "Avertissement historique.", 1)]
    public async Task PersistenceFailureIsReadableAndRetainsReport(bool success, string? error, int persisted)
    {
        var f = new Fixture();
        f.History.Result = new() { Success = success, ReadableError = error };
        await f.Analyze();
        Assert.Equal(persisted, f.Persisted);
        Assert.Contains(error ?? "Historique local indisponible. Rapport conserve en memoire.", f.Messages);
        f.Vm.ViewReportCommand.Execute(null);
        Assert.Equal("1 application(s), 1 desinstallable(s).", f.Vm.ReportSummary);
        Idle(f.Vm);
    }

    [Fact]
    public async Task ExplicitNoDoesNotBeginOperationOrLaunchAnyProcess()
    {
        var f = new Fixture();
        await f.Analyze();
        f.OpenDetails();
        f.Dialog.Answers.Enqueue(false);
        var busy = new List<bool>();
        f.Vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(f.Vm.IsBusy)) busy.Add(f.Vm.IsBusy); };
        await f.Launch();
        var call = Assert.Single(f.Dialog.Calls);
        Assert.Equal("Confirmation desinstallation", call.Title);
        Assert.Contains("Aucune donnee personnelle ne sera supprimee automatiquement.", call.Message);
        Assert.Empty(busy);
        Assert.Empty(f.Runner.Requests);
        Assert.Single(f.History.Reports);
        Assert.Contains("Desinstallation annulee.\nConfirmation explicite absente.", f.Messages);
        Idle(f.Vm);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task CautionRequiresBothAnswersBeforeLaunching(bool reinforced, int launches)
    {
        var f = new Fixture();
        f.Inventory.Report = new() { Applications = [Safe() with { RiskLevel = ApplicationRiskLevel.Caution }] };
        await f.Analyze();
        f.OpenDetails();
        Assert.Equal("LANCER AVEC VALIDATION RENFORCEE", f.Vm.LaunchUninstallLabel);
        f.Dialog.Answers.Enqueue(true);
        f.Dialog.Answers.Enqueue(reinforced);
        f.Dialog.OnConfirm = () => { Assert.Empty(f.Runner.Requests); Assert.False(f.Vm.IsBusy); };
        await f.Launch();
        Assert.Equal(2, f.Dialog.Calls.Count);
        Assert.Equal("Confirmation renforcee applications", f.Dialog.Calls[1].Title);
        Assert.Contains("profils, projets, presets, bibliotheques ou sauvegardes", f.Dialog.Calls[1].Message);
        Assert.Equal(launches, f.Runner.Requests.Count);
        if (reinforced)
        {
            var request = Assert.Single(f.Runner.Requests);
            Assert.Equal("winget", request.FileName);
            Assert.Equal(new[] { "uninstall", "--id", "VideoLAN.VLC", "--exact" }, request.Arguments);
            Assert.True(request.UseShellExecute);
            Assert.Equal(2, f.Persisted);
            f.Vm.ViewReportCommand.Execute(null);
            Assert.Contains("Confirmation explicite : oui", f.Vm.ReportDetails);
            Assert.Contains("Confirmation renforcee : oui", f.Vm.ReportDetails);
            Assert.Contains("Lance : oui", f.Vm.ReportDetails);
        }
        else
        {
            Assert.Equal(0, f.Runner.Started);
            Assert.Equal("Desinstallation annulee.\nConfirmation renforcee absente pour une application classee attention.", f.Messages.Last());
        }
        Idle(f.Vm);
    }

    [Theory]
    [InlineData("protected")]
    [InlineData("sensitive")]
    [InlineData("store")]
    [InlineData("cannot")]
    [InlineData("invalid-command")]
    public async Task BlockedApplicationsNeverOpenDialogOrLaunch(string kind)
    {
        var app = kind switch
        {
            "protected" => Safe() with { RiskLevel = ApplicationRiskLevel.Protected },
            "sensitive" => Safe() with { DisplayName = "Security Driver Runtime", RiskLevel = ApplicationRiskLevel.Protected, CanUninstall = false },
            "store" => Safe() with { UninstallKind = ApplicationUninstallKind.StoreSettings },
            "cannot" => Safe() with { CanUninstall = false },
            _ => Safe() with { WingetId = "" }
        };
        var f = new Fixture();
        f.Inventory.Report = new() { Applications = [app] };
        await f.Analyze();
        f.OpenDetails();
        await f.Launch();
        Assert.Empty(f.Dialog.Calls);
        Assert.Empty(f.Runner.Requests);
        Assert.Equal("Desinstallation bloquee.", f.Vm.StatusText);
        Idle(f.Vm);
    }

    [Fact]
    public async Task SafeExplicitYesLaunchesOnceAndPersistsReadOnlyRemnants()
    {
        var f = new Fixture();
        await f.Analyze();
        f.OpenDetails();
        f.Dialog.Answers.Enqueue(true);
        await f.Launch();
        Assert.Single(f.Dialog.Calls);
        Assert.Single(f.Runner.Requests);
        Assert.Equal(2, f.History.Reports.Count);
        Assert.Equal(2, f.Persisted);
        Assert.True(f.Vm.IsDetailsOpen);
        Assert.All(f.Vm.Remnants, r => Assert.True(r.Text.Contains("lecture seule", StringComparison.Ordinal) || r.Text.Contains("Action automatique : aucune.", StringComparison.Ordinal)));
        f.Vm.ViewReportCommand.Execute(null);
        Assert.Contains("Confirmation explicite : oui", f.Vm.ReportDetails);
        Assert.Contains("Suppression automatique : aucune", f.Vm.ReportDetails);
        Assert.Contains("Desinstalleur officiel lance.", f.Vm.ReportSummary);
        Idle(f.Vm);
    }

    [Fact]
    public async Task ExistingRemnantIsListedWithoutDeletingItsContents()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Virgil-0d9-remnant-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "preserved.txt");
        await File.WriteAllTextAsync(file, "donnees conservees");
        try
        {
            var f = new Fixture();
            f.Inventory.Report = new() { Applications = [Safe() with { InstallLocation = directory }] };
            await f.Analyze();
            f.OpenDetails();
            f.Dialog.Answers.Enqueue(true);
            await f.Launch();
            Assert.Contains(f.Vm.Remnants, r => r.Text.Contains(directory, StringComparison.Ordinal) && r.Text.EndsWith("Action automatique : aucune.", StringComparison.Ordinal));
            Assert.Equal("donnees conservees", await File.ReadAllTextAsync(file));
            Assert.Single(f.Runner.Requests);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CancelDuringLaunchReportsExactCancellationAndNoStartedUninstaller()
    {
        var f = new Fixture();
        await f.Analyze();
        f.OpenDetails();
        f.Dialog.Answers.Enqueue(true);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Runner.Handler = async (_, token) =>
        {
            entered.SetResult(true);
            await Task.Delay(Timeout.Infinite, token);
            return new(false, null);
        };
        var launch = f.Launch();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(f.Vm.IsBusy);
        Assert.False(f.Vm.IsLaunchUninstallEnabled);
        Assert.False(f.Vm.IsReportButtonEnabled);
        f.Vm.CancelActiveOperation();
        await launch;
        Assert.Equal(0, f.Runner.Started);
        Assert.Equal("Desinstallation annulee.", f.Vm.StatusText);
        Assert.Contains("Desinstallation annulee.\nAucun desinstallateur lance.", f.Messages);
        Assert.Single(f.History.Reports);
        Idle(f.Vm);
    }

    [Fact]
    public async Task DialogExceptionDoesNotLaunchAndUsesExistingErrorText()
    {
        var f = new Fixture();
        await f.Analyze();
        f.OpenDetails();
        f.Dialog.OnConfirm = () => throw new InvalidOperationException("Dialogue test.");
        await f.Launch();
        Assert.Empty(f.Runner.Requests);
        Assert.Equal("Desinstallateur officiel indisponible.", f.Vm.StatusText);
        Assert.Contains("Desinstallateur officiel indisponible.\nErreur : Dialogue test.", f.Messages);
        Idle(f.Vm);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocationUsesExactExplorerRequest(bool file)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Virgil-0d9-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var location = file ? Path.Combine(directory, "app.txt") : directory;
        if (file) await File.WriteAllTextAsync(location, "test");
        try
        {
            var f = new Fixture();
            f.Inventory.Report = new() { Applications = [Safe() with { InstallLocation = location }] };
            await f.Analyze();
            f.OpenDetails();
            await f.Vm.OpenApplicationLocationCommand.ExecuteAsync(null);
            var request = Assert.Single(f.Runner.Requests);
            AssertRequest(request, "explorer.exe", file ? $"/select,\"{location}\"" : $"\"{location}\"");
            Assert.False(f.Runner.Token.CanBeCanceled);
            Assert.Contains("Emplacement ouvert.\nAucune suppression executee.", f.Messages);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(null, "Emplacement indisponible.")]
    [InlineData("Z:\\Virgil-nonexistent-0d9-7052\\app", "Emplacement introuvable.")]
    public async Task MissingLocationDoesNotLaunch(string? location, string message)
    {
        var f = new Fixture();
        f.Inventory.Report = new() { Applications = [Safe() with { InstallLocation = location }] };
        await f.Analyze();
        f.OpenDetails();
        await f.Vm.OpenApplicationLocationCommand.ExecuteAsync(null);
        Assert.Empty(f.Runner.Requests);
        Assert.Contains(message, f.Messages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocationLaunchFailureKeepsExactErrorText(bool throws)
    {
        var f = new Fixture();
        f.Inventory.Report = new() { Applications = [Safe() with { InstallLocation = Path.GetTempPath() }] };
        f.Runner.Handler = (_, _) => throws ? throw new InvalidOperationException("Lancement test.") : Task.FromResult(new ProcessLaunchResult(false, null, "Echec"));
        await f.Analyze();
        await Assert.Single(f.Cards).OpenLocationCommand.ExecuteAsync(null);
        Assert.Contains("Ouverture emplacement impossible.", f.Messages);
        Assert.Empty(f.Dialog.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StoreSettingsUsesExactRequestAndKeepsFailuresNonBlocking(bool throws)
    {
        var f = new Fixture();
        f.Inventory.Report = new() { Applications = [Safe() with { UninstallKind = ApplicationUninstallKind.StoreSettings, CanUninstall = false }] };
        f.Runner.Handler = (_, _) => throws ? throw new InvalidOperationException("Parametres test.") : Task.FromResult(new ProcessLaunchResult(true, null));
        await f.Analyze();
        f.Vm.SelectedFilter = "Store";
        f.Vm.SelectedFilter = "TOUT";
        var card = Assert.Single(f.Cards);
        Assert.True(card.IsLocationVisible);
        Assert.Equal("PARAMETRES", card.LocationLabel);
        f.OpenDetails();
        Assert.Equal("PARAMETRES WINDOWS", f.Vm.OpenApplicationLocationLabel);
        Assert.True(f.Vm.IsOpenApplicationLocationEnabled);
        var messages = f.Messages.Count;
        await f.Vm.OpenApplicationLocationCommand.ExecuteAsync(null);
        AssertRequest(Assert.Single(f.Runner.Requests), "ms-settings:appsfeatures", null);
        Assert.Equal(messages, f.Messages.Count);
        Assert.Empty(f.Dialog.Calls);
        Idle(f.Vm);
    }

    [Fact]
    public void XamlUsesTypedCardsAndBindingsWithoutImplicitStringTemplate()
    {
        var doc = ReadXaml();
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        Assert.DoesNotContain(doc.Descendants(p + "DataTemplate"), t => ((string?)t.Attribute("DataType"))?.Contains("String", StringComparison.Ordinal) == true);
        foreach (var name in new[] { "ApplicationsListPanel", "DetailsRemnantsPanel" })
            Assert.NotNull(Assert.Single(doc.Descendants(p + "ItemsControl"), e => (string?)e.Attribute(x + "Name") == name).Attribute("ItemsSource"));
        Assert.DoesNotContain(doc.Descendants(), e => e.Attribute("Click") is not null);
        var service = File.ReadAllText(Path.Combine(RepositoryRoot(), "src/Virgil.App/Services/ApplicationUninstallConfirmationDialogService.cs"));
        Assert.DoesNotContain("MessageBox", service);
        Assert.DoesNotContain("Activate()", service);
        Assert.Contains("=> false", service);
        Assert.Contains(doc.Descendants(p + "Button"), button => (string?)button.Attribute("Command") == "{Binding AcceptConfirmationCommand}");
        Assert.Contains(doc.Descendants(p + "Grid"), grid => (string?)grid.Attribute("Visibility") == "{Binding IsConfirmationOpen, Converter={StaticResource BoolVisibility}}");
        var vm = File.ReadAllText(Path.Combine(RepositoryRoot(), "src/Virgil.App/ViewModels/ApplicationsViewModel.cs"));
        Assert.DoesNotContain("System.IO", vm);
        Assert.DoesNotContain("Directory.Exists", vm);
        Assert.DoesNotContain("File.Exists", vm);
        Assert.DoesNotContain("explorer.exe", vm);
        Assert.DoesNotContain("System.Windows", vm);
        Assert.DoesNotContain("Process.Start", vm);
        Assert.DoesNotContain("ProcessStartInfo", vm);
        Assert.Contains("Source = \"ApplicationsDetails\"", vm);
    }

    [Fact]
    public void ViewKeepsCoresAtRestAndOnlySchedulesOverlayFocusOnOpening()
    {
        Assert.Null(typeof(ApplicationsViewModel).GetProperty("MiniCoreState"));
        Assert.Null(typeof(ApplicationsViewModel).GetProperty("DetailsCoreState"));
        var view = File.ReadAllText(Path.Combine(RepositoryRoot(), "src/Virgil.App/Views/ApplicationsView.xaml.cs"));
        Assert.DoesNotContain("SetState", view);
        Assert.Contains("nameof(ApplicationsViewModel.IsDetailsOpen) && _viewModel.IsDetailsOpen", view);
        Assert.Contains("nameof(ApplicationsViewModel.IsReportOpen) && _viewModel.IsReportOpen", view);
        Assert.Contains("if (_viewModel.IsDetailsOpen) CloseDetailsButton.Focus();", view);
        Assert.Contains("if (_viewModel.IsReportOpen) CloseApplicationsReportButton.Focus();", view);
        Assert.DoesNotContain("else", view);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(view, @"\.Focus\(\)").Count);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(view, "DispatcherPriority.Input").Count);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task IssueFollowsBothDialogsAndPolicyAndRefusalUsesCancellationPath()
    {
        var authority = new RefusingConfirmationService();
        var f = new Fixture(authority);
        f.Inventory.Report = new() { Applications = [Safe() with { RiskLevel = ApplicationRiskLevel.Caution }] };
        await f.Analyze();
        f.OpenDetails();
        f.Dialog.Answers.Enqueue(true);
        f.Dialog.Answers.Enqueue(false);
        await f.Launch();
        Assert.Equal(0, authority.Calls);
        f.Dialog.Answers.Enqueue(true);
        f.Dialog.Answers.Enqueue(true);
        authority.OnIssue = consent =>
        {
            Assert.Equal(4, f.Dialog.Calls.Count);
            Assert.Empty(f.Runner.Requests);
            Assert.Equal(ConfirmationLevel.Reinforced, consent.Level);
        };
        await f.Launch();
        Assert.Equal(1, authority.Calls);
        Assert.Empty(f.Runner.Requests);
        Assert.Equal("Desinstallation annulee.", f.Vm.StatusText);
        Assert.Equal("Desinstallation annulee.\nConsentement insuffisant pour cette action.", f.Messages.Last());
        Idle(f.Vm);
    }

    private sealed class RefusingConfirmationService : IConfirmationService
    {
        public int Calls { get; private set; }
        public Action<ConfirmationConsent>? OnIssue { get; set; }
        public ConfirmedPlan Issue(ActionPreview preview, ConfirmationConsent consent)
        {
            Calls++;
            OnIssue?.Invoke(consent);
            throw new ConfirmationRefusedException();
        }
    }

    private static void AssertRequest(ProcessLaunchRequest request, string target, string? raw)
    {
        Assert.Equal(target, request.FileName);
        Assert.Empty(request.Arguments);
        Assert.True(request.UseShellExecute);
        Assert.Equal(raw, request.RawArguments);
        Assert.Null(request.Verb);
        Assert.Equal(ProcessWindowMode.Normal, request.WindowMode);
        Assert.False(request.WaitForExit);
    }
    private static void Idle(ApplicationsViewModel vm) { Assert.False(vm.IsBusy); Assert.True(vm.IsIdle); }
    internal static InstalledApplication Safe() => new() { Id = "test-0d9-vlc", DisplayName = "VLC media player 0d9 test", Publisher = "Example.Publisher", WingetId = "VideoLAN.VLC", CanUninstall = true, RiskLevel = ApplicationRiskLevel.SafeToUninstall, UninstallKind = ApplicationUninstallKind.Winget };
    internal static XDocument ReadXaml() => XDocument.Load(Path.Combine(RepositoryRoot(), "src/Virgil.App/Views/ApplicationsView.xaml"));
    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Virgil.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Racine de test introuvable.");
    }

    internal sealed class Fixture
    {
        public ConfirmationService Confirmations { get; } = new(TimeProvider.System);
        public FakeInventory Inventory { get; } = new();
        public FakeRunner Runner { get; } = new();
        public FakeHistory History { get; } = new();
        public FakeDialog Dialog { get; } = new();
        public IInstallationLocationService Location { get; }
        public ApplicationUninstallService Uninstall { get; }
        public ApplicationsViewModel Vm { get; }
        public List<string> Messages { get; } = new();
        public List<(VirgilCoreState State, string Label)> States { get; } = new();
        public int Persisted { get; private set; }
        public IEnumerable<ApplicationItemViewModel> Cards => Vm.Entries.OfType<ApplicationItemViewModel>();
        public Fixture(IConfirmationService? confirmations = null, IInstallationLocationService? location = null)
        {
            Uninstall = new(new ApplicationUninstallCommandValidator(), new ShellApplicationProcessLauncher(Runner), new ApplicationRemnantScanner(), Confirmations);
            Location = location ?? new Virgil.Infrastructure.Applications.InstallationLocationService(Runner);
            Vm = new(Inventory, Uninstall, Runner, History, Dialog, confirmations ?? Confirmations, Location);
            Vm.VirgilMessageRequested += Messages.Add;
            Vm.VirgilStateRequested += (s, l) => States.Add((s, l));
            Vm.ReportPersisted += (_, _) => Persisted++;
            Vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(Vm.IsConfirmationOpen) && Vm.IsConfirmationOpen)
                {
                    var answer = Dialog.Confirm(Vm.ConfirmationTitle, Vm.ConfirmationBody);
                    if (answer) Vm.AcceptConfirmationCommand.Execute(null);
                    else Vm.RefuseConfirmationCommand.Execute(null);
                }
            };
        }
        public Task Analyze() => Execute(() => Vm.AnalyzeCommand.ExecuteAsync(null));
        public Task Launch() => Execute(() => Vm.LaunchUninstallCommand.ExecuteAsync(null));
        public void OpenDetails() { if (!Cards.Any()) Vm.SelectedFilter = "TOUT"; Assert.Single(Cards).DetailsCommand.Execute(null); }
        // Les notifications Progress<T> restent deterministes sans Dispatcher WPF.
        private static Task Execute(Func<Task> action)
        {
            var previous = SynchronizationContext.Current;
            try { SynchronizationContext.SetSynchronizationContext(new ImmediateContext()); return action(); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
    }
    private sealed class ImmediateContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }
    internal sealed class FakeInventory : IApplicationInventoryService
    {
        public ApplicationInventoryReport Report { get; set; } = new() { Applications = [Safe()] };
        public IProgress<ApplicationInventoryProgress>? Progress { get; private set; }
        public Func<CancellationToken, Task<ApplicationInventoryReport>>? Handler { get; set; }
        public Task<ApplicationInventoryReport> InventoryAsync(IProgress<ApplicationInventoryProgress>? progress, CancellationToken cancellationToken) { Progress = progress; return Handler?.Invoke(cancellationToken) ?? Task.FromResult(Report); }
    }
    internal sealed class FakeDialog : IApplicationUninstallConfirmationDialog
    {
        public Queue<bool> Answers { get; } = new();
        public List<(string Title, string Message)> Calls { get; } = new();
        public Action? OnConfirm { get; set; }
        public bool Confirm(string title, string message)
        {
            Calls.Add((title, message));
            OnConfirm?.Invoke();
            return Answers.Count > 0 && Answers.Dequeue();
        }
    }
    internal sealed class FakeRunner : IProcessRunner
    {
        public List<ProcessLaunchRequest> Requests { get; } = new();
        public CancellationToken Token { get; private set; }
        public int Started { get; private set; }
        public Func<ProcessLaunchRequest, CancellationToken, Task<ProcessLaunchResult>>? Handler { get; set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException("Aucune execution avec capture attendue.");
        public async Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Token = cancellationToken;
            var result = Handler is null ? new ProcessLaunchResult(true, null) : await Handler(request, cancellationToken);
            if (result.Started) Started++;
            return result;
        }
    }
    internal sealed class FakeHistory : IReportHistoryService
    {
        public List<ReportEntry> Reports { get; } = new();
        public ReportSaveResult Result { get; set; } = new() { Success = true };
        public CancellationToken Token { get; private set; }
        public Task<ReportSaveResult> SaveAsync(ReportEntry report, CancellationToken cancellationToken) { Reports.Add(report); Token = cancellationToken; return Task.FromResult(Result); }
        public Task<ReportHistoryLoadResult> LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetLatestAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
