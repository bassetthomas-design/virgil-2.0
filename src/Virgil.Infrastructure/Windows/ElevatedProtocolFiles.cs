using System.Text.Json;
using Virgil.Contracts;
using Virgil.Core.Interventions;

namespace Virgil.Infrastructure.Windows;

public sealed class ElevatedProtocolFiles : IElevatedProtocolFiles
{
    public async Task WriteRequestAsync(string path, ElevatedInterventionRequest request,
        JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough
        });
        await JsonSerializer.SerializeAsync(stream, request, options, cancellationToken)
            .ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    public async Task<ElevatedInterventionResult?> ReadResultAsync(string path,
        JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        return await JsonSerializer.DeserializeAsync<ElevatedInterventionResult>(
            stream, options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InterventionProgress?> ReadProgressAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return await JsonSerializer.DeserializeAsync<InterventionProgress>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public void RequestCancel(string path)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Flush(true);
    }

    public bool Exists(string path) => File.Exists(path);

    public void Delete(string path) => File.Delete(path);
}
