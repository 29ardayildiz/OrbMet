using System.Windows;
using System.Windows.Controls;

namespace OrbMet.Desktop.Controls;

/// <summary>
/// One provider's details: title, a row per reported window, and the message
/// and footnote lines (design.md 7). Shared by the hover card and the
/// keyboard details window, so both always say the same thing.
/// </summary>
public partial class ProviderDetails : UserControl
{
    public static readonly DependencyProperty IsKeyboardReachableProperty = DependencyProperty.Register(
        nameof(IsKeyboardReachable),
        typeof(bool),
        typeof(ProviderDetails),
        new PropertyMetadata(false));

    public ProviderDetails()
    {
        InitializeComponent();
    }

    /// <summary>Makes the title, rows and messages tab stops with a visible focus ring.</summary>
    public bool IsKeyboardReachable
    {
        get => (bool)GetValue(IsKeyboardReachableProperty);
        set => SetValue(IsKeyboardReachableProperty, value);
    }
}
