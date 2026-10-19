using System.Linq;
using System.Windows;

namespace Virgil.App.Services;

public sealed class ApplicationUninstallConfirmationDialogService : IApplicationUninstallConfirmationDialog
{
    public bool Confirm(string title, string message)
    {
        var owner = ResolveDialogOwner();
        var response = owner is not null
            ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No, MessageBoxOptions.None)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No, MessageBoxOptions.None);
        return response == MessageBoxResult.Yes;
    }

    private static Window? ResolveDialogOwner()
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            ?? Application.Current?.MainWindow;
        if (owner is null) return null;
        if (owner.WindowState == WindowState.Minimized) owner.WindowState = WindowState.Normal;
        owner.Activate();
        owner.Focus();
        return owner;
    }
}
