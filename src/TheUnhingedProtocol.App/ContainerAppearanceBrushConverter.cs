using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using TheUnhingedProtocol.Domain.Contracts;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace TheUnhingedProtocol.App;

public sealed class ContainerAppearanceBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        UISettings settings = new();
        if (new AccessibilitySettings().HighContrast)
        {
            return new SolidColorBrush(settings.GetColorValue(UIColorType.Background));
        }

        string[] parts = value?.ToString()?.Split('.') ?? [];
        _ = Enum.TryParse(parts.FirstOrDefault(), out ContainerColor color);
        _ = Enum.TryParse(parts.ElementAtOrDefault(1), out ContainerBackgroundStyle backgroundStyle);
        Color approved = color switch
        {
            ContainerColor.Blue => Color.FromArgb(255, 61, 126, 255),
            ContainerColor.Teal => Color.FromArgb(255, 31, 164, 150),
            ContainerColor.Amber => Color.FromArgb(255, 211, 139, 24),
            ContainerColor.Rose => Color.FromArgb(255, 207, 86, 123),
            ContainerColor.Neutral => settings.GetColorValue(UIColorType.Foreground),
            _ => Color.FromArgb(255, 124, 108, 242),
        };
        Color background = settings.GetColorValue(UIColorType.Background);
        double tint = backgroundStyle == ContainerBackgroundStyle.SubtleTint ? 0.18 : 0.08;
        return new SolidColorBrush(Color.FromArgb(
            255,
            Blend(background.R, approved.R, tint),
            Blend(background.G, approved.G, tint),
            Blend(background.B, approved.B, tint)));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    private static byte Blend(byte background, byte tint, double amount) =>
        (byte)Math.Round((background * (1 - amount)) + (tint * amount));
}

public sealed class ContainerIconBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        UISettings settings = new();
        Color foreground = settings.GetColorValue(UIColorType.Foreground);
        if (new AccessibilitySettings().HighContrast ||
            string.Equals(value?.ToString(), nameof(ContainerIconTreatment.Monochrome), StringComparison.Ordinal))
        {
            return new SolidColorBrush(foreground);
        }

        if (string.Equals(value?.ToString(), nameof(ContainerIconTreatment.Neutral), StringComparison.Ordinal))
        {
            return new SolidColorBrush(Color.FromArgb(190, foreground.R, foreground.G, foreground.B));
        }

        return new SolidColorBrush(settings.GetColorValue(UIColorType.Accent));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
