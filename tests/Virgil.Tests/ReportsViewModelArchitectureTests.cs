using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Virgil.App.Hosting;
using Virgil.App.Services;
using Virgil.App.ViewModels;
using Xunit;

namespace Virgil.Tests;

public sealed class ReportsViewModelArchitectureTests
{
    [Fact]
    public void ViewModelsDoNotDependOnWpfOrConstructServices()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Virgil.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var files = Directory.GetFiles(Path.Combine(directory.FullName, "src", "Virgil.App", "ViewModels"), "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        var forbidden = new Regex(@"using\s+System\.Windows|new\s+\w+Service\s*\(|MessageBox");
        Assert.All(files, file => Assert.DoesNotMatch(forbidden, File.ReadAllText(file)));
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void CompositionRootResolvesReportsViewModelAndDialogAsExplicitSingletons()
    {
        var services = new ServiceCollection().AddVirgilServices().AddVirgilViews();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        foreach (var type in new[] { typeof(ReportsHistoryViewModel), typeof(IFileSaveDialog) })
        {
            var registration = Assert.Single(services, descriptor => descriptor.ServiceType == type);
            Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
            Assert.NotNull(registration.ImplementationFactory);
            Assert.Same(provider.GetRequiredService(type), provider.GetRequiredService(type));
        }
        Assert.IsType<SaveFileDialogService>(provider.GetRequiredService<IFileSaveDialog>());
    }
}
