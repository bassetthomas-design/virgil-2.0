using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Windows.Threading;
using Virgil.App.Hosting;
using Virgil.Core.Settings;
using Virgil.Infrastructure.Settings;
using Virgil.Infrastructure.Storage;
using Xunit;

namespace Virgil.Tests;

public sealed class DiagnosticsCompositionRootTests
{
    [Fact]
    public void SettingsAreSingletonAndTypedLoggerResolvesWithoutNotifierConstruction()
    {
        var root = Path.Combine(Path.GetTempPath(), "VirgilTests", Guid.NewGuid().ToString("N"));
        var paths = new VirgilPaths(root, root);
        var settings = new JsonSettingsService(paths);
        using var logger = new LoggerConfiguration().CreateLogger();
        var services = new ServiceCollection();
        services.AddSerilog(logger, dispose: false);
        services.AddVirgilDiagnostics(paths, settings, Dispatcher.CurrentDispatcher);
        services.AddVirgilServices();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.Same(settings, provider.GetRequiredService<ISettingsService>());
        Assert.Same(provider.GetRequiredService<ISettingsService>(), provider.GetRequiredService<ISettingsService>());
        Assert.NotNull(provider.GetRequiredService<ILogger<ExceptionReporter>>());
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, x => x.ServiceType == typeof(IUserNotifier)).Lifetime);
        Assert.False(Directory.Exists(root));
    }
}
