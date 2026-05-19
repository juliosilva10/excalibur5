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
    private static readonly SolidColorBrush TickBullish = new(Color.FromRgb(0x00, 0xBC, 0xD4));
    private static readonly SolidColorBrush TickBearish = new(Color.FromRgb(0xFF, 0x98, 0x00));
    private static readonly SolidColorBrush CandleBullish = new(Color.FromRgb(0x34, 0xC7, 0x59));
    private static readonly SolidColorBrush CandleBearish = new(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private static readonly SolidColorBrush HighlightBrush = new(Color.FromRgb(0xF0, 0xC0, 0x00));
    private static readonly SolidColorBrush CyanBrush = new(Color.FromRgb(0x00, 0xF0, 0xFF));

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
    }

    private static void DrawAfterLayout(Canvas canvas, CandleSnapshot? candleSnapshot, TickSnapshot? tickSnapshot)
    {
        if (canvas.ActualWidth > 0)
        {
            DrawMiniCandleChart(canvas, candleSnapshot, tickSnapshot);
            return;
        }

        void handler(object? s, EventArgs args)
        {
            canvas.LayoutUpdated -= handler;
            DrawMiniCandleChart(canvas, candleSnapshot, tickSnapshot);
        }

        canvas.LayoutUpdated += handler;
        canvas.InvalidateMeasure();
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
        double padding = 4;

        double drawWidth = width - padding * 2;
        double drawHeight = height - padding * 2;

        double min = (double)candles.Min(c => c.Low);
        double max = (double)candles.Max(c => c.High);
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
}
