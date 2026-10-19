using System.Text.Json;
using Virgil.Contracts;

namespace Virgil.Core.Interventions;

public interface IElevatedProtocolFiles
{
    Task WriteRequestAsync(string path, ElevatedInterventionRequest request,
        JsonSerializerOptions options, CancellationToken cancellationToken);

    Task<ElevatedInterventionResult?> ReadResultAsync(string path,
        JsonSerializerOptions options, CancellationToken cancellationToken);

    bool Exists(string path);

    void Delete(string path);
}
