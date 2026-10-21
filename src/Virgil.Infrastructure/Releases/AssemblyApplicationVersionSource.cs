using System.Reflection;
using Virgil.Core.Releases;

namespace Virgil.Infrastructure.Releases;

public sealed class AssemblyApplicationVersionSource(Assembly assembly) : IApplicationVersionSource
{
    public string? ReadInformationalVersion() => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
}
