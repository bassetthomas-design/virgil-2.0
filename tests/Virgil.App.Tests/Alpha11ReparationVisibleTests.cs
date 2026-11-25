using System.Xml.Linq;
using Virgil.App.ViewModels;
using Virgil.Core.Actions;
using Virgil.Contracts;
using Virgil.Core.Interventions;
using Xunit;

namespace Virgil.Tests;

public sealed class Alpha11ReparationVisibleTests
{
    [Fact]
    public async Task Catalogue_et_lancement_suivent_la_selection_visible()
    {
        var xml = XDocument.Load(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "src/Virgil.App/Views/InterventionsView.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var catalogue = Assert.Single(xml.Descendants(p + "Button"), b => (string?)b.Attribute(x + "Name") == "ShowAllRepairsButton");
        var selection = Assert.Single(xml.Descendants(p + "Button"), b => (string?)b.Attribute(x + "Name") == "RunSelectionButton");
        Assert.Same(catalogue.Parent, selection.Parent);
        Assert.Equal("TOUTES LES RÉPARATIONS", (string?)catalogue.Attribute("Content"));
        Assert.Equal("{Binding AnalyzeCommand}", (string?)catalogue.Attribute("Command"));
        Assert.Equal("{Binding IsIdle}", (string?)catalogue.Attribute("IsEnabled"));
        Assert.Equal("LANCER LA SÉLECTION", (string?)selection.Attribute("Content"));
        Assert.Equal("{Binding RunGuidedCommand}", (string?)selection.Attribute("Command"));
        Assert.Equal("{Binding IsRunSelectionEnabled}", (string?)selection.Attribute("IsEnabled"));

        InterventionId[] ids = [InterventionId.ResetStoreCache, InterventionId.ScheduleSystemDiskCheck, InterventionId.ResetWindowsUpdate];
        var actions = new FakeActions();
        var vm = new InterventionsViewModel(new FakeDiagnostics(ids), actions, new InterventionReportBuilder(),
            new CleanupViewModelTests.FakeHistory(), new RecordingConfirmation());
        await vm.AnalyzeCommand.ExecuteAsync(null);
        var items = vm.SystemItems.Concat(vm.NetworkItems).Concat(vm.StorageItems).Concat(vm.InterfaceItems).ToArray();
        Assert.Equal(ids, items.Select(i => i.Diagnostic.Definition.Id));
        Assert.All(items, i => Assert.True(i.IsAvailable));
        Assert.False(vm.IsRunSelectionEnabled);
        Assert.False(vm.IsValidationOpen);
        Assert.Equal(0, actions.Calls);
        var notified = false;
        vm.PropertyChanged += (_, args) => notified |= args.PropertyName == nameof(vm.IsRunSelectionEnabled);
        items[0].IsSelected = true;
        Assert.True(notified);
        Assert.True(vm.IsRunSelectionEnabled);
    }

    private sealed class FakeDiagnostics(InterventionId[] ids) : IInterventionDiagnosticService
    {
        public Task<InterventionDiagnostic> DiagnoseAsync(InterventionId id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<InterventionDiagnostic>> DiagnoseAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InterventionDiagnostic>>(ids.Select((id, index) => new InterventionDiagnostic
            {
                Definition = new() { Id = id, Title = id.ToString(), DisplayOrder = index },
                IsAvailable = true,
                Status = InterventionStatus.Recommended
            }).ToArray());
    }

    private sealed class FakeActions : IInterventionExecutionService
    {
        public int Calls { get; private set; }

        public Task<InterventionExecutionResult> ExecuteAsync(InterventionDiagnostic diagnostic, ConfirmedPlan? plan, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Aucune exécution attendue.");
        }

        public InterventionExecutionResult Skip(InterventionDiagnostic diagnostic) => throw new NotSupportedException();
        public InterventionExecutionResult Cancel(InterventionDiagnostic diagnostic) => throw new NotSupportedException();
        public InterventionSessionReport CreateReport(DateTimeOffset startedAt, IReadOnlyList<InterventionDiagnostic> proposedActions,
            IReadOnlyList<InterventionExecutionResult> results, IReadOnlyList<string> errors) => throw new NotSupportedException();
    }
}
