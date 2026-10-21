using Microsoft.Win32;
using Virgil.Core.Diagnostic;
using Virgil.Infrastructure.Windows.Diagnostic;
using Xunit;

namespace Virgil.Tests;

public sealed class DiagnosticStartupTests
{
    [Fact]
    public void UserRunIsReadOnceInDefaultViewAndProducesOneFinding()
    {
        var reads = new List<(RegistryHive Hive, RegistryView View)>();
        var startup = WindowsDiagnosticSignals.ReadStartup(CancellationToken.None, (hive, view, _) =>
        {
            reads.Add((hive, view));
            return hive == RegistryHive.CurrentUser ? new[] { "Launcher" } : Array.Empty<string>();
        }, (_, _) => Array.Empty<string>());
        Assert.True(startup.IsAvailable);
        Assert.Equal((RegistryHive.CurrentUser, RegistryView.Default), Assert.Single(reads, r => r.Hive == RegistryHive.CurrentUser));
        Assert.Equal("Launcher", Assert.Single(startup.Value).Name);
        var findings = DiagnosticAnalyzer.Analyze(DiagnosticAnalysisTests.Healthy(), DiagnosticAnalysisTests.Signals() with { Startup = startup });
        Assert.Equal("Launcher démarre avec Windows", Assert.Single(findings.Issues).Title);
        Assert.False(findings.Score.IsPartial);
    }

    [Fact]
    public void MachineRunReadsBothViewsAndDeduplicatesNamesWithinEachHive()
    {
        var reads = new List<(RegistryHive Hive, RegistryView View)>();
        var startup = WindowsDiagnosticSignals.ReadStartup(CancellationToken.None, (hive, view, _) =>
        {
            reads.Add((hive, view));
            return hive == RegistryHive.LocalMachine && view == RegistryView.Registry32
                ? new[] { "launcher", "Only32" } : new[] { "Launcher" };
        }, (_, _) => Array.Empty<string>());
        Assert.True(startup.IsAvailable);
        Assert.Equal(new[] { RegistryView.Registry64, RegistryView.Registry32 }, reads.Where(r => r.Hive == RegistryHive.LocalMachine).Select(r => r.View));
        Assert.Equal(3, startup.Value.Count);
        Assert.Equal(2, startup.Value.Count(e => e.Name == "Launcher")); // Même nom HKCU/HKLM : deux entrées distinctes.
        Assert.Equal("Launcher", Assert.Single(startup.Value, e => e.Source.StartsWith("LocalMachine/Registry64", StringComparison.Ordinal)).Name);
        Assert.Equal("Only32", Assert.Single(startup.Value, e => e.Source.StartsWith("LocalMachine/Registry32", StringComparison.Ordinal)).Name);
        var findings = DiagnosticAnalyzer.Analyze(DiagnosticAnalysisTests.Healthy(), DiagnosticAnalysisTests.Signals() with { Startup = startup });
        Assert.Equal(3, findings.Issues.Count);
        Assert.Equal(3, findings.Issues.Select(i => i.Id).Distinct().Count());
    }
}
