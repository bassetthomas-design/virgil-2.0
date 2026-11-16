using Virgil.App.ViewModels;

namespace Virgil.App.Speech;

/// <summary>Une instance par propriétaire ; un compteur indépendant par liste.</summary>
public sealed class VirgilLinePicker(TimeProvider clock)
{
    private readonly Dictionary<string, long> _counts = new(StringComparer.Ordinal);
    public string Pick(string list)
    {
        var lines = VirgilLines.Lists[list];
        var count = _counts.GetValueOrDefault(list);
        _counts[list] = count + 1;
        return lines[(int)((clock.GetLocalNow().DayOfYear + count) % lines.Length)];
    }
    public string PickRunning(bool humourAllowed) => Pick(humourAllowed ? "RunningHumour" : "RunningSober");
    public string PickFinal(OperationFinalState state, bool humourAllowed) => Pick(state switch
    {
        OperationFinalState.Succeeded => humourAllowed ? "SucceededHumour" : "SucceededSober",
        OperationFinalState.NeedsReview => "NeedsReview",
        OperationFinalState.Failed => "Failed",
        OperationFinalState.Cancelled => humourAllowed ? "CancelledHumour" : "CancelledSober",
        _ => humourAllowed ? "NeutralHumour" : "NeutralSober"
    });
}
