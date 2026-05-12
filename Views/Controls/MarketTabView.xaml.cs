using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Excalibur5.Models;
using Excalibur5.ViewModels;

namespace Excalibur5.Views.Controls;

public partial class MarketTabView : UserControl
{
    private Polyline? _chartLine;
    private Line? _crosshairLine;
    private Border? _tooltipBorder;
    private TextBlock? _tooltipText;
    private Ellipse? _tooltipDot;
    private DispatcherTimer? _redrawTimer;
    private int _zoomLevel;
    private int _viewEnd = -1;

    private List<decimal> _visibleValues = new();
    private int _visibleStart;
    private int _visibleEnd;
    private double _drawPadTop;
    private double _drawH;
    private double _drawMin;
    private double _drawRange;

    private bool _isPinned;
    private int _pinnedGlobalIdx = -1;
    private const double PadRight = 6;

    public MarketTabView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged        += OnSizeChanged;

        _redrawTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _redrawTimer.Tick += (_, _) =>
        {
            _redrawTimer.Stop();
            RedrawChart();
        };
    }

    private MarketTabViewModel? Vm => DataContext as MarketTabViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MarketTabViewModel old)
            old.ChartValues.CollectionChanged -= OnChartValuesChanged;

        if (e.NewValue is MarketTabViewModel vm)
        {
            _zoomLevel = 0;
            _viewEnd = -1;
            vm.ChartValues.CollectionChanged += OnChartValuesChanged;
            ScheduleRedraw();
        }
        else
        {
            ChartCanvas.Children.Clear();
            YAxisCanvas.Children.Clear();
            XAxisCanvas.Children.Clear();
            _chartLine = null;
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ScheduleRedraw();

    private void OnChartValuesChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => ScheduleRedraw();

    private void ScheduleRedraw()
    {
        if (_redrawTimer == null) return;
        _redrawTimer.Stop();
        _redrawTimer.Start();
    }

    private const int DefaultVisibleTicks = 150;

    private int GetVisibleCount()
    {
        var vm = Vm;
        if (vm == null) return 0;
        int total = vm.ChartValues.Count;
        int baseVisible = Math.Min(DefaultVisibleTicks, total);
        if (_zoomLevel == 0) return baseVisible;
        if (_zoomLevel > 0)
        {
            int visible = Math.Max(20, baseVisible / (1 + _zoomLevel));
            return Math.Min(visible, total);
        }
        // zoom out (negative): show more ticks
        int expanded = baseVisible * (1 - _zoomLevel);
        return Math.Min(expanded, total);
    }

    private (int start, int end) GetVisibleRange()
    {
        var vm = Vm;
        if (vm == null) return (0, 0);
        int total = vm.ChartValues.Count;
        int visible = GetVisibleCount();
        int end = _viewEnd < 0 ? total : Math.Min(_viewEnd, total);
        int start = Math.Max(0, end - visible);
        return (start, end);
    }

    private void ChartCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var vm = Vm;
        if (vm == null || vm.ChartValues.Count < 2) return;

        int total = vm.ChartValues.Count;

        if (e.Delta > 0)
            _zoomLevel = Math.Min(_zoomLevel + 1, 20);
        else
            _zoomLevel = Math.Max(_zoomLevel - 1, -5);

        if (_zoomLevel == 0)
        {
            _viewEnd = -1;
        }
        else if (_isPinned && _pinnedGlobalIdx >= 0)
        {
            int visible = GetVisibleCount();
            int half = visible / 2;
            int center = _pinnedGlobalIdx;
            _viewEnd = Math.Min(center + half, total);
            if (_viewEnd - visible < 0)
                _viewEnd = Math.Min(visible, total);
        }
        else if (_viewEnd < 0)
        {
            _viewEnd = total;
        }

        RedrawChart();
        e.Handled = true;
    }

    private void ChartCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPinned) return;

        var vm = Vm;
        if (vm == null || vm.ChartValues.Count < 2) return;

        var pos = e.GetPosition(ChartCanvas);
        int count = _visibleEnd - _visibleStart;
        if (count < 2) return;

        double chartWidth = ChartCanvas.ActualWidth;
        double drawWidth = chartWidth - PadRight;
        int localIdx = (int)Math.Round(pos.X / drawWidth * (count - 1));
        localIdx = Math.Clamp(localIdx, 0, count - 1);
        int globalIdx = _visibleStart + localIdx;

        ShowMarkerAt(globalIdx);
    }

    private void ChartCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var vm = Vm;
        if (vm == null || vm.ChartValues.Count < 2) return;

        var pos = e.GetPosition(ChartCanvas);
        int count = _visibleEnd - _visibleStart;
        if (count < 2) return;

        double chartWidth = ChartCanvas.ActualWidth;
        double drawWidth = chartWidth - PadRight;
        int localIdx = (int)Math.Round(pos.X / drawWidth * (count - 1));
        localIdx = Math.Clamp(localIdx, 0, count - 1);
        int globalIdx = _visibleStart + localIdx;

        if (_isPinned && _pinnedGlobalIdx == globalIdx)
        {
            _isPinned = false;
            _pinnedGlobalIdx = -1;
            HideMarker();
        }
        else
        {
            _isPinned = true;
            _pinnedGlobalIdx = globalIdx;
            ShowMarkerAt(globalIdx);
        }

        e.Handled = true;
    }

    private void ShowMarkerAt(int globalIdx)
    {
        var vm = Vm;
        if (vm == null) return;

        int count = _visibleEnd - _visibleStart;
        if (count < 2) return;

        int localIdx = globalIdx - _visibleStart;
        if (localIdx < 0 || localIdx >= count)
        {
            HideMarker();
            return;
        }

        double chartWidth = ChartCanvas.ActualWidth;
        double chartHeight = ChartCanvas.ActualHeight;

        var value = vm.ChartValues[globalIdx];
        var pipSize = GetPipSize();
        var direction = globalIdx < vm.ChartDirections.Count
            ? vm.ChartDirections[globalIdx] : TickDirection.Flat;

        var brush = direction switch
        {
            TickDirection.Up => GreenBrush,
            TickDirection.Down => RedBrush,
            _ => NeutralLightBrush
        };

        double ptX = localIdx * ((chartWidth - PadRight) / (count - 1));
        double ptY = _drawPadTop + _drawH - (((double)value - _drawMin) / _drawRange) * _drawH;

        if (_crosshairLine == null)
        {
            _crosshairLine = new Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(60, 160, 200, 220)),
                StrokeThickness = 0.5,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            };
            ChartCanvas.Children.Add(_crosshairLine);
        }
        _crosshairLine.X1 = ptX;
        _crosshairLine.X2 = ptX;
        _crosshairLine.Y1 = 0;
        _crosshairLine.Y2 = chartHeight;
        _crosshairLine.Visibility = Visibility.Visible;

        if (_tooltipDot == null)
        {
            _tooltipDot = new Ellipse
            {
                Width = 6, Height = 6,
                IsHitTestVisible = false
            };
            ChartCanvas.Children.Add(_tooltipDot);
        }
        _tooltipDot.Fill = brush;
        _tooltipDot.Visibility = Visibility.Visible;
        Canvas.SetLeft(_tooltipDot, ptX - 3);
        Canvas.SetTop(_tooltipDot, ptY - 3);

        if (_tooltipBorder == null)
        {
            _tooltipText = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                IsHitTestVisible = false
            };
            _tooltipBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 20, 28, 38)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                BorderThickness = new Thickness(0.8),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                Child = _tooltipText,
                IsHitTestVisible = false
            };
            ChartCanvas.Children.Add(_tooltipBorder);
        }
        _tooltipText!.Text = value.ToString("F" + pipSize);
        _tooltipText.Foreground = brush;
        _tooltipBorder.Visibility = Visibility.Visible;

        double labelX = Math.Min(ptX + 8, chartWidth - 70);
        double labelY = ptY - 18;
        if (labelY < 0) labelY = ptY + 8;
        Canvas.SetLeft(_tooltipBorder, labelX);
        Canvas.SetTop(_tooltipBorder, labelY);

        Canvas.SetZIndex(_crosshairLine, 10);
        Canvas.SetZIndex(_tooltipDot, 11);
        Canvas.SetZIndex(_tooltipBorder, 12);
    }

    private void HideMarker()
    {
        if (_tooltipBorder != null) _tooltipBorder.Visibility = Visibility.Collapsed;
        if (_tooltipDot != null) _tooltipDot.Visibility = Visibility.Collapsed;
        if (_crosshairLine != null) _crosshairLine.Visibility = Visibility.Collapsed;
    }

    private void ChartCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isPinned)
            HideMarker();
    }

    private int GetPipSize()
    {
        var vm = Vm;
        if (vm == null) return 2;
        var market = MarketInfo.SyntheticMarkets.FirstOrDefault(m => m.Symbol == vm.Symbol);
        return market?.PipSize ?? 2;
    }

    private void RedrawChart()
    {
        if (ChartCanvas == null) return;

        var vm = Vm;
        if (vm == null || vm.ChartValues.Count < 2)
        {
            ChartCanvas.Children.Clear();
            YAxisCanvas.Children.Clear();
            XAxisCanvas.Children.Clear();
            _chartLine = null;
            _crosshairLine = null;
            _tooltipBorder = null;
            _tooltipText = null;
            _tooltipDot = null;
            return;
        }

        var chartWidth  = ChartCanvas.ActualWidth;
        var chartHeight = ChartCanvas.ActualHeight;
        if (chartWidth <= 0 || chartHeight <= 0) return;

        var (start, end) = GetVisibleRange();
        _visibleStart = start;
        _visibleEnd = end;
        int count = end - start;
        if (count < 2) return;

        _visibleValues.Clear();
        for (int i = start; i < end; i++)
            _visibleValues.Add(vm.ChartValues[i]);

        var min   = (double)_visibleValues.Min();
        var max   = (double)_visibleValues.Max();
        var range = max - min;
        if (range == 0) range = 1;

        _drawMin = min;
        _drawRange = range;
        _drawPadTop = chartHeight * 0.06;
        double padBot = chartHeight * 0.06;
        _drawH = chartHeight - _drawPadTop - padBot;

        var points = new PointCollection(count);
        for (int i = 0; i < count; i++)
        {
            double x = i * ((chartWidth - PadRight) / (count - 1));
            double y = _drawPadTop + _drawH - (((double)_visibleValues[i] - min) / range) * _drawH;
            points.Add(new Point(x, y));
        }

        var lineBrush = vm.CurrentDirection switch
        {
            TickDirection.Up   => GreenBrush,
            TickDirection.Down => RedBrush,
            _                 => NeutralBrush
        };

        if (_chartLine == null)
        {
            _chartLine = new Polyline
            {
                StrokeThickness    = 1.2,
                StrokeLineJoin     = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap   = PenLineCap.Round,
            };
            ChartCanvas.Children.Clear();
            ChartCanvas.Children.Add(_chartLine);
            _crosshairLine = null;
            _tooltipBorder = null;
            _tooltipText = null;
            _tooltipDot = null;
        }

        _chartLine.Stroke = lineBrush;
        _chartLine.Points = points;

        DrawYAxis(min, max);
        DrawXAxis(start, end, chartWidth);

        if (_isPinned && _pinnedGlobalIdx >= 0)
            ShowMarkerAt(_pinnedGlobalIdx);
    }

    private static readonly SolidColorBrush AxisBrush = new(Color.FromRgb(0xc0, 0xd8, 0xe8));
    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(0x34, 0xc7, 0x59));
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private static readonly SolidColorBrush NeutralBrush = new(Color.FromRgb(0xa3, 0xb8, 0xcc));
    private static readonly SolidColorBrush NeutralLightBrush = new(Color.FromRgb(0xe0, 0xee, 0xf8));

    static MarketTabView()
    {
        AxisBrush.Freeze();
        GreenBrush.Freeze();
        RedBrush.Freeze();
        NeutralBrush.Freeze();
        NeutralLightBrush.Freeze();
    }

    private void DrawYAxis(double min, double max)
    {
        YAxisCanvas.Children.Clear();
        int steps = 8;
        var pipSize = GetPipSize();

        for (int i = 0; i <= steps; i++)
        {
            double val = min + (max - min) * i / steps;
            double y = _drawPadTop + _drawH - ((val - min) / (max - min)) * _drawH;

            var tb = new TextBlock
            {
                Text = ((decimal)val).ToString("F" + pipSize),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 9,
                Foreground = AxisBrush
            };
            Canvas.SetLeft(tb, 4);
            Canvas.SetTop(tb, y - 6);
            YAxisCanvas.Children.Add(tb);
        }
    }

    private void DrawXAxis(int start, int end, double chartWidth)
    {
        XAxisCanvas.Children.Clear();
        var vm = Vm;
        if (vm == null) return;

        int count = end - start;
        int maxLabels = Math.Max(2, (int)(chartWidth / 70));
        int labelCount = Math.Min(maxLabels, count);
        if (labelCount < 2) return;

        for (int i = 0; i < labelCount; i++)
        {
            int idx = start + (int)((long)i * (count - 1) / (labelCount - 1));
            double x = (double)i / (labelCount - 1) * (chartWidth - PadRight);

            string label;
            if (idx < vm.ChartEpochs.Count && vm.ChartEpochs[idx] > 0)
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(vm.ChartEpochs[idx]);
                label = dt.ToString("HH:mm:ss");
            }
            else
            {
                label = (idx + 1).ToString();
            }

            var tb = new TextBlock
            {
                Text = label,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 9,
                Foreground = AxisBrush
            };
            double labelX = x - 20;
            if (i == 0) labelX = Math.Max(0, labelX);
            Canvas.SetLeft(tb, labelX);
            Canvas.SetTop(tb, 2);
            XAxisCanvas.Children.Add(tb);
        }
    }
}
