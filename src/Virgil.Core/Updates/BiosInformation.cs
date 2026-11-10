namespace Virgil.Core.Updates;

public sealed record BiosInformation(string Manufacturer, string Version);

public interface IBiosInformationReader
{
    Task<BiosInformation> ReadAsync(CancellationToken token);
}
