using Virgil.Contracts.Elevated;

namespace Virgil.Core.Interventions;

public interface IElevatedProtocolRoot
{
    ElevatedPathGuard PathGuard { get; }

    string EnsureCreated(string localAppData);

    string ValidateExisting(string localAppData);
}
