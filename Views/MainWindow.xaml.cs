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
        Loaded -= OnLoaded;
        if (DataContext is MainViewModel vm)
        {
            var saved = TokenStore.Load();
            if (!string.IsNullOrEmpty(saved))
            {
                TokenBox.Password = saved;
                vm.SetToken(saved);
            }

            vm.Log.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.Log.LogText))
                    LogTextBox.ScrollToEnd();
            };
        }
    }

    private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.SetToken(((PasswordBox)sender).Password);
    }

    private async void MarketTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MarketTabViewModel tab &&
            DataContext is MainViewModel vm)
        {
            await vm.Markets.SelectTabAsync(tab);
        }
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Log.CopyAllCommand.Execute(null);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Dispose();
        base.OnClosed(e);
    }
}
