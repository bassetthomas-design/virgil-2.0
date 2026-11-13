using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Virgil.App.Animation;
using Virgil.App.Controls;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Updates;
using Xunit;
using static Virgil.Tests.LotFSpeechTests;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class LotFOperationTests
{
    [Theory]
    [InlineData(false, false, false, OperationFinalState.Succeeded)]
    [InlineData(false, true, false, OperationFinalState.NeedsReview)]
    [InlineData(false, false, true, OperationFinalState.NeedsReview)]
    [InlineData(true, true, false, OperationFinalState.Failed)]
    public Task Real_sequence_picks_from_its_typed_result_with_partial_refusal_and_remaining(bool refusal, bool remaining, bool reboot, OperationFinalState expected) => ThemeContrastTests.OnSta(async () =>
    {
        var scan = new Scan(); var f = new UpdatesSequenceViewModelTests.Fixture(scan: scan); f.Backend.Reboot = false;
        f.Backend.Handler = (step, _) =>
        {
            if (step.Id == "a" || !remaining) scan.Remaining.Remove(step.Id);
            return Task.FromResult(new UpdateSequenceStepResult(step, step.Id != "b" || !refusal, reboot, "Installation réussie malgré un éventuel refus."));
        };
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices) choice.IsSelected = choice.Step.Component == UpdateComponent.Winget;
        f.Vm.ExecuteDecisionCommand.Execute(null); await pending;
        Assert.Equal(expected, f.Vm.Operation.FinalState);
        Assert.Contains((string)Read(f.Vm.Operation, "Remark"), Expected[expected.ToString() + (expected == OperationFinalState.Succeeded ? "Humour" : "")]);
        Assert.Equal(expected == OperationFinalState.Failed ? VirgilCoreState.Error : expected == OperationFinalState.Succeeded ? VirgilCoreState.Success : VirgilCoreState.Warning, f.Vm.MiniCoreState);
        var face = new VirgilCoreControl(); face.SetState(f.Vm.MiniCoreState);
        if (refusal)
        {
            Call(face, "ShowSarcasticGlance");
            Assert.Equal("Error", ((FrameworkElement)face.FindName("Eyes")).Tag);
        }
        Assert.Equal(2, scan.Calls);
    });
    [Fact]
    public async Task Confirmation_refusal_is_neutral_with_unchanged_status_and_no_execution()
    {
        var f = new UpdatesSequenceViewModelTests.Fixture(); f.Confirmation.Refuse = true;
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null); f.Vm.ExecuteDecisionCommand.Execute(null); await pending;
        Assert.Equal(OperationFinalState.Neutral, f.Vm.Operation.FinalState);
        Assert.Equal("Confirmation refusée. Aucune mise à jour lancée.", f.Vm.StatusText);
        Assert.Contains((string)Read(f.Vm.Operation, "Remark"), Expected["NeutralHumour"]); Assert.Empty(f.Backend.Executed);
    }
    [Theory]
    [InlineData("en erreur")]
    [InlineData("à vérifier")]
    public Task Information_cards_do_not_change_success_remark(string status) => ThemeContrastTests.OnSta(() =>
    {
        var f = new UpdatesSequenceViewModelTests.Fixture();
        f.Vm.SingleCards.Add(new("") { Step = new("a", "Application", UpdateComponent.Winget), Status = "mis à jour" });
        f.Vm.SingleCards.Add(new("") { Step = new("SteamGame:1", "Jeu", UpdateComponent.Launcher) { InformationOnly = true }, Status = status });
        f.Vm.Operation.Start("Lecture");
        var end = typeof(UpdatesViewModel).GetMethod("EndOperation", BindingFlags.Instance | BindingFlags.NonPublic); Assert.NotNull(end); end.Invoke(f.Vm, null);
        Assert.Equal(OperationFinalState.Succeeded, f.Vm.Operation.FinalState);
        Assert.Contains((string)Read(f.Vm.Operation, "Remark"), Expected["SucceededHumour"]);
        return Task.CompletedTask;
    });
    [Fact]
    public Task Real_panel_shows_each_remark_and_one_conclusion() => ThemeContrastTests.OnSta(() =>
    {
        var vm = new LongOperationViewModel(() => { }, new Clock()); vm.Start("Lecture", immediately: true);
        var panel = new OperationProgressPanel { DataContext = vm };
        LotFRevealTests.Capture(panel, "panneau-en-cours");
        foreach (var state in Enum.GetValues<OperationFinalState>())
        {
            vm.Complete("Conclusion unique.", finalState: state);
            LotFRevealTests.Capture(panel, "panneau-" + state);
            var blocks = Descendants(panel).OfType<TextBlock>().ToArray();
            var remark = Assert.Single(blocks, b => AutomationProperties.GetAutomationId(b) == "VirgilRemarkText");
            Assert.Equal(Read(vm, "Remark"), AutomationProperties.GetName(remark));
            Assert.Equal(1, blocks.Count(b => b.Text == "Conclusion unique."));
        }
        Set(vm, "HumourAllowed", false); vm.Start("Nettoyage", immediately: true); vm.Complete("Nettoyage terminé.");
        Assert.Contains((string)Read(vm, "Remark"), Expected["NeutralSober"]);
        LotFRevealTests.Capture(panel, "panneau-nettoyage-sobre");
        return Task.CompletedTask;
    });
    [Fact]
    public Task Loaded_sarcastic_glance_restores_after_1200ms_and_state_change_cancels_it() => ThemeContrastTests.OnSta(async () =>
    {
        var face = new VirgilCoreControl { Width = 256, Height = 256, AnimationDetailLevel = VirgilCoreAnimationDetailLevel.Full };
        var window = new Window { Content = face, Width = 300, Height = 300, ShowInTaskbar = false };
        try
        {
            window.Show(); window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var eyes = (FrameworkElement)face.FindName("Eyes");
            Assert.Equal("Rest", eyes.Tag);
            // Capture a real rendered control while retaining its loaded window.
            CaptureFace(face, "visage-repos"); Call(face, "ShowSarcasticGlance");
            Assert.Equal(SystemParameters.ClientAreaAnimation ? "Sarcastic" : "Rest", eyes.Tag);
            Assert.Equal("Virgil au repos", AutomationProperties.GetName(face));
            CaptureFace(face, "visage-coup-oeil");
            await Task.Delay(1300); Assert.Equal("Rest", eyes.Tag);
            Call(face, "ShowSarcasticGlance"); face.SetState(VirgilCoreState.Error); Assert.Equal("Error", eyes.Tag);
            foreach (var state in new[] { VirgilCoreState.Error, VirgilCoreState.Warning, VirgilCoreState.Scanning, VirgilCoreState.Executing })
            {
                face.SetState(state); var shape = eyes.Tag; Call(face, "ShowSarcasticGlance"); Assert.Equal(shape, eyes.Tag);
            }
            face.SetState(VirgilCoreState.Idle); face.AnimationDetailLevel = VirgilCoreAnimationDetailLevel.Compact;
            Call(face, "ShowSarcasticGlance"); Assert.Equal("Rest", eyes.Tag);
        }
        finally { window.Close(); }
    });
    private static void CaptureFace(FrameworkElement face, string name)
    {
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32); bitmap.Render(face);
        var path = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-f"); Directory.CreateDirectory(path);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(path, name + ".png")); encoder.Save(stream);
    }
    private sealed class Scan : IUpdateScanService
    {
        public HashSet<string> Remaining { get; } = ["a", "b", "c"];
        public int Calls { get; private set; }
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token)
        {
            Calls++; return Task.FromResult(new UpdateScanReport { Winget = new() { IsAvailable = true }, Items = Remaining.Select(id =>
                new UpdateItem { Id = id, Name = "Application " + id, InstalledVersion = "1", AvailableVersion = "2" }).ToArray() });
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var node in Descendants(child)) yield return node; }
    }
}
