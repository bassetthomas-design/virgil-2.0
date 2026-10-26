namespace Virgil.App.Services;

// Compatibility adapter for the former synchronous API. Consent now comes exclusively
// from the ApplicationsViewModel inline preview. This adapter always fails closed.
public sealed class ApplicationUninstallConfirmationDialogService : IApplicationUninstallConfirmationDialog
{
    public bool Confirm(string title, string message) => false;
}
