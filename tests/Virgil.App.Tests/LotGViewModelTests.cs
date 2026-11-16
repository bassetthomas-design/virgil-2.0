using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Virgil.App.Hosting;
using Virgil.Core.Processes;
using Xunit;
using static Virgil.Tests.LotGLinkTests;

namespace Virgil.Tests;

public sealed class LotGViewModelTests
{
    internal sealed class Runner : IProcessRunner
    {
        public List<ProcessLaunchRequest> Launches { get; } = [];
        public Func<Task<ProcessLaunchResult>> Result { get; set; } = () => Task.FromResult(new ProcessLaunchResult(true, null));
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected run");
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken)
        { Launches.Add(request); return Result(); }
    }
    internal static object Create(Runner runner) => Activator.CreateInstance(Type("BugReportViewModel"), runner, Context())!;
    internal static IAsyncRelayCommand Command(object vm) => Assert.IsAssignableFrom<IAsyncRelayCommand>(Read(vm, "OpenCommand"));
    internal static void Screen(object vm, Func<string> screen) => vm.GetType().GetProperty("ScreenSource")!.SetValue(vm, screen);
    [Fact]
    public async Task Launch_uses_only_port_and_double_execution_is_guarded()
    {
        var runner = new Runner(); var completion = new TaskCompletionSource<ProcessLaunchResult>(); runner.Result = () => completion.Task;
        var vm = Create(runner); Assert.Empty(runner.Launches); Screen(vm, () => "Réseau"); Assert.Empty(runner.Launches);
        var command = Command(vm); var first = command.ExecuteAsync(null); Assert.False(command.CanExecute(null));
        var second = command.ExecuteAsync(null);
        var request = Assert.Single(runner.Launches); Assert.Equal(Build(Context(), "Réseau"), request.FileName);
        Assert.StartsWith(Prefix, request.FileName, StringComparison.Ordinal); Assert.Empty(request.Arguments); Assert.True(request.UseShellExecute);
        Assert.Null(request.Verb); Assert.Null(request.RawArguments); Assert.False(request.WaitForExit); Assert.Equal(ProcessWindowMode.Normal, request.WindowMode);
        completion.SetResult(new ProcessLaunchResult(true, null)); await Task.WhenAll(first, second); Assert.True(command.CanExecute(null));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Every_failure_displays_exact_url_then_success_clears(int failure)
    {
        var runner = new Runner { Result = () => failure switch
        {
            0 => throw new InvalidOperationException("SENTINELLE-EXCEPTION-6X7"),
            1 => Task.FromResult(new ProcessLaunchResult(false, null)),
            _ => Task.FromResult(new ProcessLaunchResult(true, null, "SENTINELLE-ERREUR-9K3"))
        }};
        var vm = Create(runner); await Command(vm).ExecuteAsync(null);
        Assert.Equal(Failure, Read(vm, "ErrorText")); Assert.Equal(Build(Context(), "non déterminé"), Read(vm, "FailedUrl")); Assert.Equal(true, Read(vm, "HasError"));
        runner.Result = () => Task.FromResult(new ProcessLaunchResult(true, null)); await Command(vm).ExecuteAsync(null);
        Assert.Equal("", Read(vm, "ErrorText")); Assert.Equal("", Read(vm, "FailedUrl")); Assert.Equal(false, Read(vm, "HasError"));
    }
    [Fact]
    public void Constructor_and_singleton_composition_are_explicit()
    {
        var type = Type("BugReportViewModel"); var constructor = Assert.Single(type.GetConstructors());
        Assert.Equal(new[] { typeof(IProcessRunner), Type("BugReportContext") }, constructor.GetParameters().Select(p => p.ParameterType));
        var services = new ServiceCollection().AddVirgilServices().AddVirgilViews();
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, d => d.ServiceType == type).Lifetime);
        using var provider = services.BuildServiceProvider(); var vm = provider.GetRequiredService(type); Assert.Same(vm, provider.GetRequiredService(type));
    }
    [Fact]
    public void Production_sources_have_no_forbidden_effects_and_wiring_is_in_views_only()
    {
        var root = TestRepositoryRoot.FindRepositoryRoot();
        var paths = Directory.GetFiles(Path.Combine(root, "src/Virgil.App/ViewModels"), "BugReport*.cs"); Assert.Equal(3, paths.Length);
        foreach (var path in paths.Append(Path.Combine(root, "src/Virgil.App/Controls/BugReportPanel.xaml.cs")))
        {
            var source = File.ReadAllText(path);
            // Ordinal case-sensitive substrings, as required by P1; none target annex text.
            foreach (var forbidden in new[] { "UserName", "MachineName", "UserDomainName", "GetFolderPath", "GetEnvironmentVariable", "USERPROFILE", "Registry", "HttpClient", "System.Net", "WebUtility", "HttpUtility", "Dns", "NetworkInterface", "IPAddress", "File.", "Directory.", "Serilog", "ILogger", "Log.", "Process.", "ProcessStartInfo", ".RunAsync(", "Clipboard", "Random", "Environment" })
                Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
            Assert.DoesNotContain("runas", source, StringComparison.OrdinalIgnoreCase);
        }
        var main = File.ReadAllText(Path.Combine(root, "src/Virgil.App/MainWindow.xaml.cs"));
        Assert.Matches(@"ReleasePanelHost\.Children\.Add\(releaseView\);\s*ReleasePanelHost\.Children\.Add\(bugReportPanel\);", main);
        Assert.Contains("ScreenSource", main, StringComparison.Ordinal); Assert.Contains("BugReportScreen.From", main, StringComparison.Ordinal);
        Assert.Contains("NavigationPanel.Children.OfType<Button>()", main, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.GetName(b)", main, StringComparison.Ordinal); Assert.Contains("Equals(b.Tag, \"Active\")", main, StringComparison.Ordinal);
        var composition = File.ReadAllText(Path.Combine(root, "src/Virgil.App/Hosting/ServiceCollectionExtensions.cs"));
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(composition, @"Environment\.OSVersion\.Version").Count);
        var views = composition[composition.IndexOf("public static IServiceCollection AddVirgilViews", StringComparison.Ordinal)..];
        Assert.Contains("AddSingleton<BugReportViewModel>", views, StringComparison.Ordinal); Assert.Contains("AddSingleton<BugReportPanel>", views, StringComparison.Ordinal);
        Assert.Contains("BugReportContext.From", views, StringComparison.Ordinal); Assert.Contains("Environment.OSVersion.Version", views, StringComparison.Ordinal);
        var xaml = File.ReadAllText(Path.Combine(root, "src/Virgil.App/MainWindow.xaml"));
        Assert.DoesNotContain("TextBox", xaml, StringComparison.Ordinal); Assert.DoesNotContain("BugReport", xaml, StringComparison.Ordinal);
    }
}
