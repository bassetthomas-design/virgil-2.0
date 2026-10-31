namespace Virgil.Core.Updates;

public sealed record PendingRebootStatus(bool ServicePresent, IReadOnlyList<string> Sources);
public interface IPendingRebootProbe
{
    PendingRebootStatus Read();
}
