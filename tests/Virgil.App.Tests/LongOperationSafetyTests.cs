using Virgil.App.ViewModels;
using Virgil.Core.Operations;
using Xunit;

namespace Virgil.Tests;

public sealed class LongOperationSafetyTests
{
    [Theory]
    [Trait("Category", "Safety")]
    [InlineData("Scan", false)]
    [InlineData("Diagnostic.Refresh", false)]
    [InlineData("Updates.Scan", false)]
    [InlineData("Updates.Drivers", true)]
    [InlineData("Updates.Selected", true)]
    [InlineData("Updates.Sequence", true)]
    [InlineData("Updates.Module", false)]
    [InlineData("Interventions.Verify", true)]
    [InlineData("Interventions.ScanRepair", true)]
    [InlineData("Interventions.Analyze", false)]
    [InlineData("Interventions.Guided", true)]
    [InlineData("Applications.Inventory", false)]
    [InlineData("Applications.Single", true)]
    [InlineData("Applications.Batch", true)]
    public async Task SharedCancelContractAcknowledgesBeforeClockAdvancesAndBlocksNext(string operation, bool atomic)
    {
        var clock = new ManualClock();
        using var cancellation = new CancellationTokenSource();
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var next = 0;
        var vm = new LongOperationViewModel(cancellation.Cancel, clock);
        vm.Start(operation, nonInterruptible: atomic);
        async Task Run()
        {
            try
            {
                if (atomic) await finish.Task;
                else await finish.Task.WaitAsync(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                next++;
            }
            catch (OperationCanceledException) { }
            finally { vm.Complete("Terminé"); }
        }
        var task = Run();
        var before = clock.GetTimestamp();
        vm.CancelCommand.Execute(null);
        Assert.True(!vm.IsRunning || vm.IsCancelling);
        Assert.InRange(clock.GetElapsedTime(before), TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Assert.False(vm.CanCancel);
        Assert.Equal(0, next);
        if (atomic)
        {
            Assert.False(task.IsCompleted);
            Assert.Contains("rien d’autre", vm.Step);
            vm.Report(new("Progression tardive", Percent: 50));
            Assert.Contains("Annulation demandée", vm.Step);
            finish.SetResult();
        }
        await task;
        Assert.Equal(0, next);
        Assert.False(vm.IsVisible);
    }

    [Fact]
    [Trait("Category", "Safety")]
    public void PanelWaitsTwoSecondsAndDeterminateTotalsUseTheSameClock()
    {
        var clock = new ManualClock();
        var vm = new LongOperationViewModel(() => { }, clock);
        vm.Start("Inventaire");
        Assert.False(vm.IsVisible);
        clock.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.False(vm.IsVisible);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(vm.IsVisible);
        Assert.Contains("00:02", vm.Timing);
        vm.Report(new("Lecteur", 2, 4));
        Assert.False(vm.IsIndeterminate);
        Assert.Equal(50, vm.Percent);
        vm.Complete("Terminé");
        vm.Start("Réparation", immediately: true, nonInterruptible: true);
        Assert.True(vm.IsVisible);
        vm.Complete("Terminé");
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private long _ticks;
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, dueTime);
            _timers.Add(timer); return timer;
        }
        public void Advance(TimeSpan duration)
        { _ticks += duration.Ticks; foreach (var timer in _timers.ToArray()) timer.Fire(); }
        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state, TimeSpan due) : ITimer
        {
            private long? _next = clock._ticks + due.Ticks;
            public bool Change(TimeSpan value, TimeSpan period)
            { _next = value == Timeout.InfiniteTimeSpan ? null : clock._ticks + value.Ticks; return true; }
            public void Fire() { if (_next is { } next && clock._ticks >= next) callback(state); }
            public void Dispose() => _next = null;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
