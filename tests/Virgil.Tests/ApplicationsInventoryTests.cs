using Virgil.Core.Applications;
using Virgil.Domain.Applications;
using Xunit;

namespace Virgil.Tests;

public sealed class ApplicationsInventoryTests
{
    [Fact]
    public async Task InventoryAsync_MergesSourcesAndClassifiesOfficialUninstall()
    {
        var service = new ApplicationInventoryService(
            [
                new FakeApplicationReader("Registry", new[]
                {
                    new InstalledApplication
                    {
                        Id = "registry-vlc",
                        DisplayName = "VLC media player",
                        Publisher = "VideoLAN",
                        Version = "3.0",
                        Source = ApplicationInventorySource.Registry,
                        UninstallKind = ApplicationUninstallKind.RegistryUninstallString,
                        UninstallCommand = @"C:\Program Files\VideoLAN\VLC\uninstall.exe"
                    }
                }),
                new FakeApplicationReader("WinGet", new[]
                {
                    new InstalledApplication
                    {
                        Id = "winget-vlc",
                        DisplayName = "VLC media player",
                        Publisher = "VideoLAN",
                        Version = "3.0.20",
                        Source = ApplicationInventorySource.Winget,
                        WingetId = "VideoLAN.VLC",
                        UninstallKind = ApplicationUninstallKind.Winget
                    }
                })
            ],
            new ApplicationRiskClassifier(),
            new ApplicationIconExtractor());

        var report = await service.InventoryAsync(null, CancellationToken.None);

        var application = Assert.Single(report.Applications);
        Assert.True(application.CanUninstall);
        Assert.Equal(ApplicationRiskLevel.SafeToUninstall, application.RiskLevel);
        Assert.Contains(ApplicationInventorySource.Registry, application.Sources);
        Assert.Contains(ApplicationInventorySource.Winget, application.Sources);
        Assert.Equal("VideoLAN.VLC", application.WingetId);
    }

    [Fact]
    public async Task InventoryAsync_ProtectsDriversAndSecurityComponents()
    {
        var service = new ApplicationInventoryService(
            [
                new FakeApplicationReader("Registry", new[]
                {
                    new InstalledApplication
                    {
                        Id = "nvidia-driver",
                        DisplayName = "NVIDIA Graphics Driver",
                        Publisher = "NVIDIA",
                        Source = ApplicationInventorySource.Registry,
                        UninstallKind = ApplicationUninstallKind.RegistryUninstallString,
                        UninstallCommand = @"C:\Program Files\NVIDIA Corporation\Installer2\setup.exe"
                    },
                    new InstalledApplication
                    {
                        Id = "security-agent",
                        DisplayName = "Contoso Security Agent",
                        Publisher = "Contoso",
                        Source = ApplicationInventorySource.Registry,
                        UninstallKind = ApplicationUninstallKind.RegistryUninstallString,
                        UninstallCommand = @"C:\Program Files\Contoso\Security\uninstall.exe"
                    }
                })
            ],
            new ApplicationRiskClassifier(),
            new ApplicationIconExtractor());

        var report = await service.InventoryAsync(null, CancellationToken.None);

        Assert.Equal(2, report.ProtectedCount);
        Assert.All(report.Applications, app =>
        {
            Assert.Equal(ApplicationRiskLevel.Protected, app.RiskLevel);
            Assert.False(app.CanUninstall);
        });
    }

    [Fact]
    public async Task InventoryAsync_KeepsStoreAppsReadOnly()
    {
        var service = new ApplicationInventoryService(
            [
                new FakeApplicationReader("Store", new[]
                {
                    new InstalledApplication
                    {
                        Id = "store-app",
                        DisplayName = "Photo Editor Store",
                        Publisher = "Example",
                        Source = ApplicationInventorySource.Store,
                        UninstallKind = ApplicationUninstallKind.StoreSettings,
                        StorePackageFullName = "Example.PhotoEditor_1.0.0.0_x64__abc"
                    }
                })
            ],
            new ApplicationRiskClassifier(),
            new ApplicationIconExtractor());

        var report = await service.InventoryAsync(null, CancellationToken.None);

        var application = Assert.Single(report.Applications);
        Assert.False(application.CanUninstall);
        Assert.Equal(ApplicationRiskLevel.Unknown, application.RiskLevel);
        Assert.Equal(ApplicationStatus.Unknown, application.Status);
    }

    private sealed class FakeApplicationReader : IApplicationInventorySourceReader
    {
        private readonly IReadOnlyList<InstalledApplication> _applications;

        public FakeApplicationReader(string sourceName, IReadOnlyList<InstalledApplication> applications)
        {
            SourceName = sourceName;
            _applications = applications;
        }

        public string SourceName { get; }

        public Task<ApplicationInventorySourceResult> ReadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new ApplicationInventorySourceResult(_applications, Array.Empty<string>()));
        }
    }
}
