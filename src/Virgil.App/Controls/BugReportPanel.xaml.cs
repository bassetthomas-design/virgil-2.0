using System.Windows.Controls;
using Virgil.App.ViewModels;

namespace Virgil.App.Controls;

public partial class BugReportPanel : UserControl
{
    public BugReportPanel(BugReportViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
