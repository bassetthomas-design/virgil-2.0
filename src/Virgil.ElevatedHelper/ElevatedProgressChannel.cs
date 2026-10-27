using System.Text.Json;
using Virgil.Contracts;
using Virgil.Contracts.Elevated;

namespace Virgil.ElevatedHelper;

internal sealed class ElevatedProgressChannel : IProgress<InterventionProgress>, IDisposable
{
    private readonly ElevatedRequestValidator _validator;
    private readonly ValidatedElevatedRequest _request;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Timer _timer;
    private readonly object _sync = new();
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    private InterventionProgress _last = new() { Step = "Préparation" };
    private bool _disposed;

    public ElevatedProgressChannel(ElevatedRequestValidator validator, ValidatedElevatedRequest request)
    {
        _validator = validator;
        _request = request;
        _timer = new Timer(_ => Tick(), null, 0, 500);
        Tick(); // Une demande déjà présente bloque la première commande, sans attendre le premier tick.
    }

    public CancellationToken Token => _cancel.Token;
    public void Report(InterventionProgress value) { lock (_sync) _last = value; Tick(); }

    private void Tick()
    {
        lock (_sync)
        {
            if (_disposed) return;
            try
            {
                _validator.ValidateProtocolRoot(_request.LocalAppDataDirectory);
                var stem = Path.Combine(_request.RootDirectory, $"intervention-{_request.Request.Nonce}");
                var cancel = Guard(stem + ".cancel");
                if (File.Exists(cancel)) _cancel.Cancel();
                var target = Guard(stem + ".progress.json");
                var temporary = Guard(stem + ".progress.tmp");
                // CreateNew refuses pre-existing hardlinks or files. Never overwrite a temporary file.
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, _last with
                    {
                        Nonce = _request.Request.Nonce, Elapsed = DateTimeOffset.UtcNow - _started
                    });
                    stream.Flush(true);
                }
                Guard(target);
                File.Move(temporary, target, overwrite: true);
            }
            catch { _cancel.Cancel(); } // A compromised/unavailable channel blocks further privileged work.
        }
    }

    private string Guard(string path) => _validator.PathGuard.ValidateFilePath(
        _request.LocalAppDataDirectory, path, ElevatedPathExistence.Optional);

    public void Dispose()
    {
        lock (_sync) { _disposed = true; _timer.Dispose(); _cancel.Dispose(); }
    }
}
