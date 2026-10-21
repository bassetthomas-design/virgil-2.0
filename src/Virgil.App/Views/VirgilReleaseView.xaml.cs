using System.Windows.Controls;
using Virgil.App.ViewModels;

namespace Virgil.App.Views;
public partial class VirgilReleaseView : UserControl
{
    public VirgilReleaseView(VirgilReleaseViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
