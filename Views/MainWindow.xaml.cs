using System.Windows;
using System.Windows.Controls;
using Excalibur5.Config;
using Excalibur5.ViewModels;

namespace Excalibur5.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded; // fire once only
        if (DataContext is MainViewModel vm)
        {
            var saved = TokenStore.Load();
            if (!string.IsNullOrEmpty(saved))
            {
                TokenBox.Password = saved;
                vm.SetToken(saved);
            }
        }
    }

    private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.SetToken(((PasswordBox)sender).Password);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Dispose();
        base.OnClosed(e);
    }
}