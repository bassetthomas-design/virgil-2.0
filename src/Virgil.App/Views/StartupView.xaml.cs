using System.Windows;
using System.Windows.Controls;
using Virgil.App.ViewModels;

namespace Virgil.App.Views;

public partial class StartupView : UserControl
{
    private readonly StartupViewModel _viewModel;
    private bool _loaded;
    public StartupView(StartupViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }
    private async void View_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        await _viewModel.RefreshAsync();
    }
}
