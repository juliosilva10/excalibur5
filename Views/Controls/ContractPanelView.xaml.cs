using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Excalibur5.ViewModels;

namespace Excalibur5.Views.Controls;

public partial class ContractPanelView : UserControl
{
    public ContractPanelView()
    {
        InitializeComponent();
    }

    private ContractPanelViewModel? Vm => DataContext as ContractPanelViewModel;

    private void DurationUnit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string unit)
            Vm?.SetDurationUnitCommand.Execute(unit);
    }

    private void EndTimeMode_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null)
            Vm.UseDuration = false;
    }

    private void CallButton_Click(object sender, RoutedEventArgs e)
    {
        Vm?.SelectCallCommand.Execute(null);
    }

    private void PutButton_Click(object sender, RoutedEventArgs e)
    {
        Vm?.SelectPutCommand.Execute(null);
    }

    private void DurationTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, @"^[0-9]$");
    }

    private void StakeTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var textBox = (TextBox)sender;
        var currentText = textBox.Text;
        var caretIndex = textBox.CaretIndex;
        var selectionLength = textBox.SelectionLength;

        var newText = currentText.Substring(0, caretIndex)
                    + e.Text
                    + currentText.Substring(caretIndex + selectionLength);

        e.Handled = !Regex.IsMatch(newText, @"^\d*[.,]?\d{0,2}$");
    }

    private void TimeTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var textBox = (TextBox)sender;
        var currentText = textBox.Text;
        var caretIndex = textBox.CaretIndex;
        var selectionLength = textBox.SelectionLength;

        var newText = currentText.Substring(0, caretIndex)
                    + e.Text
                    + currentText.Substring(caretIndex + selectionLength);

        // Allow digits and colon in HH:mm format
        e.Handled = !Regex.IsMatch(newText, @"^[0-2]?[0-9]?:?[0-5]?[0-9]?$");
    }

    private void DpEndDate_CalendarOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not DatePicker dp) return;

        // Access the internal Calendar via reflection (DatePicker._calendar field)
        var calendarField = typeof(DatePicker).GetField("_calendar",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var calendar = calendarField?.GetValue(dp) as Calendar;

        if (calendar == null)
        {
            // Fallback: try via Popup
            var popup = dp.Template.FindName("PART_Popup", dp) as Popup;
            if (popup?.Child is FrameworkElement popupChild)
                calendar = FindVisualChild<Calendar>(popupChild);
        }

        if (calendar == null) return;

        calendar.DisplayMode = CalendarMode.Month;
        calendar.DisplayDateStart = dp.DisplayDateStart;
        calendar.DisplayDateEnd = dp.DisplayDateEnd;

        // Mark past days as blacked out (styled with reduced opacity, no gray square)
        var today = DateTime.UtcNow.Date;
        calendar.BlackoutDates.Clear();
        var firstOfMonth = new DateTime(today.Year, today.Month, 1);
        if (firstOfMonth < today)
        {
            calendar.BlackoutDates.Add(new CalendarDateRange(firstOfMonth, today.AddDays(-1)));
        }

        // Use Render priority to ensure visual tree is fully built
        calendar.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            AddDayHeaders(calendar);
        });
    }

    private void AddDayHeaders(Calendar calendar)
    {
        var calendarItem = FindVisualChild<System.Windows.Controls.Primitives.CalendarItem>(calendar);
        if (calendarItem == null) return;

        // Find PART_MonthView by walking the visual tree
        var monthView = FindVisualChildByName<Grid>(calendarItem, "PART_MonthView");
        if (monthView == null) return;

        var cyanBrush = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00f0ff"));
        cyanBrush.Freeze();

        string[] dayNames = { "D", "S", "T", "Q", "Q", "S", "S" };

        // Remove any previously added headers (tagged)
        var toRemove = new System.Collections.Generic.List<System.Windows.UIElement>();
        foreach (System.Windows.UIElement child in monthView.Children)
        {
            if (child is TextBlock tb && tb.Tag is string tag && tag == "DayHeader")
                toRemove.Add(child);
        }
        foreach (var item in toRemove)
            monthView.Children.Remove(item);

        // Set first row to fixed height
        if (monthView.RowDefinitions.Count > 0)
            monthView.RowDefinitions[0].Height = new GridLength(24);

        // Add day headers
        for (int i = 0; i < 7; i++)
        {
            var tb = new TextBlock
            {
                Text = dayNames[i],
                Foreground = cyanBrush,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 8, 0, 4),
                Tag = "DayHeader"
            };
            Grid.SetRow(tb, 0);
            Grid.SetColumn(tb, i);
            monthView.Children.Add(tb);
        }

        // Add separator line above day headers
        var separator = new Border
        {
            Height = 1,
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1d4957")),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 0, 0),
            Tag = "DayHeader"
        };
        Grid.SetRow(separator, 0);
        Grid.SetColumn(separator, 0);
        Grid.SetColumnSpan(separator, 7);
        monthView.Children.Add(separator);
    }

    private static T? FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T fe && fe.Name == name)
                return fe;
            var found = FindVisualChildByName<T>(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
                return result;
            var found = FindVisualChild<T>(child);
            if (found != null)
                return found;
        }
        return null;
    }

    private void RecoverDecimalInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox tb) { e.Handled = true; return; }
        var future = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, e.Text);
        e.Handled = !Regex.IsMatch(future, @"^\d*\.?\d{0,2}$");
    }

    private void RecoverDecimalInput_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            var text = (string)e.DataObject.GetData(typeof(string))!;
            if (!Regex.IsMatch(text, @"^\d*\.?\d{0,2}$"))
                e.CancelCommand();
        }
        else
        {
            e.CancelCommand();
        }
    }

    private void RecoverIntegerInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox tb) { e.Handled = true; return; }
        var future = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, e.Text);
        e.Handled = !Regex.IsMatch(future, @"^\d+$");
    }

    private void RecoverIntegerInput_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            var text = (string)e.DataObject.GetData(typeof(string))!;
            if (!Regex.IsMatch(text, @"^\d+$"))
                e.CancelCommand();
        }
        else
        {
            e.CancelCommand();
        }
    }

    private void BotDurationUnit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string unit)
        {
            var vm = FindStrategyVm();
            if (vm != null) vm.DurationUnit = unit;
        }
    }

    private void BotEndTimeMode_Click(object sender, RoutedEventArgs e)
    {
        var vm = FindStrategyVm();
        if (vm != null) vm.UseDuration = false;
    }

    private void BotDurationMode_Click(object sender, RoutedEventArgs e)
    {
        var vm = FindStrategyVm();
        if (vm != null) vm.UseDuration = true;
    }

    private void BotDurationTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, @"^[0-9]$");
    }

    private void BotDirection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            var vm = FindStrategyVm();
            if (vm != null) vm.DirectionMode = tag;
        }
    }

    private StrategyViewModel? FindStrategyVm()
    {
        var mainVm = Window.GetWindow(this)?.DataContext as MainViewModel;
        return mainVm?.Strategy;
    }
}
