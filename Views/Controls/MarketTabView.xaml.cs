using System.ComponentModel;
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

    // Candle zoom state
    private int _candleZoomLevel;
    private int _candleViewEnd = -1;
    private int _visibleCandleStart;
    private int _visibleCandleEnd;

    public MarketTabView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged        += OnSizeChanged;

        _redrawTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
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
        {
            old.ChartValues.CollectionChanged -= OnChartValuesChanged;
            old.PropertyChanged -= OnVmPropertyChanged;
            old.CandleUpdated -= OnCandleUpdated;
        }

        if (e.NewValue is MarketTabViewModel vm)
        {
            _zoomLevel = 0;
            _viewEnd = -1;
            _candleZoomLevel = 0;
            _candleViewEnd = -1;
            vm.ChartValues.CollectionChanged += OnChartValuesChanged;
            vm.PropertyChanged += OnVmPropertyChanged;
            vm.CandleUpdated += OnCandleUpdated;
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

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MarketTabViewModel.ChartType) or nameof(MarketTabViewModel.CandleValues))
            ScheduleRedraw();
    }

    private void OnCandleUpdated(object? sender, EventArgs e)
    {
        if (Vm?.ChartType != ChartType.Candles) return;

        if (_candleViewEnd >= 0)
        {
            var total = Vm.CandleValues.Count;
            var (_, currentEnd) = GetVisibleCandleRange(Vm);
            if (currentEnd >= total - 1)
                _candleViewEnd = total;
        }

        ScheduleRedraw();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ScheduleRedraw();

    private void OnChartValuesChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (Vm?.ChartType == ChartType.Candles) return;
        ScheduleRedraw();
    }

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

    private const int DefaultVisibleCandles = 100;

    private (int start, int end) GetVisibleCandleRange(MarketTabViewModel vm)
    {
        int total = vm.CandleValues.Count;
        int baseVisible = Math.Min(DefaultVisibleCandles, total);
        int visible;
        if (_candleZoomLevel == 0)
            visible = baseVisible;
        else if (_candleZoomLevel > 0)
            visible = Math.Max(10, baseVisible / (1 + _candleZoomLevel));
        else
            visible = Math.Min(baseVisible * (1 - _candleZoomLevel), total);

        int end = _candleViewEnd < 0 ? total : Math.Min(_candleViewEnd, total);
        int start = Math.Max(0, end - visible);
        return (start, end);
    }

    private void ChartCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var vm = Vm;
        if (vm == null) return;

        if (vm.ChartType == ChartType.Candles)
        {
            int total = vm.CandleValues.Count;
            if (total < 2) return;

            if (e.Delta > 0)
                _candleZoomLevel = Math.Min(_candleZoomLevel + 1, 20);
            else
                _candleZoomLevel = Math.Max(_candleZoomLevel - 1, -5);

            if (_candleZoomLevel == 0)
                _candleViewEnd = -1;
            else if (_candleViewEnd < 0)
                _candleViewEnd = total;

            RedrawChart();
            e.Handled = true;
            return;
        }

        if (vm.ChartValues.Count < 2) return;
        int tickTotal = vm.ChartValues.Count;

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
            _viewEnd = Math.Min(center + half, tickTotal);
            if (_viewEnd - visible < 0)
                _viewEnd = Math.Min(visible, tickTotal);
        }
        else if (_viewEnd < 0)
        {
            _viewEnd = tickTotal;
        }

        RedrawChart();
        e.Handled = true;
    }

    private void ChartCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPinned) return;

        var vm = Vm;
        if (vm == null) return;

        var pos = e.GetPosition(ChartCanvas);
        double chartWidth = ChartCanvas.ActualWidth;
        double drawWidth = chartWidth - PadRight;

        if (vm.ChartType == ChartType.Candles && vm.CandleValues.Count > 0)
        {
            int numVisible = _visibleCandleEnd - _visibleCandleStart;
            if (numVisible < 1) return;
            double gap = drawWidth / numVisible;
            int candleIdx = (int)(pos.X / gap);
            candleIdx = Math.Clamp(candleIdx, 0, numVisible - 1);
            ShowCandleMarkerAt(_visibleCandleStart + candleIdx, pos, numVisible);
            return;
        }

        if (vm.ChartValues.Count < 2) return;
        int count = _visibleEnd - _visibleStart;
        if (count < 2) return;

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

    private void ShowCandleMarkerAt(int candleIdx, Point pos, int numVisible)
    {
        var vm = Vm;
        if (vm == null || vm.CandleValues.Count == 0) return;

        var c = vm.CandleValues[candleIdx];
        var pipSize = GetPipSize();
        double chartWidth = ChartCanvas.ActualWidth;
        double chartHeight = ChartCanvas.ActualHeight;
        double drawWidth = chartWidth - PadRight;
        double gap = drawWidth / numVisible;
        int localIdx = candleIdx - _visibleCandleStart;
        double ptX = localIdx * gap + gap / 2;

        bool bullish = c.Close >= c.Open;
        var brush = bullish ? GreenBrush : NeutralLightBrush;

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

        // Hide dot for candles
        if (_tooltipDot != null) _tooltipDot.Visibility = Visibility.Collapsed;

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
                Padding = new Thickness(6, 4, 6, 4),
                Child = _tooltipText,
                IsHitTestVisible = false
            };
            ChartCanvas.Children.Add(_tooltipBorder);
        }

        var dt = c.Epoch > 0 ? DateTimeOffset.FromUnixTimeSeconds(c.Epoch).ToString("dd/MM HH:mm") : "";
        _tooltipText!.Text = $"{dt}\n" +
                             $"OPEN  {c.Open.ToString("F" + pipSize)}\n" +
                             $"CLOSE  {c.Close.ToString("F" + pipSize)}\n" +
                             $"HIGH  {c.High.ToString("F" + pipSize)}\n" +
                             $"LOW  {c.Low.ToString("F" + pipSize)}\n" +
                             $"{vm.FullName.ToUpper()}  {c.Close.ToString("F" + pipSize)}";
        _tooltipText.Foreground = NeutralLightBrush;
        _tooltipBorder.Visibility = Visibility.Visible;

        double tooltipWidth = 180;
        double labelX = ptX + 8;
        if (labelX + tooltipWidth > chartWidth)
            labelX = ptX - tooltipWidth - 8;
        labelX = Math.Max(0, labelX);
        double labelY = 8;
        Canvas.SetLeft(_tooltipBorder, labelX);
        Canvas.SetTop(_tooltipBorder, labelY);

        Canvas.SetZIndex(_crosshairLine, 10);
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
        if (vm == null)
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

        if (vm.ChartType == ChartType.Candles)
        {
            if (vm.CandleValues.Count < 2) return;
            var (cStart, cEnd) = GetVisibleCandleRange(vm);
            _visibleCandleStart = cStart;
            _visibleCandleEnd = cEnd;
            DrawCandles(cStart, cEnd, chartWidth, chartHeight);
            DrawXAxisCandles(chartWidth, cStart, cEnd);
            return;
        }

        if (vm.ChartValues.Count < 2)
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
            if (i == 0) labelX = Math.Max(8, labelX);
            Canvas.SetLeft(tb, labelX);
            Canvas.SetTop(tb, 2);
            XAxisCanvas.Children.Add(tb);
        }
    }

    private void ChartTypeButton_Down(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (ChartTypePopup.IsOpen)
        {
            CloseChartPopup();
        }
        else
        {
            ChartTypePopup.IsOpen = true;
            // Capture mouse on next frame to detect clicks outside the popup
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                Mouse.Capture(this, CaptureMode.SubTree);
            });
        }
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (ChartTypePopup.IsOpen)
        {
            // Check if click is inside the popup or the button
            var popupContent = ChartTypePopup.Child as Border;
            if (popupContent != null)
            {
                var posInPopup = e.GetPosition(popupContent);
                if (posInPopup.X >= 0 && posInPopup.Y >= 0 &&
                    posInPopup.X <= popupContent.ActualWidth &&
                    posInPopup.Y <= popupContent.ActualHeight)
                {
                    base.OnPreviewMouseDown(e);
                    return;
                }
            }

            var posInButton = e.GetPosition(ChartTypeButton);
            if (posInButton.X >= 0 && posInButton.Y >= 0 &&
                posInButton.X <= ChartTypeButton.ActualWidth &&
                posInButton.Y <= ChartTypeButton.ActualHeight)
            {
                base.OnPreviewMouseDown(e);
                return;
            }

            // Click is outside — close popup
            CloseChartPopup();
            e.Handled = true;
        }
        base.OnPreviewMouseDown(e);
    }

    private void CloseChartPopup()
    {
        ChartTypePopup.IsOpen = false;
        if (Mouse.Captured == this)
            Mouse.Capture(null);
    }

    private void ChartTypeLine_Click(object sender, MouseButtonEventArgs e)
    {
        if (Vm != null) Vm.ChartType = ChartType.Line;
        UpdateButtonIcon(ChartType.Line);
        CloseChartPopup();
    }

    private void ChartTypeCandles_Click(object sender, MouseButtonEventArgs e)
    {
        if (Vm != null) Vm.ChartType = ChartType.Candles;
        UpdateButtonIcon(ChartType.Candles);
        CloseChartPopup();
    }

    private void UpdateButtonIcon(ChartType type)
    {
        ChartTypeButton.Child = null;

        if (type == ChartType.Candles)
        {
            var canvas = new Canvas { Width = 14, Height = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            canvas.Children.Add(new Line { X1 = 3, Y1 = 0, X2 = 3, Y2 = 12, Stroke = GreenBrush, StrokeThickness = 1 });
            canvas.Children.Add(new Rectangle { Width = 4, Height = 5, Fill = GreenBrush });
            Canvas.SetLeft(canvas.Children[1], 1);
            Canvas.SetTop(canvas.Children[1], 3);
            canvas.Children.Add(new Line { X1 = 11, Y1 = 0, X2 = 11, Y2 = 12, Stroke = RedBrush, StrokeThickness = 1 });
            canvas.Children.Add(new Rectangle { Width = 4, Height = 6, Fill = RedBrush });
            Canvas.SetLeft(canvas.Children[3], 9);
            Canvas.SetTop(canvas.Children[3], 2);
            ChartTypeButton.Child = canvas;
        }
        else
        {
            var canvas = new Canvas { Width = 14, Height = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            canvas.Children.Add(new Line { X1 = 0, Y1 = 8, X2 = 4, Y2 = 3, Stroke = GreenBrush, StrokeThickness = 1.2 });
            canvas.Children.Add(new Line { X1 = 4, Y1 = 3, X2 = 9, Y2 = 6, Stroke = GreenBrush, StrokeThickness = 1.2 });
            canvas.Children.Add(new Line { X1 = 9, Y1 = 6, X2 = 14, Y2 = 1, Stroke = GreenBrush, StrokeThickness = 1.2 });
            ChartTypeButton.Child = canvas;
        }
    }

    private void ChartTypeButton_MouseEnter(object sender, MouseEventArgs e)
    {
        ChartTypeButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xf0, 0xff));
    }

    private void ChartTypeButton_MouseLeave(object sender, MouseEventArgs e)
    {
        ChartTypeButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2a, 0x55, 0x68));
    }

    private void MenuOption_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
            border.Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x3a, 0x4d));
    }

    private void MenuOption_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
            border.Background = Brushes.Transparent;
    }

    private void DrawXAxisCandles(double chartWidth, int startIdx, int endIdx)
    {
        XAxisCanvas.Children.Clear();
        var vm = Vm;
        if (vm == null || vm.CandleValues.Count < 2) return;

        int numVisible = endIdx - startIdx;
        if (numVisible < 2) return;
        double drawWidth = chartWidth - PadRight;
        double gap = drawWidth / numVisible;

        int maxLabels = Math.Max(2, (int)(chartWidth / 70));
        int labelCount = Math.Min(maxLabels, numVisible);
        if (labelCount < 2) return;

        for (int i = 0; i < labelCount; i++)
        {
            int localIdx = (int)((long)i * (numVisible - 1) / (labelCount - 1));
            double x = localIdx * gap + gap / 2;

            var epoch = vm.CandleValues[startIdx + localIdx].Epoch;
            string label = epoch > 0
                ? DateTimeOffset.FromUnixTimeSeconds(epoch).ToString("HH:mm")
                : (startIdx + localIdx + 1).ToString();

            var tb = new TextBlock
            {
                Text = label,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 9,
                Foreground = AxisBrush
            };
            double labelX = x - 16;
            if (i == 0) labelX = Math.Max(8, labelX);
            Canvas.SetLeft(tb, labelX);
            Canvas.SetTop(tb, 2);
            XAxisCanvas.Children.Add(tb);
        }
    }

    private void DrawCandles(int startIdx, int endIdx, double chartWidth, double chartHeight)
    {
        ChartCanvas.Children.Clear();
        _chartLine = null;
        _crosshairLine = null;
        _tooltipBorder = null;
        _tooltipText = null;
        _tooltipDot = null;

        var vm = Vm;
        if (vm == null || vm.CandleValues.Count == 0) return;

        var candles = vm.CandleValues;
        int numVisible = endIdx - startIdx;
        if (numVisible < 1) return;

        // Calculate min/max only for visible candles
        double candleMin = double.MaxValue;
        double candleMax = double.MinValue;
        for (int i = startIdx; i < endIdx; i++)
        {
            var c = candles[i];
            if ((double)c.Low < candleMin) candleMin = (double)c.Low;
            if ((double)c.High > candleMax) candleMax = (double)c.High;
        }
        var candleRange = candleMax - candleMin;
        if (candleRange == 0) candleRange = 1;

        _drawPadTop = chartHeight * 0.06;
        double padBot = chartHeight * 0.06;
        _drawH = chartHeight - _drawPadTop - padBot;
        _drawMin = candleMin;
        _drawRange = candleRange;

        double drawWidth = chartWidth - PadRight;
        double gap = drawWidth / numVisible;
        double candleWidth = Math.Max(2, gap * 0.7);

        for (int i = 0; i < numVisible; i++)
        {
            var c = candles[startIdx + i];
            bool bullish = c.Close >= c.Open;
            var brush = bullish ? GreenBrush : RedBrush;

            double x = i * gap + gap / 2;
            double yHigh  = _drawPadTop + _drawH - (((double)c.High - candleMin) / candleRange) * _drawH;
            double yLow   = _drawPadTop + _drawH - (((double)c.Low - candleMin) / candleRange) * _drawH;
            double yOpen  = _drawPadTop + _drawH - (((double)c.Open - candleMin) / candleRange) * _drawH;
            double yClose = _drawPadTop + _drawH - (((double)c.Close - candleMin) / candleRange) * _drawH;

            var wick = new Line
            {
                X1 = x, X2 = x, Y1 = yHigh, Y2 = yLow,
                Stroke = brush, StrokeThickness = 1
            };
            ChartCanvas.Children.Add(wick);

            double bodyTop = Math.Min(yOpen, yClose);
            double bodyHeight = Math.Max(1, Math.Abs(yOpen - yClose));

            var body = new Rectangle
            {
                Width = candleWidth,
                Height = bodyHeight,
                Fill = brush,
                Stroke = brush,
                StrokeThickness = 1
            };
            Canvas.SetLeft(body, x - candleWidth / 2);
            Canvas.SetTop(body, bodyTop);
            ChartCanvas.Children.Add(body);
        }

        DrawYAxis(candleMin, candleMax);
    }
}
