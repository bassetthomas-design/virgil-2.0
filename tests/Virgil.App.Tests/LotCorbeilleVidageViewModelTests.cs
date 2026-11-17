using Virgil.Contracts;
using Virgil.Core.Cleanup;
using Xunit;

namespace Virgil.Tests;

public sealed class LotCorbeilleVidageViewModelTests
{
    [Fact]
    public async Task Verified_empty_is_announced_as_verified()
    {
        var f = await Run(CleanupStepStatus.Completed, null);
        Assert.Contains("Nettoyage terminé.\n\nCorbeille vidée et vérifiée : 1 Ko libérés.\nRapport disponible.", f.Messages);
        Assert.Equal("Nettoyage terminé.", f.Vm.StatusText.Replace("\r", "").Split('\n')[0]);
        Assert.Contains("  Corbeille vidée et vérifiée : 3 élément(s) - 1 Ko", f.Vm.ReportText.Replace("\r", "").Split('\n'));
    }

    [Theory]
    [InlineData("Vidage incomplet de la corbeille : 1 élément(s) restant(s) (code 0x00000000).")]
    [InlineData("Windows ne permet pas de confirmer le vidage de la corbeille (code 0x00000000). Ouvrez la Corbeille pour vérifier.")]
    public async Task Failed_or_unverified_empty_is_never_announced_as_done(string message)
    {
        var f = await Run(CleanupStepStatus.Failed, message);
        Assert.Equal("Nettoyage terminé avec erreurs.", f.Vm.StatusText.Replace("\r", "").Split('\n')[0]);
        Assert.Contains("Nettoyage terminé avec erreurs.\nCorbeille : " + message + "\nRapport disponible.", f.Messages);
        Assert.DoesNotContain(f.Messages, text => text.StartsWith("Nettoyage terminé.\n", StringComparison.Ordinal));
        var lines = f.Vm.ReportText.Replace("\r", "").Split('\n');
        Assert.Contains("Corbeille : échec", lines);
        Assert.Contains("  Erreur : " + message, lines);
    }

    private static async Task<CleanupViewModelTests.Fixture> Run(CleanupStepStatus status, string? message)
    {
        var f = new CleanupViewModelTests.Fixture();
        f.Previews.Items = [Recycle(3, 1024, [])];
        f.Actions.Handler = (preview, _) => Task.FromResult(new CleanupStepResult(preview.Definition, status,
            status == CleanupStepStatus.Completed ? 3 : 0, status == CleanupStepStatus.Completed ? 1024 : 0,
            0, status == CleanupStepStatus.Failed ? 1 : 0, TimeSpan.Zero, message is null ? [] : [message]));
        await f.Analyze().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(f.Vm.Groups.SelectMany(g => g.Rows)).IsSelected = true;
        var pending = f.Run();
        try
        {
            f.Vm.CriticalConfirmationText = f.Vm.ExpectedCriticalConfirmation;
            f.Vm.ExecuteDecisionCommand.Execute(null);
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            if (f.Vm.IsValidationOpen) f.Vm.CancelDecisionCommand.Execute(null);
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
        return f;
    }

    private static CleanupZonePreview Recycle(int items, long bytes, string[] errors)
    {
        var def = Assert.Single(CleanupZoneCatalog.CreateDefault(), z => z.Id == CleanupZoneId.RecycleBin);
        return new PersonalFolderPolicy(new EmptyCleanupSource()).Apply(
            new CleanupZonePreview(def, DateTimeOffset.UtcNow, items, items, bytes, 0, [], errors));
    }
}
