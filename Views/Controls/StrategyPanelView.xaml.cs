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

    private void Timeframe_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag && int.TryParse(tag, out var tf))
        {
            if (Vm != null)
                Vm.Timeframe = tf;
        }
    }

    private void Direction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            if (Vm != null)
                Vm.DirectionMode = tag;
        }
    }
}
