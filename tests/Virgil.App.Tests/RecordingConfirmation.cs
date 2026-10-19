using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Tests;

internal sealed class RecordingConfirmation : IConfirmationService
{
    public ConfirmationService Authority { get; } = new(TimeProvider.System);
    public int Calls { get; private set; }
    public bool Refuse { get; set; }
    public ConfirmedPlan Issue(ActionPreview preview, ConfirmationConsent consent)
    {
        Calls++;
        if (Refuse) throw new ConfirmationRefusedException();
        return Authority.Issue(preview, consent);
    }
}
