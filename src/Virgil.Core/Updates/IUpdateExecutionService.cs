using Virgil.Domain;

namespace Virgil.Core.Updates;

public interface IUpdateExecutionService
{
    Task<UpdateExecutionResult> ExecuteAsync(UpdateItem item, CancellationToken cancellationToken);

    UpdateExecutionResult Skip(UpdateItem item);

    UpdateExecutionResult Cancel(UpdateItem item);

    UpdateSessionReport CreateReport(
        DateTimeOffset startedAt,
        IReadOnlyList<UpdateExecutionResult> results,
        IReadOnlyList<string> errors);
}
