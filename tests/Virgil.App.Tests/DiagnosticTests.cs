using Microsoft.Extensions.DependencyInjection;
using Virgil.App.Hosting;
using Virgil.App.ViewModels;
using Virgil.Contracts.Diagnostic;
using Virgil.Core.Actions;
using Virgil.Core.Diagnostic;
using Virgil.Infrastructure.Windows.Diagnostic;
using Xunit;

namespace Virgil.Tests;

public sealed class DiagnosticPresentationTests
{
    [Fact]
    public async Task HiddenAndMinimizedViewsNeverRead()
    {
        var source = new BlockingSource();
        var vm = Create(source);
        await vm.RefreshAsync();
        vm.SetPresentation(false, false);
        vm.SetPresentation(true, true);
        await vm.RefreshAsync();
        Assert.Equal(0, source.Reads);
        Assert.Null(vm.Snapshot);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task VisibleViewStartsAndHidingOrMinimizingCancelsImmediately(bool visible, bool minimized)
    {
        var source = new BlockingSource();
        var vm = Create(source);
        vm.SetPresentation(true, false);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsRefreshing);
        vm.SetPresentation(visible, minimized);
        await vm.PresentationTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, source.Active);
        Assert.Null(vm.Snapshot);
        Assert.False(vm.IsRefreshing);
    }
    [Fact]
    public async Task RepeatedVisibilityAndManualRefreshNeverOverlapSampling()
    {
        var source = new BlockingSource();
        var vm = Create(source);
        vm.SetPresentation(true, false);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.SetPresentation(true, false);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => vm.RefreshAsync()));
        Assert.Equal(1, source.Reads);
        vm.SetPresentation(false, false);
        vm.SetPresentation(true, false);
        vm.SetPresentation(false, false);
        await vm.PresentationTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, source.Maximum);
        Assert.Equal(0, source.Active);
    }
    [Fact]
    public void DiagnosticUsesValidatedDiWithSharedSamplerAndTwoMaintenanceModules()
    {
        var services = new ServiceCollection().AddVirgilServices().AddVirgilViews();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var vm = provider.GetRequiredService<DiagnosticViewModel>();
        Assert.Same(vm, provider.GetRequiredService<DiagnosticViewModel>());
        Assert.Same(provider.GetRequiredService<DiagnosticSampler>(), provider.GetRequiredService<DiagnosticSampler>());
        var modules = provider.GetServices<IMaintenanceModule>().ToArray();
        Assert.Equal(2, modules.Length);
        Assert.Single(modules.OfType<DiagnosticModule>());
        Assert.Single(modules.OfType<Virgil.Core.Cleanup.CleanupMaintenanceModule>());
        Assert.IsType<WindowsDiagnosticSource>(provider.GetRequiredService<IDiagnosticSource>());
    }
    [Fact]
    public void DisplayDoesNotTurnMissingValuesIntoZero()
    {
        var missing = DiagnosticValue<double>.Unavailable("Indisponible : capteur non exposé par Windows");
        Assert.Equal(missing.Reason, DiagnosticDisplay.Percent(missing));
        Assert.Equal(missing.Reason, DiagnosticDisplay.Bytes(missing));
        Assert.Equal(missing.Reason, DiagnosticDisplay.Rate(missing));
        Assert.Equal("1 Go", DiagnosticDisplay.Bytes(DiagnosticValue<double>.Available(1024d * 1024 * 1024)));
        Assert.Equal("1 Mo/s", DiagnosticDisplay.Rate(DiagnosticValue<double>.Available(1024d * 1024)));
    }
    [Fact]
    [Trait("Category", "Windows")]
    public async Task RealWindowsReadingHasLogicalCoresMemoryVolumesAndCurrentProcess()
    {
        var source = new WindowsDiagnosticSource();
        var reading = await source.ReadAsync(CancellationToken.None);
        Assert.Equal(Environment.ProcessorCount, reading.Cpu.Cores.Count);
        Assert.True(reading.Memory.TotalBytes.IsAvailable);
        Assert.True(reading.Memory.TotalBytes.Value > 0);
        Assert.Contains(reading.Disks, d => d.Kind == "Volume" && d.Total.IsAvailable && d.Total.Value > 0);
        Assert.Contains(reading.Processes, p => p.Pid == Environment.ProcessId);
    }
    private static DiagnosticViewModel Create(IDiagnosticSource source) => new(new DiagnosticModule(new DiagnosticSampler(source, TimeProvider.System)), TimeProvider.System);
    private sealed class BlockingSource : IDiagnosticSource
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads, Active, Maximum;
        public async Task<DiagnosticReading> ReadAsync(CancellationToken cancellationToken)
        {
            Reads++;
            Active++;
            Maximum = Math.Max(Maximum, Active);
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("La lecture doit être annulée.");
            }
            finally { Active--; }
        }
    }
}
