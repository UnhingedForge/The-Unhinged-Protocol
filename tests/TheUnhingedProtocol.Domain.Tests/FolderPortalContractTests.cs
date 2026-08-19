using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.Domain.Tests;

public sealed class FolderPortalContractTests
{
    [Fact]
    public void TabsPreserveIndependentNavigationViewSortAndSearchState()
    {
        string firstPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "portal-first"));
        string secondPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "portal-second"));
        string nestedPath = Path.Combine(firstPath, "nested");
        FolderPortal portal = FolderPortal.Create("Work", firstPath).AddTab(secondPath);
        FolderPortalTab first = portal.Tabs[0]
            .Navigate(nestedPath)
            .WithView(PortalViewMode.Details)
            .WithSort(PortalSortMode.ModifiedNewest)
            .WithSearch("report");
        FolderPortal updated = portal.UpdateTab(first);

        Assert.Equal(nestedPath, updated.Tabs[0].CurrentPath);
        Assert.Equal(PortalViewMode.Details, updated.Tabs[0].ViewMode);
        Assert.Equal(PortalSortMode.ModifiedNewest, updated.Tabs[0].SortMode);
        Assert.Equal("report", updated.Tabs[0].SearchQuery);
        Assert.Equal(secondPath, updated.Tabs[1].CurrentPath);
        Assert.Equal(PortalViewMode.Grid, updated.Tabs[1].ViewMode);
        Assert.Equal(PortalSortMode.NameAscending, updated.Tabs[1].SortMode);
        Assert.Empty(updated.Tabs[1].SearchQuery);
    }

    [Fact]
    public void BackForwardAndUpMaintainBoundedDeterministicHistory()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "portal-history"));
        string child = Path.Combine(root, "child");
        string grandchild = Path.Combine(child, "grandchild");
        FolderPortalTab tab = FolderPortalTab.Create(root).Navigate(child).Navigate(grandchild);

        FolderPortalTab back = tab.GoBack();
        FolderPortalTab forward = back.GoForward();
        FolderPortalTab up = forward.GoUp();

        Assert.Equal(child, back.CurrentPath);
        Assert.Equal(grandchild, forward.CurrentPath);
        Assert.Equal(child, up.CurrentPath);
        Assert.True(back.ForwardHistory.Length > 0);
        Assert.True(up.BackHistory.Length <= FolderPortalTab.MaximumHistoryEntries);
    }

    [Fact]
    public void PortalRejectsRelativeTargetsAndClosingItsOnlyTab()
    {
        Assert.Throws<ArgumentException>(() => FolderPortal.Create("Unsafe", "relative-folder"));

        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "portal-only"));
        FolderPortal portal = FolderPortal.Create("Only", path);
        Assert.Throws<InvalidOperationException>(() => portal.CloseTab(portal.ActiveTabId));
    }

    [Fact]
    public void RootPathRemainsRootedAfterNormalization()
    {
        string root = Path.GetPathRoot(Path.GetTempPath())!;
        FolderPortal portal = FolderPortal.Create("Root", root);

        Assert.Equal(Path.GetFullPath(root), portal.Tabs[0].CurrentPath);
        Assert.Same(portal.Tabs[0], portal.Tabs[0].GoUp());
    }
}
