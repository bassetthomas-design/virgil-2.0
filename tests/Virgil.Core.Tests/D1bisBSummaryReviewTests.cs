using Virgil.Core.Updates;
using Xunit;

namespace Virgil.Tests;

public sealed class D1bisBSummaryReviewTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Launcher_source_omits_zero_counts_and_agrees_with_the_number_of_games(int count)
    {
        string[] statuses = ["mis à jour", "à jour selon Steam", "reste à installer", "en erreur", "non lancée"];
        var outcomes = statuses.SelectMany((status, index) => Enumerable.Range(0, count).Select(number =>
            new UpdateSequenceStepResult(new($"SteamGame:{index}{number}", "Jeu", UpdateComponent.Launcher),
                status is "mis à jour" or "à jour selon Steam", false, status)
            { VerifiedStatus = status, WasSkipped = status is "reste à installer" or "non lancée" })).ToList();
        for (var index = 0; index < count; index++)
            outcomes.Add(new(new("Autre lanceur " + index, "Autre lanceur", UpdateComponent.Launcher), false, false, LauncherCardSummary.OtherGames)
            { VerifiedStatus = LauncherCardSummary.Unsupported, WasSkipped = true });
        var source = LauncherCardSummary.Summarize(outcomes);
        Assert.DoesNotContain("0 ", source.Detail);
        if (count == 0)
        {
            Assert.Equal(string.Empty, source.Detail);
            return;
        }
        var expected = count == 1
            ? "Steam : 1 mis à jour, 1 à jour selon Steam, 1 reste à installer, 1 en erreur, 1 non lancé ; 1 lanceur non pris en charge automatiquement"
            : "Steam : 2 mis à jour, 2 à jour selon Steam, 2 restent à installer, 2 en erreur, 2 non lancés ; 2 lanceurs non pris en charge automatiquement";
        Assert.StartsWith(expected, source.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("non lancée", source.Detail);
        Assert.False(source.IsCurrent);
        Assert.True(source.HasRemaining);
    }

    [Theory]
    [InlineData("mis à jour")]
    [InlineData("à jour selon Steam")]
    [InlineData("reste à installer")]
    [InlineData("en erreur")]
    [InlineData("non lancée")]
    public void Source_only_mentions_the_present_game_category(string status)
    {
        var game = new UpdateSequenceStepResult(new("SteamGame:10", "Jeu", UpdateComponent.Launcher),
            status is "mis à jour" or "à jour selon Steam", false, status)
        { VerifiedStatus = status, WasSkipped = status is "reste à installer" or "non lancée" };
        var source = LauncherCardSummary.Summarize([game]);
        Assert.StartsWith("Steam : 1 " + (status == "non lancée" ? "non lancé" : status), source.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("0 ", source.Detail);
        Assert.DoesNotContain(",", source.Detail);
    }
}
