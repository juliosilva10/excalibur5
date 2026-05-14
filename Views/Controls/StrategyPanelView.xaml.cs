using System.Windows;
using System.Windows.Controls;
using Excalibur5.ViewModels;

namespace Excalibur5.Views.Controls;

public partial class StrategyPanelView : UserControl
{
    public StrategyPanelView()
    {
        InitializeComponent();
    }

    private StrategyViewModel? Vm => DataContext as StrategyViewModel;

    private void Direction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            if (Vm != null)
                Vm.DirectionMode = tag;
        }
    }
}
