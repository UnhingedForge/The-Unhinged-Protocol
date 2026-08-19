using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.App.ViewModels;

public sealed class ContainerSectionViewModel : ObservableObject
{
    private bool isActive;

    public ContainerSectionViewModel(
        ContainerSectionDefinition definition,
        IEnumerable<ItemCardViewModel> items,
        bool isActive)
    {
        Id = definition.Id;
        Name = definition.Name;
        IsExpanded = definition.IsExpanded;
        this.isActive = isActive;
        Items = new ObservableCollection<ItemCardViewModel>(items);
    }

    public Guid Id { get; }

    public string Name { get; }

    public bool IsExpanded { get; }

    public bool IsActive
    {
        get => isActive;
        set => SetProperty(ref isActive, value);
    }

    public ObservableCollection<ItemCardViewModel> Items { get; }

    public string ItemCountLabel => Items.Count == 1 ? "1 item" : $"{Items.Count} items";

    public string AccessibleName => $"{Name}, {ItemCountLabel}{(IsActive ? ", selected" : string.Empty)}";

    public ContainerSectionDefinition CaptureDefinition() => new()
    {
        Id = Id,
        Name = Name,
        IsExpanded = IsExpanded,
        ItemIds = Items.Select(item => item.Id).ToArray(),
    };
}
