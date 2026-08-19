using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Domain.Contracts;
using TheUnhingedProtocol.Infrastructure.Persistence;
using TheUnhingedProtocol.Infrastructure.Search;
using TheUnhingedProtocol.Infrastructure.Shell;

namespace TheUnhingedProtocol.Domain.Tests;

public sealed class Phase1OperationsTests
{
    [Fact]
    public async Task VisibilityPreferencesPersistAtomicallyAndHotKeysCannotConflict()
    {
        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Path, "settings.json");
        using JsonOrganizerPreferencesService service = new(path);
        OrganizerPreferences saved = await service.SaveAsync(new OrganizerPreferences
        {
            IsOrganizerVisible = false,
            DesktopGesture = DesktopGestureAction.ToggleOrganizerVisibility,
            VisibilityHotKey = new HotKeyGesture { Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Shift, VirtualKey = 'U' },
        }, TestContext.Current.CancellationToken);

        OrganizerPreferences restored = await service.GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(saved, restored);
        Assert.False(restored.IsOrganizerVisible);
        Assert.Equal(DesktopGestureAction.ToggleOrganizerVisibility, restored.DesktopGesture);
        Assert.Empty(Directory.EnumerateFiles(temporary.Path, "*.tmp"));
        Assert.Throws<InvalidDataException>(() => (restored with
        {
            PeekHotKey = restored.VisibilityHotKey,
        }).EnsureValid());
    }

    [Fact]
    public void PerContainerAndGlobalVisibilityRemainIndependent()
    {
        ContainerDefinition hidden = ContainerDefinition.CreateReferenceGroup("Private").WithVisibility(false);
        OrganizerPreferences global = new() { IsOrganizerVisible = true };

        Assert.False(hidden.IsVisible);
        Assert.True(global.IsOrganizerVisible);
        Assert.True((global with { IsOrganizerVisible = false }).EnsureValid().IsOrganizerVisible is false);
        Assert.False(hidden.WithVisibility(false).IsVisible);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    [InlineData(2.25)]
    [InlineData(2.5)]
    [InlineData(3.0)]
    public void DisplayRecoveryKeepsEverySurfaceReachableAcrossScaleAndResolution(double scale)
    {
        ContainerBounds saved = ContainerBounds.Create(1540, 780, 360, 260);
        DisplayRectangle previous = new(0, 0, 1920, 1040);
        DisplayRectangle current = new(0, 0, 1366, 728);

        ContainerBounds recovered = DisplayRecoveryPolicy.Recover(saved, previous, current, 1, scale);

        Assert.InRange(recovered.X, 0, current.Width - recovered.Width);
        Assert.InRange(recovered.Y, 0, current.Height - recovered.Height);
        Assert.InRange(recovered.Width, ContainerBounds.MinimumWidth, current.Width);
        Assert.InRange(recovered.Height, ContainerBounds.MinimumHeight, current.Height);
    }

    [Fact]
    public void DisplayFingerprintIsStableAcrossEnumerationOrderAndExposesVirtualDesktopLimit()
    {
        DisplayDescriptor primary = new() { Id = "DISPLAY1", Name = "Primary", IsPrimary = true };
        DisplayDescriptor secondary = new()
        {
            Id = "DISPLAY2",
            Name = "Secondary",
            Bounds = new DisplayRectangle(1920, 0, 2560, 1440),
            WorkArea = new DisplayRectangle(1920, 0, 2560, 1400),
            Scale = 1.5,
        };
        DisplayProfile first = DisplayProfile.Create([primary, secondary], false);
        DisplayProfile second = DisplayProfile.Create([secondary, primary], false);

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.False(first.VirtualDesktopPlacementAvailable);
    }

    [Fact]
    public async Task SnapshotsRoundTripCompareRestoreAndNeverTouchReferencedFiles()
    {
        using TemporaryDirectory temporary = new();
        string database = Path.Combine(temporary.Path, "state.db");
        string source = Path.Combine(temporary.Path, "source.txt");
        await File.WriteAllTextAsync(source, "untouched", TestContext.Current.CancellationToken);
        byte[] before;
        await using (FileStream stream = File.OpenRead(source))
        {
            before = await SHA256.HashDataAsync(stream, TestContext.Current.CancellationToken);
        }
        SqliteContainerService containers = new(database);
        ContainerDefinition container = await containers.CreateReferenceGroupAsync(
            "Snapshot source",
            ContainerBounds.Create(40, 60, 320, 240),
            TestContext.Current.CancellationToken);
        container = container.AddItem(ItemReference.Create(source, ItemKind.File));
        await containers.UpdateAsync(container, TestContext.Current.CancellationToken);
        SqliteLayoutSnapshotService layouts = new(database);
        DisplayProfile profile = TestProfile();

        LayoutArchive snapshot = await layouts.CreateAsync("Baseline", LayoutSnapshotKind.Manual, profile, TestContext.Current.CancellationToken);
        string export = Path.Combine(temporary.Path, "baseline.json");
        await layouts.ExportAsync(snapshot.Id, export, TestContext.Current.CancellationToken);
        LayoutArchive imported = await layouts.ImportAsync(export, TestContext.Current.CancellationToken);
        Assert.Equal(snapshot.Checksum, imported.Checksum);

        await containers.UpdateBoundsAsync(container.Id, ContainerBounds.Create(500, 300, 420, 300), TestContext.Current.CancellationToken);
        LayoutDifference difference = await layouts.CompareAsync(snapshot.Id, profile, TestContext.Current.CancellationToken);
        Assert.Equal(1, difference.ChangedContainers);
        await layouts.RestoreAsync(snapshot.Id, TestContext.Current.CancellationToken);
        ContainerDefinition restored = Assert.Single(await containers.GetAllAsync(TestContext.Current.CancellationToken));
        Assert.Equal(40, restored.Bounds.X);
        Assert.Contains((await layouts.GetAllAsync(TestContext.Current.CancellationToken)), item => item.Kind == LayoutSnapshotKind.Recovery);
        byte[] after;
        await using (FileStream stream = File.OpenRead(source))
        {
            after = await SHA256.HashDataAsync(stream, TestContext.Current.CancellationToken);
        }
        Assert.Equal(before, after);
        Assert.Equal("untouched", await File.ReadAllTextAsync(source, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SnapshotCorruptionIsRejectedBeforeStateChanges()
    {
        using TemporaryDirectory temporary = new();
        string database = Path.Combine(temporary.Path, "state.db");
        SqliteContainerService containers = new(database);
        ContainerDefinition original = await containers.CreateReferenceGroupAsync(
            "Original",
            new ContainerBounds(),
            TestContext.Current.CancellationToken);
        SqliteLayoutSnapshotService layouts = new(database);
        LayoutArchive snapshot = await layouts.CreateAsync("Safe", LayoutSnapshotKind.Manual, TestProfile(), TestContext.Current.CancellationToken);
        string path = Path.Combine(temporary.Path, "corrupt.json");
        await layouts.ExportAsync(snapshot.Id, path, TestContext.Current.CancellationToken);
        string json = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(path, json.Replace("Safe", "Tampered", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => layouts.ImportAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(original.Id, Assert.Single(await containers.GetAllAsync(TestContext.Current.CancellationToken)).Id);
    }

    [Fact]
    public async Task AutomaticSnapshotHistoryIsBoundedAndPruned()
    {
        using TemporaryDirectory temporary = new();
        SqliteLayoutSnapshotService layouts = new(Path.Combine(temporary.Path, "state.db"));
        for (int index = 0; index < 24; index++)
        {
            await layouts.CreateAsync($"Automatic {index}", LayoutSnapshotKind.Automatic, TestProfile(), TestContext.Current.CancellationToken);
        }

        IReadOnlyList<LayoutArchive> snapshots = await layouts.GetAllAsync(TestContext.Current.CancellationToken);
        Assert.Equal(20, snapshots.Count(snapshot => snapshot.Kind == LayoutSnapshotKind.Automatic));
    }

    [Fact]
    public async Task UnifiedSearchCoversEveryLocalSourceAndRanksDeterministically()
    {
        ContainerDefinition container = ContainerDefinition.CreateReferenceGroup("Development")
            .WithPresentation("Development", "Current code", ["work"], ContainerDefinition.ApprovedIconGlyphs[0])
            .AddItem(ItemReference.Create("https://example.com", ItemKind.Url).WithMetadata(
                "Protocol docs", null, ["reference"], null, false));
        FolderPortal portal = FolderPortal.Create("Source portal", Path.GetTempPath());
        FakeWindowsSearchAdapter adapter = new([
            new SearchResult { Id = "indexed", Title = "Protocol indexed note", Source = SearchResultSource.WindowsSearch, Score = 450 },
        ]);
        UnifiedSearchService service = new(adapter);

        SearchResponse response = await service.SearchAsync(
            "protocol",
            [container],
            [portal],
            TestContext.Current.CancellationToken);

        Assert.Contains(response.Results, result => result.Title == "Protocol docs" && result.Source == SearchResultSource.DesktopItem);
        Assert.Contains(response.Results, result => result.Source == SearchResultSource.WindowsSearch);
        Assert.Equal(response.Results.OrderByDescending(result => result.Score).ThenBy(result => result.Title, StringComparer.OrdinalIgnoreCase), response.Results);

        SearchResponse setting = await service.SearchAsync("visibility", [container], [portal], TestContext.Current.CancellationToken);
        Assert.Contains(setting.Results, result => result.Source == SearchResultSource.Setting);
        SearchResponse tag = await service.SearchAsync("work", [container], [portal], TestContext.Current.CancellationToken);
        Assert.Contains(tag.Results, result => result.Source == SearchResultSource.Tag);
        SearchResponse portalResult = await service.SearchAsync("source", [container], [portal], TestContext.Current.CancellationToken);
        Assert.Contains(portalResult.Results, result => result.Source == SearchResultSource.Portal);
    }

    [Fact]
    public async Task WindowsSearchFailureDoesNotBlockLocalResults()
    {
        ContainerDefinition container = ContainerDefinition.CreateReferenceGroup("Local result");
        UnifiedSearchService service = new(new ThrowingWindowsSearchAdapter());

        SearchResponse response = await service.SearchAsync("local", [container], [], TestContext.Current.CancellationToken);

        Assert.Single(response.Results);
        Assert.Equal(SearchAvailability.IndexUnavailable, response.WindowsSearchState);
    }

    [Fact]
    public async Task LargeSearchResultSetIsBoundedResponsiveAndCancelable()
    {
        ContainerDefinition container = ContainerDefinition.CreateReferenceGroup("Performance");
        ItemReference[] items = Enumerable.Range(0, 10_000).Select(index =>
            ItemReference.Create(
                $"https://example.com/item-{index}",
                ItemKind.Url).WithMetadata($"Needle item {index}", null, [], null, false) with
            {
                SortOrder = index,
            }).ToArray();
        ContainerSectionDefinition section = container.Sections[0] with { ItemIds = items.Select(item => item.Id).ToArray() };
        container = (container with { Items = items, Sections = [section] }).EnsureValid();
        UnifiedSearchService service = new(new FakeWindowsSearchAdapter([]));
        Stopwatch stopwatch = Stopwatch.StartNew();
        SearchResponse response = await service.SearchAsync("needle", [container], [], TestContext.Current.CancellationToken);
        stopwatch.Stop();

        Assert.Equal(200, response.Results.Length);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Search took {stopwatch.Elapsed}.");
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SearchAsync("needle", [container], [], canceled.Token));
    }

    [Fact]
    public async Task OnboardingRequiresConsentAndLeavesEverySourceByteUnchanged()
    {
        using TemporaryDirectory temporary = new();
        string desktop = Path.Combine(temporary.Path, "Desktop");
        Directory.CreateDirectory(desktop);
        string document = Path.Combine(desktop, "proposal.txt");
        string image = Path.Combine(desktop, "photo.png");
        await File.WriteAllTextAsync(document, "private document content", TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(image, [1, 2, 3, 4, 5], TestContext.Current.CancellationToken);
        Dictionary<string, byte[]> before = await HashTreeAsync(desktop);
        NonDestructiveOnboardingService service = new(Path.Combine(temporary.Path, "state.db"));

        OnboardingScanResult denied = await service.ScanAsync(desktop, false, TestContext.Current.CancellationToken);
        Assert.Equal(OnboardingScanState.ConsentRequired, denied.State);
        OnboardingScanResult scan = await service.ScanAsync(desktop, true, TestContext.Current.CancellationToken);
        Assert.Equal(OnboardingScanState.Ready, scan.State);
        Assert.Equal(2, scan.Suggestions.Sum(suggestion => suggestion.Candidates.Length));
        IReadOnlyList<ContainerDefinition> created = await service.ApplyAsync(scan.Suggestions, TestContext.Current.CancellationToken);
        Assert.NotEmpty(created);
        Assert.All(created.SelectMany(container => container.Items), item => Assert.False(item.AllowPhysicalMove));
        Dictionary<string, byte[]> after = await HashTreeAsync(desktop);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (string path in before.Keys) Assert.Equal(before[path], after[path]);
    }

    [Fact]
    public async Task CanceledOnboardingCreatesNoPartialOrganizationOrBackgroundWork()
    {
        using TemporaryDirectory temporary = new();
        string desktop = Path.Combine(temporary.Path, "Desktop");
        Directory.CreateDirectory(desktop);
        for (int index = 0; index < 100; index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(desktop, $"file-{index}.txt"),
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                TestContext.Current.CancellationToken);
        }
        string database = Path.Combine(temporary.Path, "state.db");
        NonDestructiveOnboardingService service = new(database);
        using CancellationTokenSource canceled = new();
        canceled.Cancel();

        OnboardingScanResult result = await service.ScanAsync(desktop, true, canceled.Token);
        Assert.Equal(OnboardingScanState.Canceled, result.State);
        await using SqliteConnection connection = new($"Data Source={database}");
        await SqliteSchemaMigrator.MigrateAsync(connection, TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM containers;";
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private static DisplayProfile TestProfile() => DisplayProfile.Create([
        new DisplayDescriptor { Id = "DISPLAY1", Name = "Reference", IsPrimary = true },
    ], false);

    private static async Task<Dictionary<string, byte[]>> HashTreeAsync(string path)
    {
        Dictionary<string, byte[]> hashes = new(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            await using FileStream stream = File.OpenRead(file);
            hashes[Path.GetRelativePath(path, file)] = await SHA256.HashDataAsync(stream, TestContext.Current.CancellationToken);
        }
        return hashes;
    }

    private sealed class FakeWindowsSearchAdapter(SearchResult[] results) : IWindowsSearchAdapter
    {
        public Task<SearchResponse> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult(new SearchResponse
            {
                Results = results,
                WindowsSearchState = SearchAvailability.Ready,
            });
    }

    private sealed class ThrowingWindowsSearchAdapter : IWindowsSearchAdapter
    {
        public Task<SearchResponse> SearchAsync(string query, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Index unavailable");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tup-phase1-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
            GC.SuppressFinalize(this);
        }
    }
}
