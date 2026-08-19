using System.Diagnostics;
using TheUnhingedProtocol.App.ViewModels;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.App.Tests;

public sealed class Phase1QualificationTests
{
    [Fact]
    public void FiveHundredVisibleItemsMaterializeWithinTheUiBudget()
    {
        ContainerDefinition container = ContainerDefinition.CreateReferenceGroup("500 item benchmark");
        ItemReference[] items = Enumerable.Range(0, 500)
            .Select(index => ItemReference.Create($"https://example.com/{index}", ItemKind.Url) with { SortOrder = index })
            .ToArray();
        ContainerSectionDefinition section = container.Sections[0] with
        {
            ItemIds = items.Select(item => item.Id).ToArray(),
        };
        container = (container with { Items = items, Sections = [section] }).EnsureValid();

        Stopwatch stopwatch = Stopwatch.StartNew();
        ContainerCardViewModel viewModel = ContainerCardViewModel.FromDefinition(container);
        stopwatch.Stop();

        Assert.Equal(500, viewModel.Items.Count);
        Assert.Equal(500, viewModel.VisibleItemCount);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"500-item materialization took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void AccessibilityNamesExposeVisibilityLockStateAndSafeActions()
    {
        ContainerDefinition container = ContainerDefinition.CreateReferenceGroup("Accessible") with
        {
            IsVisible = false,
        };
        container = container.WithLayoutOptions(false, true, false);
        ContainerCardViewModel viewModel = ContainerCardViewModel.FromDefinition(container);

        Assert.Contains("hidden", viewModel.AccessibleName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("locked", viewModel.AccessibleName, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.CanChangeLayout);
    }
}
