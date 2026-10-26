using System.Text.Json;
using Virgil.Contracts;

namespace Virgil.Core.Interventions;

public interface IElevatedProtocolFiles
{
    Task WriteRequestAsync(string path, ElevatedInterventionRequest request,
        JsonSerializerOptions options, CancellationToken cancellationToken);

    Task<ElevatedInterventionResult?> ReadResultAsync(string path,
        JsonSerializerOptions options, CancellationToken cancellationToken);

    Task<InterventionProgress?> ReadProgressAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult<InterventionProgress?>(null);

    void RequestCancel(string path) { }

    bool Exists(string path);

    void Delete(string path);
}
