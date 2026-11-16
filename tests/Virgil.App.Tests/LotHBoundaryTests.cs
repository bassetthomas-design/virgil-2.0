using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Virgil.App.Hosting;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Processes;
using Virgil.Core.Releases;
using Virgil.Core.Status;
using Virgil.Infrastructure.Releases;
using Xunit;
using static Virgil.Tests.LotGLinkTests;

namespace Virgil.Tests;

public sealed class LotHBoundaryTests
{
    private static System.Type Port()
    {
        var port = typeof(ApplicationVersionService).Assembly.GetType("Virgil.Core.Releases.IWindowsVersionSource");
        Assert.NotNull(port); return port;
    }
    [Fact]
    public void Windows_port_and_lightweight_adapter_have_exact_contract_and_source_boundary()
    {
        var port = Port(); Assert.True(port.IsInterface);
        var read = Assert.IsAssignableFrom<MethodInfo>(Assert.Single(port.GetMembers()));
        Assert.Equal("Read", read.Name); Assert.Empty(read.GetParameters()); Assert.Equal(typeof(Version), read.ReturnType);
        var adapter = typeof(AssemblyApplicationVersionSource).Assembly.GetType("Virgil.Infrastructure.Releases.EnvironmentWindowsVersionSource");
        Assert.NotNull(adapter); Assert.True(adapter.IsSealed); Assert.True(port.IsAssignableFrom(adapter));
        Assert.NotNull(adapter.GetConstructor(System.Type.EmptyTypes));
        Assert.Equal(Environment.OSVersion.Version, read.Invoke(Activator.CreateInstance(adapter), null));
        var root = TestRepositoryRoot.FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src/Virgil.Infrastructure/Releases/EnvironmentWindowsVersionSource.cs"));
        Assert.Equal(2, source.Split("Environment.OSVersion.Version", StringSplitOptions.None).Length);
        Assert.DoesNotContain("Environment", File.ReadAllText(Path.Combine(root, "src/Virgil.Core/Releases/IWindowsVersionSource.cs")), StringComparison.Ordinal);
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src/Virgil.App"), "*", SearchOption.AllDirectories)
            .Where(p => (p.EndsWith(".cs", StringComparison.Ordinal) || p.EndsWith(".xaml", StringComparison.Ordinal))
                && !p.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin")))
            Assert.DoesNotContain("OSVersion", File.ReadAllText(path), StringComparison.Ordinal);
        var composition = File.ReadAllText(Path.Combine(root, "src/Virgil.App/Hosting/ServiceCollectionExtensions.cs"));
        Assert.Contains("sp.GetRequiredService<IWindowsVersionSource>().Read()", composition[composition.IndexOf("public static IServiceCollection AddVirgilViews", StringComparison.Ordinal)..], StringComparison.Ordinal);
        var services = new ServiceCollection().AddVirgilServices();
        var descriptor = Assert.Single(services, d => d.ServiceType == port);
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime); Assert.NotNull(descriptor.ImplementationFactory);
        using var provider = services.BuildServiceProvider();
        Assert.Equal(adapter, provider.GetRequiredService(port).GetType());
        Assert.NotSame(provider.GetRequiredService(port), provider.GetRequiredService(port));
    }
    public class WindowsProxy : DispatchProxy
    {
        public int Reads { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.NotNull(targetMethod); Assert.Equal("Read", targetMethod.Name);
            Reads++; return new Version(10, 0, 12345, 6);
        }
    }
    [Fact]
    public async Task Real_composition_reads_fake_port_once_only_on_resolution_and_launches_only_on_command()
    {
        var services = new ServiceCollection().AddVirgilServices().AddVirgilViews();
        var runner = new LotGViewModelTests.Runner();
        var fake = DispatchProxy.Create(Port(), typeof(WindowsProxy));
        var proxy = Assert.IsAssignableFrom<WindowsProxy>(fake);
        services.AddSingleton(Port(), fake); services.AddSingleton<IProcessRunner>(runner);
        Assert.Equal(0, proxy.Reads); Assert.Empty(runner.Launches);
        using var provider = services.BuildServiceProvider();
        Assert.Equal(0, proxy.Reads); Assert.Empty(runner.Launches);
        var vm = provider.GetRequiredService<BugReportViewModel>();
        Assert.Same(vm, provider.GetRequiredService<BugReportViewModel>());
        Assert.Equal(1, proxy.Reads); Assert.Empty(runner.Launches);
        await vm.OpenCommand.ExecuteAsync(null);
        Assert.Equal("- Version de Windows : Windows 10 (10.0.12345)", DecodedBody(Assert.Single(runner.Launches).FileName).Split('\n')[2]);
        Assert.Equal(1, proxy.Reads);
    }
    [Fact]
    public async Task Unreadable_screen_still_opens_exact_unknown_screen_link()
    {
        var runner = new LotGViewModelTests.Runner(); var vm = LotGViewModelTests.Create(runner);
        LotGViewModelTests.Screen(vm, () => throw new InvalidOperationException("SENTINELLE-ECRAN-9K2"));
        await LotGViewModelTests.Command(vm).ExecuteAsync(null);
        var url = Assert.Single(runner.Launches).FileName;
        Assert.Equal("https://github.com/bassetthomas-design/virgil-2.0/issues/new?labels=bug&title=Probl%C3%A8me%20signal%C3%A9%20depuis%20l%27%C3%A9cran%20non%20d%C3%A9termin%C3%A9&body=%23%23%20Informations%0A-%20Version%20de%20Virgil%20%3A%200.8.0-alpha.8%0A-%20Version%20de%20Windows%20%3A%20Windows%2011%20%2810.0.22631%29%0A-%20%C3%89cran%20%3A%20non%20d%C3%A9termin%C3%A9%0A%0A%23%23%20Ce%20qui%20s%27est%20pass%C3%A9%0A%28D%C3%A9crivez%20le%20probl%C3%A8me%20en%20quelques%20mots.%29%0A%0A%23%23%20Ce%20qui%20%C3%A9tait%20attendu%0A%28D%C3%A9crivez%20ce%20que%20vous%20pensiez%20voir.%29%0A%0A%23%23%20%C3%89tapes%20pour%20reproduire%0A1.%0A2.%0A3.%0A%0AAvant%20d%27envoyer%20%3A%20ne%20collez%20ni%20mot%20de%20passe%2C%20ni%20chemin%20de%20dossier%20personnel%2C%20ni%20nom%20d%27ordinateur.", url);
        Assert.False((bool)Read(vm, "HasError")); Assert.Equal("", Read(vm, "ErrorText")); Assert.Equal("", Read(vm, "FailedUrl"));
        Assert.DoesNotContain("SENTINELLE-ECRAN-9K2", url, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Backup_failure_is_visible_and_next_success_clears_it(int failure)
    {
        var store = new SystemStatusStore(); var runner = new LotGViewModelTests.Runner { Result = () => failure switch
        {
            0 => Task.FromResult(new ProcessLaunchResult(false, null)),
            1 => Task.FromResult(new ProcessLaunchResult(true, null, "Accès refusé.")),
            _ => throw new InvalidOperationException("Accès refusé.")
        }};
        using var vm = new HomePrioritiesViewModel(store, runner, TimeProvider.System);
        store.PublishScan(LotFPrioritiesTests.Report(new ScanFinding("disk.failure.0", "", "Disque", "", "", ScanSeverity.Critical, null)));
        var navigation = new List<string>(); vm.NavigationRequested += navigation.Add;
        var item = Assert.Single(vm.Items, i => i.Destination == "ms-settings:backup");
        await vm.OpenCommand.ExecuteAsync(item);
        Assert.Equal("Le réglage Sauvegarde Windows n'a pas pu être ouvert.", vm.StatusText);
        runner.Result = () => Task.FromResult(new ProcessLaunchResult(true, null)); await vm.OpenCommand.ExecuteAsync(item);
        Assert.Equal("", vm.StatusText); Assert.Equal(2, runner.Launches.Count); Assert.Empty(navigation);
        foreach (var request in runner.Launches)
        {
            Assert.Equal("ms-settings:backup", request.FileName); Assert.Empty(request.Arguments);
            Assert.True(request.UseShellExecute); Assert.Null(request.Verb); Assert.Null(request.RawArguments);
            Assert.False(request.WaitForExit); Assert.Equal(ProcessWindowMode.Normal, request.WindowMode);
        }
    }
}
