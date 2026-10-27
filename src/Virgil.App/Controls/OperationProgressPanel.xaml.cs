using System.Windows.Controls;
namespace Virgil.App.Controls;
public partial class OperationProgressPanel : UserControl
{
    public static readonly System.Windows.DependencyProperty CancelAutomationIdProperty =
        System.Windows.DependencyProperty.Register(nameof(CancelAutomationId), typeof(string), typeof(OperationProgressPanel), new System.Windows.PropertyMetadata(string.Empty));
    public string CancelAutomationId
    {
        get => (string)GetValue(CancelAutomationIdProperty);
        set => SetValue(CancelAutomationIdProperty, value);
    }
    public OperationProgressPanel() => InitializeComponent();
}
