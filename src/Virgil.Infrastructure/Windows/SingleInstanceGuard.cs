using System.Runtime.InteropServices;
using System.Security;

namespace Virgil.Infrastructure.Windows;

public sealed class SingleInstanceGuard(
    string mutexName = @"Local\Virgil.SingleInstance.v2",
    string activationEventName = @"Local\Virgil.SingleInstance.v2.Activate") : IDisposable
{
    private Mutex? _mutex;
    private EventWaitHandle? _activation;
    private readonly ManualResetEvent _stop = new(false);
    private Thread? _listener;
    private bool _ownsMutex;
    private bool _disposed;

    public bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_ownsMutex) return true;
        _mutex ??= new Mutex(false, mutexName);
        try { _ownsMutex = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsMutex = true; }
        if (_ownsMutex)
            _activation = new EventWaitHandle(false, EventResetMode.AutoReset, activationEventName);
        return _ownsMutex;
    }

    public void StartListening(Action onActivate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(onActivate);
        if (!_ownsMutex) throw new InvalidOperationException("L'instance ne détient pas le mutex.");
        if (_listener is not null) throw new InvalidOperationException("L'écoute est déjà démarrée.");
        var handles = new WaitHandle[] { _stop, _activation! };
        _listener = new Thread(() =>
        {
            while (WaitHandle.WaitAny(handles) == 1)
            {
                if (_stop.WaitOne(0)) break;
                onActivate();
            }
        }) { IsBackground = true, Name = "Virgil activation" };
        _listener.Start();
    }

    public bool SignalExisting()
    {
        try
        {
            AllowSetForegroundWindow(uint.MaxValue);
            if (!EventWaitHandle.TryOpenExisting(activationEventName, out var activation)) return false;
            using (activation) return activation.Set();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
            or WaitHandleCannotBeOpenedException or PlatformNotSupportedException or ObjectDisposedException
            or DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Set();
        _listener?.Join();
        try
        {
            // Mutex ownership is thread-affine: dispose on the acquiring UI thread.
            if (_ownsMutex) _mutex!.ReleaseMutex();
        }
        finally
        {
            _ownsMutex = false;
            _activation?.Dispose();
            _mutex?.Dispose();
            _stop.Dispose();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
