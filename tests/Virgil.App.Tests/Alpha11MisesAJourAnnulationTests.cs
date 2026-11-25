using System.Diagnostics;
using System.Xml.Linq;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

public sealed class Alpha11MisesAJourAnnulationTests
{
    [Fact]
    public async Task Annulation_rend_le_retour_disponible_pendant_une_etape()
    {
        var xml = XDocument.Load(Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "src/Virgil.App/Views/UpdatesView.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var button = Assert.Single(xml.Descendants(p + "Button"), b => (string?)b.Attribute(x + "Name") == "ReturnHomeButton");
        Assert.Equal("RETOUR", (string?)button.Attribute("Content"));
        Assert.Equal("{Binding IsReturnHomeEnabled}", (string?)button.Attribute("IsEnabled"));
        Assert.Equal("{Binding ReturnHomeCommand}", (string?)button.Attribute("Command"));
        var panel = Assert.Single(xml.Descendants(), e => e.Name.LocalName == "OperationProgressPanel");
        Assert.Equal("{Binding Operation}", (string?)panel.Attribute("DataContext"));
        Assert.Equal("UpdatesCancelButton", (string?)panel.Attribute("CancelAutomationId"));
        Assert.Equal(p + "ScrollViewer", panel.Parent?.Name);
        Assert.NotNull(panel.Parent?.Attribute("MaxHeight"));

        var f = new UpdatesSequenceViewModelTests.Fixture();
        var vm = f.Vm;
        var preview = vm.RunAllCommand.ExecuteAsync(null);
        try
        {
            Assert.True(vm.IsValidationOpen);
            Assert.False(vm.IsReturnHomeEnabled);
        }
        finally
        {
            vm.CancelActiveOperation();
            await CompleteAsync(preview, "fermeture de l’aperçu");
        }
        Assert.False(vm.IsValidationOpen);
        Assert.Empty(f.Backend.Executed);

        var entered = new TaskCompletionSource<UpdateSequenceStep>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<UpdateSequenceStepResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Backend.Handler = (step, _) => { entered.SetResult(step); return finish.Task; };
        var returned = 0;
        vm.ReturnHomeRequested += (_, _) => returned++;
        var pending = vm.RunAllCommand.ExecuteAsync(null);
        try
        {
            Assert.True(vm.IsValidationOpen);
            foreach (var choice in vm.SequenceChoices) choice.IsSelected = choice.Step.Component == UpdateComponent.Windows;
            vm.ExecuteDecisionCommand.Execute(null);
            await CompleteAsync(entered.Task, "entrée dans l’étape Windows Update");
            Assert.False(vm.IsReturnHomeEnabled);
            var stopwatch = Stopwatch.StartNew();
            vm.CancelActiveOperation();
            Assert.True(stopwatch.Elapsed <= TimeSpan.FromSeconds(1),
                $"Accusé d’annulation attendu en une seconde, délai observé {stopwatch.Elapsed}.");
            Assert.True(vm.IsCancelling);
            Assert.True(vm.IsReturnHomeEnabled);
            Assert.False(vm.IsValidationOpen);
            Assert.Contains("ne peut pas être interrompue", vm.SequenceStepText);
            Assert.Contains("Vous pouvez revenir à l'accueil", vm.SequenceStepText);
            Assert.True(vm.ReturnHomeCommand.CanExecute(null));
            vm.ReturnHomeCommand.Execute(null);
            Assert.Equal(1, returned);
            Assert.Single(f.Backend.Executed);
            Assert.False(vm.ScanUpdatesCommand.CanExecute(null));
            Assert.False(vm.RunAllCommand.CanExecute(null));
            Assert.False(pending.IsCompleted);
        }
        finally
        {
            if (entered.Task.IsCompletedSuccessfully)
                finish.TrySetResult(new(await entered.Task, true, false, "Terminée"));
            else
            {
                vm.CancelActiveOperation();
                finish.TrySetCanceled();
            }
            await CompleteAsync(pending, "fin du contrôle final");
        }
        Assert.True(vm.IsReturnHomeEnabled);
        Assert.True(vm.ScanUpdatesCommand.CanExecute(null));
        Assert.True(vm.RunAllCommand.CanExecute(null));
        Assert.Single(f.Backend.Executed);
        Assert.Equal("Séquence annulée entre deux étapes — contrôle final effectué.", vm.StatusText);
        Assert.NotNull(f.History.Report);

        async Task CompleteAsync(Task task, string expected)
        {
            try { await task.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (TimeoutException)
            {
                Assert.Fail($"Attendu : {expected}. Observé : tâche {task.Status}, état « {vm.StatusText} », {f.Backend.Executed.Count} étapes lancées.");
            }
        }
    }
}
