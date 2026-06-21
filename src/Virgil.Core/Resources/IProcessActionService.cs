using Virgil.Domain;

namespace Virgil.Core.Resources;

public interface IProcessActionService
{
    bool CanReleaseInactiveMemory { get; }

    Task<ProcessActionResult> ExecuteAsync(
        ProcessActionKind action,
        ProcessResourceInfo? target,
        bool confirmed,
        bool reinforcedConfirmation,
        CancellationToken cancellationToken);

    ProcessActionResult Skip(ProcessActionKind action, string target);
}
