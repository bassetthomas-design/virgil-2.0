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

    public bool Exists(string path) => File.Exists(path);

    public void Delete(string path) => File.Delete(path);
}
