using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.Input;
using Virgil.App.ViewModels;
using Virgil.App.Views;
using Virgil.Contracts;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class D2SingleItemTests
{
    // Reflection keeps the tests-only commit compilable against the unchanged application.
    // All behavior below still uses the real ViewModel, sequence and confirmation gate.
    internal static IAsyncRelayCommand Command(UpdatesViewModel vm) => Assert.IsAssignableFrom<IAsyncRelayCommand>(
        typeof(UpdatesViewModel).GetProperty("RunSingleCommand")?.GetValue(vm));
    internal static T Read<T>(object owner, string name) => Assert.IsAssignableFrom<T>(owner.GetType().GetProperty(name)?.GetValue(owner));
    internal static object Card(UpdatesViewModel vm, string key) => Assert.Single(
        Read<IEnumerable>(vm, "SingleCards").Cast<object>(), card => Read<string>(card, "Key") == key);
    private static UpdateItem App(string id) => new() { Id = id, Name = "Même application", InstalledVersion = "1.0", AvailableVersion = "2.0" };
    private static UpdateSequenceStep Step(string id) => new(id, "Même application", UpdateComponent.Winget, App(id));

    [Fact]
    public Task Partial_screen_derives_sources_from_individual_results() => ThemeContrastTests.OnSta(async () =>
    {
        var scan = new Scan();
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: scan);
        f.Backend.Reboot = false;
        var calls = new List<string>();
        f.Backend.Handler = (step, _) =>
        {
            Assert.Equal(UpdateComponent.Winget, step.Component);
            Assert.Contains(step.Id, new[] { "a", "b" });
            calls.Add(step.Id);
            if (step.Id == "a") scan.Remaining.Remove("a");
            return Task.FromResult(new UpdateSequenceStepResult(step, step.Id == "a", false,
                step.Id == "a" ? "Installation réussie." : "Installation impossible."));
        };
        await f.Vm.ScanUpdatesCommand.ExecuteAsync(null);
        var command = Command(f.Vm);
        var pending = command.ExecuteAsync(Step("a"));
        try
        {
            Assert.Equal("Mettre à jour Même application", f.Vm.ValidationTitle);
            Assert.Equal("Même application\n1.0 → 2.0\nMême méthode que « Tout mettre à jour ».", f.Vm.ValidationDetails);
            Assert.Equal("Aucun redémarrage automatique.", f.Vm.ValidationSafety);
            Assert.Empty(calls);
            Assert.Equal(0, f.Confirmation.Calls);
            await Capture(f.Vm, "02-confirmation-logiciel");
        }
        finally { f.Vm.ExecuteDecisionCommand.Execute(null); await pending; }
        Assert.Equal("mis à jour", Read<string>(Card(f.Vm, "Winget:a"), "Status"));
        Assert.Equal("reste à installer", Read<string>(Card(f.Vm, "Winget:c"), "Status"));
        pending = command.ExecuteAsync(Step("b"));
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(new[] { "a", "b" }, calls);
        Assert.Equal("en erreur", Read<string>(Card(f.Vm, "Winget:b"), "Status"));
        Assert.Equal("mis à jour", Read<string>(Card(f.Vm, "Winget:a"), "Status"));
        Assert.Equal("Applications : en erreur — 1 mis à jour, 1 en erreur, 1 reste à installer", Read<string>(f.Vm, "SingleSourceSummary").Split('\n')[0]);
        Assert.DoesNotContain("Tout est à jour.", f.Vm.Operation.FinalSummary);
        Assert.Equal(2, f.Confirmation.Calls);
        Assert.Equal(3, scan.Calls);
        await Capture(f.Vm, "04-bilan-partiel");
    });

    [Theory]
    [InlineData(false, "reste à installer")]
    [InlineData(true, "indisponible")]
    public Task Successful_command_requires_own_final_proof(bool unavailable, string expected) => ThemeContrastTests.OnSta(async () =>
    {
        var scan = new Scan();
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: scan);
        await f.Vm.ScanUpdatesCommand.ExecuteAsync(null);
        scan.Unavailable = unavailable;
        f.Backend.Reboot = false;
        f.Backend.Handler = (step, _) => Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Installation réussie."));
        var pending = Command(f.Vm).ExecuteAsync(Step("a"));
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(expected, Read<string>(Card(f.Vm, "Winget:a"), "Status"));
        Assert.DoesNotContain("Tout est à jour.", f.Vm.Operation.FinalSummary);
    });

    [Fact]
    public Task Double_click_and_other_operations_are_blocked_until_completion() => ThemeContrastTests.OnSta(async () =>
    {
        var scan = new Scan();
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: scan);
        await f.Vm.ScanUpdatesCommand.ExecuteAsync(null);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<UpdateSequenceStepResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Backend.Handler = (step, _) => { Assert.Equal("a", step.Id); started.TrySetResult(); return finish.Task; };
        var command = Command(f.Vm);
        var pending = command.ExecuteAsync(Step("a"));
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.False(f.Vm.IsIdle);
            Assert.False(f.Vm.IsRunAllEnabled);
            Assert.False(command.CanExecute(Step("a")));
            Assert.False(command.CanExecute(Step("b")));
            await command.ExecuteAsync(Step("a"));
            await f.Vm.ScanUpdatesCommand.ExecuteAsync(null);
            await f.Vm.InspectDriversCommand.ExecuteAsync(null);
            Assert.Equal(1, scan.Calls);
            Assert.Single(f.Backend.Executed);
            await Capture(f.Vm, "07-boutons-desactives");
            f.Vm.CancelActiveOperation();
            Assert.False(pending.IsCompleted);
            scan.Remaining.Remove("a");
        }
        finally { finish.TrySetResult(new(Step("a"), true, false, "Installation réussie.")); await pending; }
        Assert.Equal("mis à jour", Read<string>(Card(f.Vm, "Winget:a"), "Status"));
        Assert.Single(f.Backend.Executed);
        await Capture(f.Vm, "08-annulation");
    });

    [Fact]
    public Task Cancellation_before_confirmation_has_no_effect() => ThemeContrastTests.OnSta(async () =>
    {
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: new Scan());
        await f.Vm.ScanUpdatesCommand.ExecuteAsync(null);
        var pending = Command(f.Vm).ExecuteAsync(Step("a"));
        f.Vm.CancelActiveOperation();
        await pending;
        Assert.Empty(f.Backend.Executed);
        Assert.Equal(0, f.Confirmation.Calls);
        Assert.Equal("non lancée", Read<string>(Card(f.Vm, "Winget:a"), "Status"));
    });

    [Fact]
    public Task Full_sequence_disables_single_actions_without_replacing_its_snapshot() => ThemeContrastTests.OnSta(async () =>
    {
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: new Scan());
        await f.Vm.ScanUpdatesCommand.ExecuteAsync(null);
        var command = Command(f.Vm);
        var pending = f.Vm.RunAllCommand.ExecuteAsync(null);
        try
        {
            Assert.False(command.CanExecute(Step("a")));
            var choices = f.Vm.SequenceChoices.ToArray();
            await command.ExecuteAsync(Step("a"));
            Assert.Equal(choices, f.Vm.SequenceChoices);
            Assert.Empty(f.Backend.Executed);
        }
        finally { f.Vm.SkipDecisionCommand.Execute(null); await pending; }
    });

    [Theory]
    [InlineData("truncated")]
    [InlineData("ellipsis")]
    [InlineData("version")]
    [InlineData("critical")]
    [InlineData("invented")]
    [InlineData("firmware")]
    public Task Unsafe_or_unseen_target_has_no_button_or_effect(string scenario) => ThemeContrastTests.OnSta(async () =>
    {
        var item = App("a");
        item = scenario switch
        {
            "truncated" => item with { IsIdTruncated = true },
            "ellipsis" => item with { Id = "a…" },
            "version" => item with { AvailableVersion = "" },
            "critical" => item with { RiskLevel = UpdateRiskLevel.CriticalInformationOnly },
            "firmware" => item with { Name = "System Firmware", Source = UpdateSource.FirmwareInformation },
            _ => item
        };
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: new Scan { Items = [item] });
        await f.Vm.ScanUpdatesCommand.ExecuteAsync(null);
        var step = new UpdateSequenceStep(item.Id, item.Name, UpdateComponent.Winget, item);
        if (scenario == "invented") step = Step("unseen");
        var command = Command(f.Vm);
        if (scenario != "invented")
        {
            var card = Card(f.Vm, "Winget:" + item.Id);
            Assert.Equal("non pris en charge automatiquement", Read<string>(card, "Status"));
            Assert.Equal(scenario == "firmware"
                ? "Virgil n'installe jamais le BIOS ni le firmware ; passez par l'outil officiel du fabricant de l'ordinateur si une mise à jour est proposée."
                : scenario == "critical" ? "Cet élément est affiché pour information ; aucune installation automatique n'est proposée."
                : "Cet élément ne peut pas être mis à jour automatiquement ; relancez l'analyse pour confirmer sa version.", Read<string>(card, "Detail"));
        }
        Assert.False(command.CanExecute(step));
        await command.ExecuteAsync(step);
        Assert.False(f.Vm.IsValidationOpen);
        Assert.Empty(f.Backend.Executed);
    });

    [Fact]
    public Task Application_button_has_exact_new_labels_and_preserves_legacy_selection() => ThemeContrastTests.OnSta(async () =>
    {
        var f = new UpdatesSequenceViewModelTests.Fixture(scan: new Scan());
        await f.Vm.ScanUpdatesCommand.ExecuteAsync(null);
        var card = Card(f.Vm, "Winget:a");
        Assert.Equal("Même application", Read<string>(card, "Title"));
        Assert.Equal("Mettre à jour", Read<string>(card, "ButtonText"));
        Assert.Equal("Même méthode que « Tout mettre à jour ».", Read<string>(card, "MethodText"));
        Assert.True(Read<bool>(card, "CanUpdate"));
        var legacy = Assert.Single(f.Vm.Entries.OfType<UpdateItemViewModel>(), c => c.Item.Id == "a");
        legacy.IsSelected = true;
        Assert.True(f.Vm.IsRunSelectedEnabled);
        Assert.Contains("ID : a", legacy.Lines);
        await Capture(f.Vm, "01-carte-avec-bouton");
    });

    private sealed class Scan : IUpdateScanService
    {
        public int Calls { get; private set; }
        public bool Unavailable { get; set; }
        public UpdateItem[]? Items { get; init; }
        public HashSet<string> Remaining { get; } = ["a", "b", "c"];
        public Task<UpdateScanReport> ScanAsync(UpdateScanRequest request, IProgress<string>? progress, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new UpdateScanReport { Winget = new() { IsAvailable = !Unavailable },
                Items = Items ?? Remaining.Select(App).ToArray() });
        }
    }

    internal static async Task Capture(UpdatesViewModel vm, string name)
    {
        var view = new UpdatesView(vm) { Width = 1600, Height = 2400 };
        using var presentation = new HwndSource(new HwndSourceParameters("Preuve WPF D2")
        {
            Width = 1600, Height = 2400, PositionX = -20000, PositionY = -20000,
            WindowStyle = unchecked((int)0x90000000)
        });
        try
        {
            presentation.RootVisual = view;
            view.Measure(new Size(1600, 2400));
            view.Arrange(new Rect(0, 0, 1600, 2400));
            view.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            view.Measure(new Size(1600, 2400));
            view.Arrange(new Rect(0, 0, 1600, 2400));
            view.UpdateLayout();
            var buttons = Descendants<Button>(view).Where(b => Equals(b.Content, "Mettre à jour") && b.IsVisible).ToArray();
            Assert.NotEmpty(buttons);
            if (name == "07-boutons-desactives") Assert.All(buttons, b => Assert.False(b.IsEnabled));
            var required = Descendants<TextBlock>(view).Where(text => text.IsVisible && (vm.IsValidationOpen
                ? text.Name is "ValidationTitleText" or "ValidationDetailsText" or "ValidationSafetyText"
                : text.DataContext?.GetType().GetProperty("Key") is not null)).ToArray();
            Assert.NotEmpty(required);
            foreach (var text in required)
            {
                var bounds = new Rect(view.RenderSize); bounds.Inflate(0.5, 0.5);
                Assert.True(bounds.Contains(text.TransformToAncestor(view).TransformBounds(new Rect(text.RenderSize))), "Texte D2 hors cadre.");
                for (var parent = VisualTreeHelper.GetParent(text); parent is not null && parent != view; parent = VisualTreeHelper.GetParent(parent))
                    if (parent is ScrollContentPresenter viewport)
                    {
                        var visible = new Rect(viewport.RenderSize); visible.Inflate(0.5, 0.5);
                        Assert.True(visible.Contains(text.TransformToAncestor(viewport).TransformBounds(new Rect(text.RenderSize))), "Texte D2 hors de la zone visible.");
                    }
            }
            var directory = Path.Combine(TestRepositoryRoot.FindRepositoryRoot(), "artifacts", "ui-smoke", "lot-d2");
            Directory.CreateDirectory(directory);
            var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            foreach (var text in required.Where(text => !string.IsNullOrWhiteSpace(text.Text)))
            {
                var area = text.TransformToAncestor(view).TransformBounds(new Rect(text.RenderSize));
                var x = Math.Max(0, (int)Math.Floor(area.X)); var y = Math.Max(0, (int)Math.Floor(area.Y));
                var width = Math.Min(bitmap.PixelWidth - x, Math.Max(1, (int)Math.Ceiling(area.Width)));
                var height = Math.Min(bitmap.PixelHeight - y, Math.Max(1, (int)Math.Ceiling(area.Height)));
                Assert.True(width > 0 && height > 0);
                var pixels = new byte[width * height * 4]; bitmap.CopyPixels(new Int32Rect(x, y, width, height), pixels, width * 4, 0);
                Assert.True(Enumerable.Range(0, width * height).Any(i => pixels[i * 4 + 3] > 0
                    && Math.Max(pixels[i * 4], Math.Max(pixels[i * 4 + 1], pixels[i * 4 + 2])) >= 96), "Texte D2 absent des pixels rendus.");
            }
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
        }
        finally { presentation.RootVisual = null; }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var item in Descendants<T>(child)) yield return item;
        }
    }
}
