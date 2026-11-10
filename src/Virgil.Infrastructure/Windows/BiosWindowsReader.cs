using Virgil.Core.Processes;
using Virgil.Core.Updates;

namespace Virgil.Infrastructure.Windows;

// Échafaudage du commit de tests : état actuel, sans lecture ni effet nouveau.
public sealed class BiosWindowsReader : IBiosInformationReader
{
    public BiosWindowsReader(IProcessRunner runner) => _ = runner;

    public Task<BiosInformation> ReadAsync(CancellationToken token) =>
        Task.FromResult(new BiosInformation(string.Empty, string.Empty));
}
