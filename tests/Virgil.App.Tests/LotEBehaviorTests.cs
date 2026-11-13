using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Virgil.App.Animation;
using Virgil.App.Controls;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class LotEBehaviorTests
{
    [Theory]
    [InlineData(false, false, "NeedsReview", VirgilCoreState.Warning)]
    [InlineData(true, false, "Failed", VirgilCoreState.Error)]
    [InlineData(false, true, "NeedsReview", VirgilCoreState.Warning)]
    public Task One_sequence_keeps_success_refusal_and_remaining_cards_independent(bool refusal, bool reboot, string expected, VirgilCoreState face) => ThemeContrastTests.OnSta(async () =>
    {
        var scan = new MixedScan(); var f = new UpdatesSequenceViewModelTests.Fixture(scan: scan); f.Backend.Reboot = false;
        f.Backend.Handler = (step, _) =>
        {
            Assert.Equal(UpdateComponent.Winget, step.Component);
            if (step.Id == "a") scan.Remaining.Remove("a");
            return Task.FromResult(new UpdateSequenceStepResult(step, step.Id != "b" || !refusal, reboot,
                step.Id == "b" && refusal ? "Installation refusée, malgré le mot réussi." : "Installation réussie."));
        };
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        foreach (var choice in f.Vm.SequenceChoices)
            choice.IsSelected = choice.Step.Component == UpdateComponent.Winget && choice.Step.Id is "a" or "b";
        f.Vm.ExecuteDecisionCommand.Execute(null); await pending;
        Assert.Equal(expected, LotEProgressColorTests.Read(f.Vm.Operation, "FinalState").ToString());
        Assert.Equal(face, f.Vm.MiniCoreState);
        Assert.Equal(reboot ? "redémarrage nécessaire" : "mis à jour", Assert.Single(f.Vm.Operation.Steps, c => c.Key == "Winget:a").Status);
        Assert.Equal(refusal ? "en erreur" : "reste à installer", Assert.Single(f.Vm.Operation.Steps, c => c.Key == "Winget:b").Status);
        Assert.Equal("reste à installer", Assert.Single(f.Vm.SingleCards, c => c.Key == "Winget:c").Status);
        Assert.Equal(100d, f.Vm.Operation.Percent);
        Assert.Equal(2, scan.Calls);
        var panel = new OperationProgressPanel { DataContext = f.Vm.Operation };
        LotEFaceTests.Capture(panel, $"bilan-mixte-{refusal}-{reboot}", 720, 420);
    });

    [Fact]
    public Task Loaded_face_transitions_in_200ms_then_slowly_blinks_and_stops_when_suppressed() => ThemeContrastTests.OnSta(async () =>
    {
        var face = new VirgilCoreControl { Width = 256, Height = 256, AnimationDetailLevel = VirgilCoreAnimationDetailLevel.Full };
        var window = new Window { Content = face, Width = 300, Height = 300, ShowInTaskbar = false };
        var refresh = typeof(VirgilCoreControl).GetMethod("RefreshFace", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(refresh);
        try
        {
            window.Show(); window.UpdateLayout();
            face.SetState(VirgilCoreState.Scanning); refresh.Invoke(face, new object[] { true });
            var eyes = Assert.IsAssignableFrom<FrameworkElement>(face.FindName("Eyes"));
            Assert.True(eyes.HasAnimatedProperties);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(280);
            Assert.False(eyes.HasAnimatedProperties);
            face.SetState(VirgilCoreState.Idle); refresh.Invoke(face, new object[] { true });
            await Task.Delay(280);
            Assert.True(eyes.RenderTransform.HasAnimatedProperties);
            refresh.Invoke(face, new object[] { false });
            Assert.False(eyes.HasAnimatedProperties); Assert.False(eyes.RenderTransform.HasAnimatedProperties);
            face.AnimationDetailLevel = VirgilCoreAnimationDetailLevel.Compact;
            refresh.Invoke(face, new object[] { true });
            Assert.False(eyes.HasAnimatedProperties); Assert.False(eyes.RenderTransform.HasAnimatedProperties);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Reserved_sarcastic_shape_does_not_add_an_application_state()
    {
        var type = typeof(VirgilCoreState).Assembly.GetType("Virgil.App.Animation.VirgilEyeShape");
        Assert.NotNull(type); Assert.True(type.IsEnum);
        Assert.Contains("Sarcastic", Enum.GetNames(type));
        Assert.DoesNotContain("Sarcastic", Enum.GetNames<VirgilCoreState>());
    }

    [Fact]
    public async Task Icon_script_reproduces_the_committed_bytes_without_external_tools()
    {
        var root = TestRepositoryRoot.FindRepositoryRoot();
        var script = System.IO.Path.Combine(root, "build", "Build-Icon.ps1");
        Assert.True(File.Exists(script), "Le générateur reproductible doit être livré.");
        var output = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "virgil-icon-" + Guid.NewGuid() + ".ico");
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("pwsh")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-OutputPath", output }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start); Assert.NotNull(process);
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == 0, await stdout + await stderr);
            Assert.Empty(await stderr);
            Assert.Equal(File.ReadAllBytes(System.IO.Path.Combine(root, "assets", "virgil.ico")), File.ReadAllBytes(output));
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }

    private sealed class MixedScan : IUpdateScanService
    {
        public int Calls { get; private set; }
        public HashSet<string> Remaining { get; } = ["a", "b", "c"];
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new UpdateScanReport { Winget = new() { IsAvailable = true }, Items = Remaining.Select(id =>
                new UpdateItem { Id = id, Name = "Application " + id, InstalledVersion = "1.0", AvailableVersion = "2.0" }).ToArray() });
        }
    }
}
