using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.App.ViewModels;

public sealed class ItemCardViewModel
{
    public ItemCardViewModel(ItemReference reference)
    {
        Reference = reference.UpgradeToCurrent();
        IsAvailable = Reference.Kind switch
        {
            ItemKind.Url => true,
            ItemKind.Folder => Directory.Exists(Reference.CanonicalPath),
            _ => File.Exists(Reference.CanonicalPath),
        };
        ThumbnailUri = CreateThumbnailUri(Reference);
    }

    public ItemReference Reference { get; }

    public Guid Id => Reference.Id;

    public string DisplayName => Reference.DisplayName;

    public string KindLabel => Reference.Kind.ToString();

    public string? Label => Reference.Label;

    public string TagsText => string.Join(", ", Reference.Tags);

    public string Target => Reference.CanonicalPath;

    public bool IsAvailable { get; }

    public string AvailabilityLabel => IsAvailable ? KindLabel : $"{KindLabel} — unavailable";

    public string IconGlyph => Reference.IconGlyph ?? Reference.Kind switch
    {
        ItemKind.Folder => "\uE8B7",
        ItemKind.Shortcut => "\uE71B",
        ItemKind.Application => "\uE7B8",
        ItemKind.Url => "\uE774",
        _ => "\uE8A5",
    };

    public Uri? ThumbnailUri { get; }

    public bool HasThumbnail => ThumbnailUri is not null;

    public string AccessibleName => $"{DisplayName}, {AvailabilityLabel}";

    private static Uri? CreateThumbnailUri(ItemReference reference)
    {
        if (!reference.ShowThumbnail || reference.Kind != ItemKind.File || !File.Exists(reference.CanonicalPath))
        {
            return null;
        }

        string extension = Path.GetExtension(reference.CanonicalPath);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
            ? new Uri(reference.CanonicalPath)
            : null;
    }
}
