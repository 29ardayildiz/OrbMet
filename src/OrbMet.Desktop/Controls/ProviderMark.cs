using System.Windows;
using System.Windows.Media;

namespace OrbMet.Desktop.Controls;

/// <summary>A provider logo on its own, filled with <see cref="Fill"/>, scaled to fit.</summary>
public sealed class ProviderMark : FrameworkElement
{
    public static readonly DependencyProperty MarkProperty = DependencyProperty.Register(
        nameof(Mark),
        typeof(string),
        typeof(ProviderMark),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill),
        typeof(Brush),
        typeof(ProviderMark),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Mark
    {
        get => (string)GetValue(MarkProperty);
        set => SetValue(MarkProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (ProviderMarks.Get(Mark) is not { } mark)
        {
            return;
        }

        var size = Math.Min(ActualWidth, ActualHeight);
        var scale = size / ProviderMarks.ViewBoxSize;
        drawingContext.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));
        drawingContext.DrawGeometry(Fill, null, mark);
        drawingContext.Pop();
        drawingContext.Pop();
    }
}
