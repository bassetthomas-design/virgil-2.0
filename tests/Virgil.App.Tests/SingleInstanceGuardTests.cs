using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class SingleInstanceGuardTests
{
    private static (string Mutex, string Event) Names()
    {
        var name = @"Local\Virgil.Tests." + Guid.NewGuid().ToString("N");
        return (name, name + ".Activate");
    }

    // A distinct thread models a second process without recursive ownership of the Windows mutex.
    private static void OnOtherThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void OnlyOneOwnerAndDisposeAllowsNewOwner()
    {
        var names = Names();
        using (var first = new SingleInstanceGuard(names.Mutex, names.Event))
        {
            Assert.True(first.TryAcquire());
            OnOtherThread(() =>
            {
                using var second = new SingleInstanceGuard(names.Mutex, names.Event);
                Assert.False(second.TryAcquire());
            });
        }
        using var next = new SingleInstanceGuard(names.Mutex, names.Event);
        Assert.True(next.TryAcquire());
    }

    [Fact]
    public void SignalWithoutOwnerReturnsFalse()
    {
        var names = Names();
        using var guard = new SingleInstanceGuard(names.Mutex, names.Event);
        Assert.False(guard.SignalExisting());
    }

    [Fact]
    public void ListenerReceivesRepeatedSignals()
    {
        var names = Names();
        using var first = new SingleInstanceGuard(names.Mutex, names.Event);
        using var received = new AutoResetEvent(false);
        Assert.True(first.TryAcquire());
        first.StartListening(() => received.Set());
        using var second = new SingleInstanceGuard(names.Mutex, names.Event);
        for (var i = 0; i < 2; i++)
        {
            Assert.True(second.SignalExisting());
            Assert.True(received.WaitOne(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void DisposeIsIdempotentAndStopsListener()
    {
        var names = Names();
        using var first = new SingleInstanceGuard(names.Mutex, names.Event);
        using var received = new AutoResetEvent(false);
        Assert.True(first.TryAcquire());
        first.StartListening(() => received.Set());
        first.Dispose();
        first.Dispose();
        using var second = new SingleInstanceGuard(names.Mutex, names.Event);
        Assert.False(second.SignalExisting());
        Assert.False(received.WaitOne(TimeSpan.FromMilliseconds(100)));
        Assert.True(second.TryAcquire());
        Assert.True(second.SignalExisting());
        Assert.False(received.WaitOne(TimeSpan.FromMilliseconds(100)));
    }

    private sealed class NativeThreadExit : WaitHandle
    {
        public void OpenCurrentThread()
        {
            SafeWaitHandle = OpenThread(0x00100000, false, GetCurrentThreadId()); // SYNCHRONIZE
            Assert.False(SafeWaitHandle.IsInvalid);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeWaitHandle OpenThread(
            uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint threadId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }

    [Fact]
    public void AbandonedMutexIsAcquired()
    {
        var names = Names();
        using var mutex = new Mutex(false, names.Mutex);
        using var nativeExit = new NativeThreadExit();
        OnOtherThread(() =>
        {
            // Retain the native thread handle before it can exit and its ID can be reused.
            nativeExit.OpenCurrentThread();
            Assert.True(mutex.WaitOne(0));
        });
        // Join observes managed completion, which can precede native thread teardown and
        // mutex abandonment. A signaled OS thread handle guarantees teardown completed.
        Assert.True(nativeExit.WaitOne(TimeSpan.FromSeconds(5)));
        using var guard = new SingleInstanceGuard(names.Mutex, names.Event);
        Assert.True(guard.TryAcquire());
    }
}
