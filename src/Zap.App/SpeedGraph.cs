using System.Windows;
using System.Windows.Media;

namespace Zap.App;

/// <summary>Live area chart of the most recent speed samples.</summary>
public sealed class SpeedGraph : FrameworkElement
{
    const int MaxSamples = 120; // 30 s at 4 samples/s
    readonly List<double> _samples = [];
    static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)), 1));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(SpeedGraph), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(SpeedGraph), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public void Add(double value)
    {
        _samples.Add(Math.Max(0, value));
        if (_samples.Count > MaxSamples) _samples.RemoveAt(0);
        InvalidateVisual();
    }

    public void Clear()
    {
        _samples.Clear();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        for (int i = 1; i <= 3; i++) dc.DrawLine(GridPen, new Point(0, h * i / 4), new Point(w, h * i / 4));
        if (_samples.Count < 2) return;

        double max = _samples.Max() * 1.15;
        if (max <= 0) max = 1;
        double step = w / (_samples.Count - 1); // stretch to full width; scrolls once MaxSamples is reached
        Point At(int i) => new(i * step, h - _samples[i] / max * (h - 4));

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(At(0), false, false);
            for (int i = 1; i < _samples.Count; i++) ctx.LineTo(At(i), true, true);
        }
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(0, h), true, true);
            for (int i = 0; i < _samples.Count; i++) ctx.LineTo(At(i), false, true);
            ctx.LineTo(new Point((_samples.Count - 1) * step, h), false, false);
        }
        dc.DrawGeometry(Fill, null, area);
        dc.DrawGeometry(null, new Pen(Stroke, 2) { LineJoin = PenLineJoin.Round }, line);
    }

    static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
