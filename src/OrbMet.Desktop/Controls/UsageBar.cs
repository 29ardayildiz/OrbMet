using System.Windows;
using System.Windows.Media;

namespace OrbMet.Desktop.Controls;

/// <summary>
/// The detail card's horizontal usage bar. Same rules as the ring: a missing
/// value is an empty track, the fill caps at full width while the text keeps
/// the raw value, and colour follows the shared thresholds.
/// </summary>
public sealed class UsageBar : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent),
        typeof(double?),
        typeof(UsageBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsExhaustedProperty = DependencyProperty.Register(
        nameof(IsExhausted),
        typeof(bool),
        typeof(UsageBar),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public double? Percent
    {
        get => (double?)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public bool IsExhausted
    {
        get => (bool)GetValue(IsExhaustedProperty);
        set => SetValue(IsExhaustedProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var height = ActualHeight;
        var width = ActualWidth;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var radius = height / 2;
        drawingContext.DrawRoundedRectangle(UsagePalette.Track, null, new Rect(0, 0, width, height), radius, radius);

        if (Percent is { } percent && percent > 0)
        {
            // Keep a visible sliver for tiny values so 1% does not look like 0%.
            var fill = Math.Max(height, width * Math.Min(percent, 100) / 100.0);
            drawingContext.DrawRoundedRectangle(
                UsagePalette.For(percent, IsExhausted),
                null,
                new Rect(0, 0, Math.Min(fill, width), height),
                radius,
                radius);
        }
    }
}
