using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Excalibur5.Models;
using Excalibur5.ViewModels;

namespace Excalibur5.Views.Controls;

public partial class PerformancePanelView : UserControl
{
    private const double ChartPadding = 6;
    private const double EntryExitLineThickness = 1.1;
    private const double EntryExitDashLength = 4;
    private const double EntryExitDashGap = 3;
    private const double EntryArrowLength = 18;
    private const double EntryArrowHeadSize = 5;

    private static readonly SolidColorBrush TickBullish = new(Color.FromRgb(0x00, 0xBC, 0xD4));
    private static readonly SolidColorBrush TickBearish = new(Color.FromRgb(0xFF, 0x98, 0x00));
    private static readonly SolidColorBrush CandleBullish = new(Color.FromRgb(0x34, 0xC7, 0x59));
    private static readonly SolidColorBrush CandleBearish = new(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private static readonly SolidColorBrush HighlightBrush = new(Color.FromRgb(0xF0, 0xC0, 0x00));
    private static readonly SolidColorBrush CyanBrush = new(Color.FromRgb(0x00, 0xF0, 0xFF));
    private static readonly SolidColorBrush EntryGuideBrush = new(Color.FromArgb(0xA8, 0x00, 0x58, 0xFF));
    private static readonly SolidColorBrush ExitGuideBrush = new(Color.FromArgb(0xA8, 0xFF, 0xD7, 0x00));
    private static readonly SolidColorBrush EntryArrowBrush = new(Color.FromRgb(0xFF, 0xD7, 0x00));

    public PerformancePanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PerformanceViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;

        if (e.NewValue is PerformanceViewModel newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PerformanceViewModel.LargestStake))
        {
            var vm = DataContext as PerformanceViewModel;
            DrawAfterLayout(LargestStakeChart, vm?.LargestStake?.CandleSnapshot, vm?.LargestStake?.TickSnapshot);
        }

        if (e.PropertyName == nameof(PerformanceViewModel.MaxDrawdown))
        {
            var vm = DataContext as PerformanceViewModel;
            DrawAfterLayout(DrawdownChart, vm?.MaxDrawdown?.CandleSnapshot, vm?.MaxDrawdown?.TickSnapshot);
        }

        if (e.PropertyName == nameof(PerformanceViewModel.LongestLossStreak))
        {
            var vm = DataContext as PerformanceViewModel;
            DrawAfterLayout(LossStreakChart, vm?.LongestLossStreak?.CandleSnapshot, vm?.LongestLossStreak?.TickSnapshot);
        }

        if (e.PropertyName == nameof(PerformanceViewModel.IsPerformanceVisible))
        {
            var vm = DataContext as PerformanceViewModel;
            if (vm?.IsPerformanceVisible == true)
            {
                DrawAfterLayout(LargestStakeChart, vm.LargestStake?.CandleSnapshot, vm.LargestStake?.TickSnapshot);
                DrawAfterLayout(DrawdownChart, vm.MaxDrawdown?.CandleSnapshot, vm.MaxDrawdown?.TickSnapshot);
                DrawAfterLayout(LossStreakChart, vm.LongestLossStreak?.CandleSnapshot, vm.LongestLossStreak?.TickSnapshot);
            }
        }
    }

    private static void DrawAfterLayout(Canvas canvas, CandleSnapshot? candleSnapshot, TickSnapshot? tickSnapshot)
    {
        if (canvas.ActualWidth > 0)
        {
            DrawMiniCandleChart(canvas, candleSnapshot, tickSnapshot);
            return;
        }

        int attempts = 0;
        void handler(object? s, EventArgs args)
        {
            attempts++;
            if (canvas.ActualWidth > 0)
            {
                canvas.LayoutUpdated -= handler;
                DrawMiniCandleChart(canvas, candleSnapshot, tickSnapshot);
            }
            else if (attempts > 5)
            {
                canvas.LayoutUpdated -= handler;
            }
        }

        canvas.LayoutUpdated += handler;
        canvas.InvalidateMeasure();
        canvas.UpdateLayout();
    }

    private static void DrawMiniCandleChart(Canvas canvas, CandleSnapshot? candleSnapshot, TickSnapshot? tickSnapshot)
    {
        canvas.Children.Clear();

        if (candleSnapshot != null && candleSnapshot.Candles.Count >= 2)
        {
            DrawCandles(canvas, candleSnapshot);
            return;
        }

        if (tickSnapshot != null && tickSnapshot.Values.Count >= 2)
            DrawPolyline(canvas, tickSnapshot);
    }

    private static void DrawCandles(Canvas canvas, CandleSnapshot snapshot)
    {
        var candles = snapshot.Candles;
        double width = canvas.ActualWidth > 0 ? canvas.ActualWidth : 200;
        double height = canvas.ActualHeight > 0 ? canvas.ActualHeight : 86;
        double padding = ChartPadding;

        double drawWidth = width - padding * 2;
        double drawHeight = height - padding * 2;

        double min = (double)candles.Min(c => c.Low);
        double max = (double)candles.Max(c => c.High);
        if (snapshot.EntryPrice.HasValue)
        {
            min = Math.Min(min, (double)snapshot.EntryPrice.Value);
            max = Math.Max(max, (double)snapshot.EntryPrice.Value);
        }
        if (snapshot.ExitPrice.HasValue)
        {
            min = Math.Min(min, (double)snapshot.ExitPrice.Value);
            max = Math.Max(max, (double)snapshot.ExitPrice.Value);
        }
        double range = max - min;
        if (range == 0) range = 1;

        double gap = drawWidth / candles.Count;
        double candleWidth = Math.Max(2, gap * 0.7);

        bool isTickCandles = snapshot.Type == ChartSnapshotType.TickCandles;

        for (int i = 0; i < candles.Count; i++)
        {
            var c = candles[i];
            bool bullish = c.Close >= c.Open;
            bool isHighlight = i == snapshot.HighlightIndex;

            SolidColorBrush brush;
            if (isTickCandles)
                brush = bullish ? TickBullish : TickBearish;
            else
                brush = bullish ? CandleBullish : CandleBearish;

            double x = padding + i * gap + gap / 2;
            double yHigh = padding + drawHeight - (((double)c.High - min) / range) * drawHeight;
            double yLow = padding + drawHeight - (((double)c.Low - min) / range) * drawHeight;
            double yOpen = padding + drawHeight - (((double)c.Open - min) / range) * drawHeight;
            double yClose = padding + drawHeight - (((double)c.Close - min) / range) * drawHeight;

            var wick = new Line
            {
                X1 = x, X2 = x, Y1 = yHigh, Y2 = yLow,
                Stroke = isHighlight ? HighlightBrush : brush,
                StrokeThickness = isHighlight ? 1.5 : 1
            };
            canvas.Children.Add(wick);

            double bodyTop = Math.Min(yOpen, yClose);
            double bodyHeight = Math.Max(1, Math.Abs(yOpen - yClose));

            var body = new Rectangle
            {
                Width = candleWidth,
                Height = bodyHeight,
                Fill = brush,
                Stroke = isHighlight ? HighlightBrush : brush,
                StrokeThickness = isHighlight ? 2 : 1
            };
            Canvas.SetLeft(body, x - candleWidth / 2);
            Canvas.SetTop(body, bodyTop);
            canvas.Children.Add(body);
        }

        // ── Desenha indicadores de entrada (compra) e saída (venda) ──
        DrawEntryExitMarkers(canvas, snapshot, padding, drawWidth, drawHeight, min, range, candles.Count);
    }

    private static void DrawPolyline(Canvas canvas, TickSnapshot snapshot)
    {
        var values = snapshot.Values;
        double width = canvas.ActualWidth > 0 ? canvas.ActualWidth : 200;
        double height = canvas.ActualHeight > 0 ? canvas.ActualHeight : 86;
        double padding = 4;

        double drawWidth = width - padding * 2;
        double drawHeight = height - padding * 2;

        decimal min = values.Min();
        decimal max = values.Max();
        decimal range = max - min;
        if (range == 0) range = 1;

        var points = new PointCollection();
        for (int i = 0; i < values.Count; i++)
        {
            double x = padding + (i * drawWidth / (values.Count - 1));
            double y = padding + (double)((max - values[i]) / range) * drawHeight;
            points.Add(new Point(x, y));
        }

        var polyline = new Polyline
        {
            Points = points,
            Stroke = CyanBrush,
            StrokeThickness = 1.2,
            StrokeLineJoin = PenLineJoin.Round
        };

        canvas.Children.Add(polyline);

        var fillPoints = new PointCollection(points);
        fillPoints.Add(new Point(padding + drawWidth, padding + drawHeight));
        fillPoints.Add(new Point(padding, padding + drawHeight));

        var fillPolygon = new Polygon
        {
            Points = fillPoints,
            Fill = new LinearGradientBrush(
                Color.FromArgb(0x33, 0x00, 0xF0, 0xFF),
                Color.FromArgb(0x00, 0x00, 0xF0, 0xFF),
                90)
        };

        canvas.Children.Insert(0, fillPolygon);
    }

    private static void DrawEntryExitMarkers(Canvas canvas, CandleSnapshot snapshot,
        double padding, double drawWidth, double drawHeight,
        double min, double range, int candleCount)
    {
        if (!snapshot.EntryPrice.HasValue && !snapshot.ExitPrice.HasValue)
            return;

        if (snapshot.EntryPrice.HasValue)
        {
            DrawPriceRegionLine(canvas, snapshot.EntryPrice.Value, padding, drawWidth, drawHeight, min, range, EntryGuideBrush);
            DrawEntryArrow(canvas, snapshot, padding, drawWidth, drawHeight, min, range, candleCount);
        }

        if (snapshot.ExitPrice.HasValue)
            DrawPriceRegionLine(canvas, snapshot.ExitPrice.Value, padding, drawWidth, drawHeight, min, range, ExitGuideBrush);
    }

    private static double GetPriceY(decimal price, double padding, double drawHeight, double min, double range)
    {
        return padding + drawHeight - (((double)price - min) / range) * drawHeight;
    }

    private static void DrawPriceRegionLine(Canvas canvas, decimal price,
        double padding, double drawWidth, double drawHeight,
        double min, double range, Brush brush)
    {
        double y = GetPriceY(price, padding, drawHeight, min, range);
        canvas.Children.Add(new Line
        {
            X1 = padding,
            X2 = padding + drawWidth,
            Y1 = y,
            Y2 = y,
            Stroke = brush,
            StrokeThickness = EntryExitLineThickness,
            StrokeDashArray = new DoubleCollection { EntryExitDashLength, EntryExitDashGap },
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
    }

    private static void DrawEntryArrow(Canvas canvas, CandleSnapshot snapshot,
        double padding, double drawWidth, double drawHeight,
        double min, double range, int candleCount)
    {
        int index = GetMarkerIndex(snapshot.EntryIndex, snapshot.HighlightIndex, candleCount);
        double x = GetMarkerX(index, candleCount, padding, drawWidth);
        double yHigh = GetPriceY(snapshot.Candles[index].High, padding, drawHeight, min, range);
        double tipY = Math.Max(padding + EntryArrowLength, yHigh - EntryArrowHeadSize);
        double startY = Math.Max(padding, tipY - EntryArrowLength);

        canvas.Children.Add(CreateEntryArrowShaft(x, startY, tipY));
        canvas.Children.Add(CreateEntryArrowHead(x, tipY));
    }

    private static int GetMarkerIndex(int? index, int fallbackIndex, int count)
    {
        return Math.Clamp(index ?? fallbackIndex, 0, Math.Max(0, count - 1));
    }

    private static double GetMarkerX(int index, int count, double padding, double drawWidth)
    {
        double gap = drawWidth / count;
        return padding + index * gap + gap / 2;
    }

    private static Line CreateEntryArrowShaft(double x, double startY, double tipY)
    {
        return new Line
        {
            X1 = x,
            X2 = x,
            Y1 = startY,
            Y2 = tipY,
            Stroke = EntryArrowBrush,
            StrokeThickness = 1.4,
            StrokeStartLineCap = PenLineCap.Round
        };
    }

    private static Polygon CreateEntryArrowHead(double x, double tipY)
    {
        return new Polygon
        {
            Fill = EntryArrowBrush,
            Points = new PointCollection
            {
                new(x - EntryArrowHeadSize, tipY - EntryArrowHeadSize),
                new(x + EntryArrowHeadSize, tipY - EntryArrowHeadSize),
                new(x, tipY)
            }
        };
    }
}
