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
        double padding = 4;

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

    /// <summary>
    /// Desenha marcadores visuais de entrada (compra) e saída (venda) sobre o gráfico de candles.
    /// </summary>
    private static void DrawEntryExitMarkers(Canvas canvas, CandleSnapshot snapshot,
        double padding, double drawWidth, double drawHeight,
        double min, double range, int candleCount)
    {
        if (!snapshot.EntryPrice.HasValue && !snapshot.ExitPrice.HasValue)
            return;

        // ── Indicador de ENTRADA (compra) — triângulo verde apontando pra cima ──
        if (snapshot.EntryPrice.HasValue)
        {
            double entryPrice = (double)snapshot.EntryPrice.Value;
            double entryY = padding + drawHeight - ((entryPrice - min) / range) * drawHeight;
            double entryX = GetMarkerX(snapshot.EntryIndex, snapshot.HighlightIndex, candleCount, padding, drawWidth);

            // Pequeno triângulo ▲
            var entryTriangle = new Polygon
            {
                Points = new PointCollection
                {
                    new(entryX, entryY - 5),
                    new(entryX - 4, entryY + 3),
                    new(entryX + 4, entryY + 3)
                },
                Fill = new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59)), // verde
                Stroke = Brushes.White,
                StrokeThickness = 0.5,
                ToolTip = $"Entrada: {snapshot.EntryPrice.Value}"
            };
            canvas.Children.Add(entryTriangle);

            // Linha tracejada horizontal no preço de entrada
            var entryLine = new Line
            {
                X1 = padding, X2 = padding + drawWidth,
                Y1 = entryY, Y2 = entryY,
                Stroke = new SolidColorBrush(Color.FromArgb(0x88, 0x34, 0xC7, 0x59)),
                StrokeThickness = 0.8,
                StrokeDashArray = new DoubleCollection { 3, 3 }
            };
            canvas.Children.Add(entryLine);
            canvas.Children.Add(CreateVerticalMarkerLine(padding, drawHeight, entryX, entryLine.Stroke));
        }

        // ── Indicador de SAÍDA (venda) — triângulo vermelho apontando pra baixo ──
        if (snapshot.ExitPrice.HasValue)
        {
            double exitPrice = (double)snapshot.ExitPrice.Value;
            double exitY = padding + drawHeight - ((exitPrice - min) / range) * drawHeight;
            double exitX = GetMarkerX(snapshot.ExitIndex, snapshot.HighlightIndex, candleCount, padding, drawWidth);

            // Pequeno triângulo ▼
            var exitTriangle = new Polygon
            {
                Points = new PointCollection
                {
                    new(exitX, exitY + 5),
                    new(exitX - 4, exitY - 3),
                    new(exitX + 4, exitY - 3)
                },
                Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)), // vermelho
                Stroke = Brushes.White,
                StrokeThickness = 0.5,
                ToolTip = $"Saída: {snapshot.ExitPrice.Value}"
            };
            canvas.Children.Add(exitTriangle);

            // Linha tracejada horizontal no preço de saída
            var exitLine = new Line
            {
                X1 = padding, X2 = padding + drawWidth,
                Y1 = exitY, Y2 = exitY,
                Stroke = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0x6B, 0x6B)),
                StrokeThickness = 0.8,
                StrokeDashArray = new DoubleCollection { 3, 3 }
            };
            canvas.Children.Add(exitLine);
            canvas.Children.Add(CreateVerticalMarkerLine(padding, drawHeight, exitX, exitLine.Stroke));
        }
    }

    private static double GetMarkerX(int? index, int fallbackIndex, int count, double padding, double drawWidth)
    {
        int markerIndex = Math.Clamp(index ?? fallbackIndex, 0, Math.Max(0, count - 1));
        double gap = drawWidth / count;
        return padding + markerIndex * gap + gap / 2;
    }

    private static Line CreateVerticalMarkerLine(double padding, double drawHeight, double x, Brush brush)
    {
        return new Line
        {
            X1 = x, X2 = x,
            Y1 = padding, Y2 = padding + drawHeight,
            Stroke = brush,
            StrokeThickness = 0.8,
            StrokeDashArray = new DoubleCollection { 2, 3 }
        };
    }
}
