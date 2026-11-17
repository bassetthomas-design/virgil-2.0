using Virgil.Contracts;
using Virgil.Contracts.Elevated;
using Virgil.ElevatedHelper;
using Xunit;

namespace Virgil.Tests;

public sealed class LotReparationProgressionTests
{
    public static IEnumerable<object[]> Steps => new[]
    {
        new object[] { InterventionId.VerifyHealth, new[] { "Étape 1 sur 4 : Fichiers système (SFC, vérification)", "Étape 2 sur 4 : Image Windows (DISM, contrôle rapide)", "Étape 3 sur 4 : Disque système (CHKDSK, analyse en ligne)", "Étape 4 sur 4 : Services Windows" } },
        new object[] { InterventionId.VerifyHealthDeep, new[] { "Étape 1 sur 5 : Fichiers système (SFC, vérification)", "Étape 2 sur 5 : Image Windows (DISM, contrôle rapide)", "Étape 3 sur 5 : Image Windows (DISM, analyse)", "Étape 4 sur 5 : Disque système (CHKDSK, analyse en ligne)", "Étape 5 sur 5 : Services Windows" } },
        new object[] { InterventionId.SfcScan, new[] { "Étape 1 sur 1 : Fichiers système (SFC, réparation)" } },
        new object[] { InterventionId.DismRestoreHealth, new[] { "Étape 1 sur 1 : Image Windows (DISM, réparation)" } },
        new object[] { InterventionId.RenewIp, new[] { "Étape 1 sur 2 : Adresse IP (libération)", "Étape 2 sur 2 : Adresse IP (renouvellement)" } },
        new object[] { InterventionId.ResetPrintQueue, new[] { "Étape 1 sur 1 : File d'impression" } },
    };

    [Theory]
    [MemberData(nameof(Steps))]
    public async Task Dispatcher_names_each_step_in_french(InterventionId action, string[] expected)
    {
        var values = new List<InterventionProgress>();
        await Dispatcher().ExecuteValidatedAsync(new() { ActionId = action }, new Collector(values));
        Assert.Equal(expected, values.Select(value => value.Step).Distinct());
        foreach (var step in expected)
            Assert.Equal(new int?[] { null, 50 }, values.Where(value => value.Step == step).Select(value => value.Percent));
    }

    [Fact]
    public async Task Summary_output_is_unchanged()
    {
        var result = await Dispatcher().ExecuteValidatedAsync(new() { ActionId = InterventionId.VerifyHealth });
        const string expected = """
        VIRGIL|SFC|UNKNOWN|Résultat non interprétable ; consultez la sortie de l’outil
        Étape : sfc.exe /verifyonly
        Code sortie : 0.
        VIRGIL|DISM|UNKNOWN|Résultat non interprétable ; consultez la sortie de l’outil
        Étape : dism.exe /Online /Cleanup-Image /CheckHealth /English
        Code sortie : 0.
        VIRGIL|CHKDSK|OK|Aucune anomalie détectée
        Étape : chkdsk.exe C: /scan
        Code sortie : 0.

        Étape : virgil-services
        Code sortie : 0.
        VIRGIL|wuauserv|INFO|x
        Vérification incomplète : certains contrôles sont indisponibles. Consultez les détails.
        """;
        Assert.Equal(expected.Replace("\r\n", "\n"), result.SummaryOutput);
    }

    private static ElevatedActionDispatcher Dispatcher() => new(
        new ElevatedRequestValidator(() => new DateTimeOffset(2026, 6, 17, 10, 0, 0, TimeSpan.Zero), () => Path.GetTempPath()),
        new ElevatedActionAllowlist(), new Runner(), new SecurityContext(), () => @"C:\Windows\System32");

    private sealed class SecurityContext : IElevatedSecurityContext
    {
        public bool IsAdministrator => true;
    }

    private sealed class Runner : IElevatedCommandRunner
    {
        public Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command) => RunAsync(command, null, CancellationToken.None);
        public Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command, IProgress<int?>? progress, CancellationToken cancellationToken)
        {
            progress?.Report(50);
            return Task.FromResult(new ElevatedCommandResult(0,
                command.FileName == "virgil-services" ? "VIRGIL|wuauserv|INFO|x" : string.Empty, string.Empty));
        }
    }

    private sealed class Collector(List<InterventionProgress> values) : IProgress<InterventionProgress>
    {
        public void Report(InterventionProgress value) => values.Add(value);
    }
}
