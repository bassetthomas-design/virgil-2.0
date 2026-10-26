using System.Reflection;
using Virgil.Core.Actions;
using Virgil.Core.Applications;
using Virgil.Core.Cleanup;
using Virgil.Core.Interventions;
using Virgil.Core.Resources;
using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

public sealed class ConfirmedPlanModuleArchitectureTests
{
    [Fact]
    public void AllFiveModuleEffectEntryPointsRequirePlanWithoutBooleanBypass()
    {
        var modules = new[]
        {
            (typeof(ApplicationUninstallService), "LaunchOfficialUninstallAsync"),
            (typeof(ApplicationUninstallService), "LaunchBatchAsync"),
            (typeof(IProcessActionService), "ExecuteAsync"), (typeof(ProcessActionService), "ExecuteAsync"),
            (typeof(IUpdateExecutionService), "ExecuteAsync"), (typeof(WingetUpdateExecutionService), "ExecuteAsync"),
            (typeof(IInterventionExecutionService), "ExecuteAsync"), (typeof(InterventionExecutionService), "ExecuteAsync"),
            (typeof(ICleanupExecutionService), "ExecuteZoneAsync"), (typeof(CleanupExecutionService), "ExecuteZoneAsync")
        };
        foreach (var (type, name) in modules)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            var executions = methods.Where(method => method.Name == name).ToArray();
            Assert.NotEmpty(executions);
            Assert.Equal(type == typeof(IInterventionExecutionService) || type == typeof(InterventionExecutionService) ? 2 : 1, executions.Length);
            Assert.All(executions, execution => Assert.Contains(execution.GetParameters(), p => p.ParameterType == typeof(ConfirmedPlan)));
            Assert.All(methods, m => Assert.DoesNotContain(m.GetParameters(), p => p.ParameterType == typeof(bool)));
        }
    }

    [Fact]
    public void ExplorerAdapterHasOnlyTwoGuardedConsumers()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Virgil.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var source = Path.Combine(root.FullName, "src", "Virgil.Core");
        var consumers = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => File.ReadAllText(path).Contains("IExplorerRestarter", StringComparison.Ordinal))
            .Select(Path.GetFileName).OrderBy(name => name).ToArray();
        Assert.Equal(new[] { "ExplorerRestarter.cs", "IExplorerRestarter.cs", "InterventionExecutionService.cs", "ProcessActionService.cs" }, consumers);
    }
}
