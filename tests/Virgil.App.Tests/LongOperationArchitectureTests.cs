using System.Reflection;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Input;
using Virgil.App.ViewModels;
using Xunit;
using static Virgil.Tests.TestRepositoryRoot;

namespace Virgil.Tests;

public sealed class AppLongOperationArchitectureTests
{
    // Explicit inventory: Scan still lives in MainWindow; the other thirteen entries
    // are ViewModel operations (including Diagnostic's action preview).
    public static TheoryData<string, string, string> LongOperations => new()
    {
        { "MainWindow", "RunScanAsync", "MainWindow.xaml" },
        { "DiagnosticViewModel", "RefreshAsync", "Views/DiagnosticView.xaml" },
        { "DiagnosticViewModel", "PrepareActionAsync", "Views/DiagnosticView.xaml" },
        { "UpdatesViewModel", "ScanUpdatesAsync", "Views/UpdatesView.xaml" },
        { "UpdatesViewModel", "InspectDriversAsync", "Views/UpdatesView.xaml" },
        { "UpdatesViewModel", "RunSelectedAsync", "Views/UpdatesView.xaml" },
        { "UpdatesViewModel", "RunAllAsync", "Views/UpdatesView.xaml" },
        { "UpdatesViewModel", "InstallWingetModuleAsync", "Views/UpdatesView.xaml" },
        { "InterventionsViewModel", "VerifyHealthAsync", "Views/InterventionsView.xaml" },
        { "InterventionsViewModel", "ScanRepairAsync", "Views/InterventionsView.xaml" },
        { "InterventionsViewModel", "AnalyzeAsync", "Views/InterventionsView.xaml" },
        { "InterventionsViewModel", "RunGuidedAsync", "Views/InterventionsView.xaml" },
        { "ApplicationsViewModel", "AnalyzeAsync", "Views/ApplicationsView.xaml" },
        { "ApplicationsViewModel", "LaunchUninstallAsync", "Views/ApplicationsView.xaml" },
        { "ApplicationsViewModel", "UninstallSelectionAsync", "Views/ApplicationsView.xaml" }
    };

    [Theory]
    [MemberData(nameof(LongOperations))]
    public void LongOperationsExposeCancel(string owner, string operation, string view)
    {
        var assembly = typeof(LongOperationViewModel).Assembly;
        var type = Assert.Single(assembly.GetTypes(), t => t.Name == owner);
        var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name == operation).ToArray();
        Assert.NotEmpty(methods);
        Assert.All(methods, method => Assert.True(typeof(Task).IsAssignableFrom(method.ReturnType), $"{owner}.{operation} must remain async"));
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var hasOperation = fields.Any(f => f.FieldType == typeof(LongOperationViewModel)) || properties.Any(p => p.PropertyType == typeof(LongOperationViewModel));
        Assert.True(hasOperation || methods.All(m => m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken))), $"{owner}.{operation}: missing cancellation contract");
        if (owner != "MainWindow" && operation != "PrepareActionAsync" && operation != "ScanUpdatesAsync")
        {
            var commandName = operation.Replace("Async", "") + "Command";
            Assert.True(properties.Any(p => p.Name == commandName && typeof(IAsyncRelayCommand).IsAssignableFrom(p.PropertyType)), $"{owner}.{operation}: {commandName} missing");
        }
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Virgil.App", view));
        Assert.True(HasBoundPanel(xaml, owner == "MainWindow"), $"{owner}.{operation}: {view} missing bound OperationProgressPanel");
        if (owner == "MainWindow")
            Assert.Contains("ScanProgressPanel.DataContext = _scanOperation;", File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Virgil.App", "MainWindow.xaml.cs")));
    }

    private static bool HasBoundPanel(string xaml, bool scan) => XDocument.Parse(xaml).Descendants().Any(element =>
        element.Name.LocalName == "OperationProgressPanel" &&
        (scan ? (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "ScanProgressPanel"
              : (string?)element.Attribute("DataContext") == "{Binding Operation}"));

    [Theory]
    [InlineData("Views/DiagnosticView.xaml")]
    [InlineData("Views/UpdatesView.xaml")]
    [InlineData("Views/InterventionsView.xaml")]
    [InlineData("Views/ApplicationsView.xaml")]
    [InlineData("MainWindow.xaml")]
    public void MissingOrUnboundPanelIsRejected(string view)
    {
        var doc = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "Virgil.App", view));
        var panels = doc.Descendants().Where(e => e.Name.LocalName == "OperationProgressPanel").ToArray();
        Assert.NotEmpty(panels);
        foreach (var panel in panels) panel.Remove();
        Assert.False(HasBoundPanel(doc.ToString(), view == "MainWindow.xaml"));
        Assert.False(HasBoundPanel("<View><OperationProgressPanel /></View>", false));
    }
}
