using System.Globalization;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.App.ViewModels;

public sealed class PortalItemViewModel
{
    public PortalItemViewModel(PortalItem item)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
    }

    public PortalItem Item { get; }

    public string Name => Item.Name;

    public string FullPath => Item.FullPath;

    public PortalItemKind Kind => Item.Kind;

    public string TypeLabel => Item.TypeLabel;

    public string SizeLabel => Item.Kind == PortalItemKind.Folder ? string.Empty : FormatSize(Item.SizeBytes);

    public string ModifiedLabel => Item.ModifiedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);

    public string IconGlyph => Item.Kind == PortalItemKind.Folder ? "\uE8B7" : "\uE7C3";

    public string AccessibleName => Item.Kind == PortalItemKind.Folder
        ? $"{Name}, folder, modified {ModifiedLabel}"
        : $"{Name}, {TypeLabel}, {SizeLabel}, modified {ModifiedLabel}";

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1_024 => $"{bytes} B",
        < 1_048_576 => $"{bytes / 1_024d:F1} KB",
        < 1_073_741_824 => $"{bytes / 1_048_576d:F1} MB",
        _ => $"{bytes / 1_073_741_824d:F1} GB",
    };
}
