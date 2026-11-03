using System.Windows;

namespace Virgil.App;

public partial class AlphaWarningWindow : Window
{
    public const string WarningText = "Attention, ceci est une version alpha. Vous êtes seul responsable de ce que vous faites. En cas de doute, ne lancez rien. Merci.";

    public AlphaWarningWindow() => InitializeComponent();

    // No remembered acknowledgement: each startup invokes the modal before showing the main window.
    // VIRGIL_APP_EXE is inherited by the child process launched by the existing CI UI capture harness.
    public static void RunStartup(bool isCaptureTest, Action showWarning, Action showMainWindow)
    {
        if (!isCaptureTest) showWarning();
        showMainWindow();
    }

    private void Accept_Click(object sender, RoutedEventArgs e) => Close();
}
