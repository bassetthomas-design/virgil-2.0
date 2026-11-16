using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

public sealed class LotHLauncherSummaryTests
{
    [Fact]
    public void Steam_without_games_is_current_only_after_its_own_check_and_keeps_other_priorities()
    {
        var steam = new UpdateSequenceStepResult(new("Steam", "Steam", UpdateComponent.Launcher), true, false,
            "Steam ouvert en silencieux ; jeux contrôlés en lecture seule.") { VerifiedStatus = "à jour selon Steam" };
        var result = LauncherCardSummary.Summarize([steam]);
        Assert.Equal("à jour selon Steam", result.Status); Assert.Equal("", result.Detail);
        var other = new UpdateSequenceStepResult(new("Battle.net", "Battle.net", UpdateComponent.Launcher), false, false,
            LauncherCardSummary.OtherGames) { VerifiedStatus = LauncherCardSummary.Unsupported, WasSkipped = true };
        result = LauncherCardSummary.Summarize([steam, other]);
        Assert.Equal("non pris en charge automatiquement", result.Status);
        Assert.Equal("1 lanceur non pris en charge automatiquement", result.Detail);
        result = LauncherCardSummary.Summarize([new(new("Steam", "Steam", UpdateComponent.Launcher), true, false,
            "Steam : fermé ; relancez-le quand vous voulez.") { VerifiedStatus = "à vérifier" }]);
        Assert.Equal("à vérifier", result.Status);
        Assert.Equal("à vérifier", LauncherCardSummary.Summarize([]).Status);
    }
}
