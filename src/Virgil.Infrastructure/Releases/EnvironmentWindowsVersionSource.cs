using Virgil.Core.Releases;

namespace Virgil.Infrastructure.Releases;

public sealed class EnvironmentWindowsVersionSource : IWindowsVersionSource
{
    public Version Read() => Environment.OSVersion.Version;
}
