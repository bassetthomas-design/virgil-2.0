using Virgil.Contracts;

namespace Virgil.Core.Interventions;

public interface IInterventionCatalog
{
    IReadOnlyList<InterventionDefinition> GetAll();

    InterventionDefinition Get(InterventionId id);
}
