using Virgil.Core.Updates;

namespace Virgil.Tests;

internal sealed class EmptyPendingRebootProbe : IPendingRebootProbe
{
    public PendingRebootStatus Read() => new(false, []);
}
