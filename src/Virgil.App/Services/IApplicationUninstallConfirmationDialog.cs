namespace Virgil.App.Services;

public interface IApplicationUninstallConfirmationDialog
{
    bool Confirm(string title, string message);
}
