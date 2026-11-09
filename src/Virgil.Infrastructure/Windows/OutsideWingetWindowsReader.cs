using Virgil.Core.Processes;
using Virgil.Core.Updates;

namespace Virgil.Infrastructure.Windows;

public sealed class OutsideWingetWindowsReader(IProcessRunner runner) : IOutsideWingetReader
{
    public Task<OutsideWingetSnapshot> ReadAsync(CancellationToken token)
    {
        _ = runner;
        return Task.FromResult(new OutsideWingetSnapshot([], [], false));
    }
}
