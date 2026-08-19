using System.Text.Json;
using Microsoft.Data.Sqlite;
using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.Infrastructure.Persistence;

public sealed class SqliteLayoutSnapshotService : ILayoutSnapshotService
{
    private const int ManualLimit = 50;
    private const int AutomaticLimit = 20;
    private const long MaximumImportBytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private readonly string connectionString;

    public SqliteLayoutSnapshotService(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        string fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    public async Task<IReadOnlyList<LayoutArchive>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM layout_snapshots ORDER BY created_utc DESC, id;";
        List<LayoutArchive> snapshots = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            snapshots.Add(Deserialize(reader.GetString(0)));
        }

        return snapshots;
    }

    public async Task<LayoutArchive> CreateAsync(
        string name,
        LayoutSnapshotKind kind,
        DisplayProfile displayProfile,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(displayProfile);
        if (name.Trim().Length > 80 || !Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(name));
        }

        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        LayoutArchive archive = (await CaptureAsync(
            connection,
            transaction,
            name.Trim(),
            kind,
            displayProfile,
            cancellationToken).ConfigureAwait(false)).WithChecksum();
        await InsertAsync(connection, transaction, archive, cancellationToken).ConfigureAwait(false);
        await PruneAsync(connection, transaction, kind, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return archive;
    }

    public async Task<LayoutDifference> CompareAsync(
        Guid snapshotId,
        DisplayProfile currentDisplayProfile,
        CancellationToken cancellationToken)
    {
        LayoutArchive snapshot = await GetAsync(snapshotId, cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        LayoutArchive current = (await CaptureAsync(
            connection,
            transaction,
            "Current layout",
            LayoutSnapshotKind.Recovery,
            currentDisplayProfile,
            cancellationToken).ConfigureAwait(false)).WithChecksum();
        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

        HashSet<Guid> snapshotContainers = snapshot.Containers.Select(container => container.Id).ToHashSet();
        HashSet<Guid> currentContainers = current.Containers.Select(container => container.Id).ToHashSet();
        Dictionary<Guid, ContainerDefinition> currentById = current.Containers.ToDictionary(container => container.Id);
        int changedContainers = snapshot.Containers.Count(container =>
            currentById.TryGetValue(container.Id, out ContainerDefinition? currentContainer) &&
            JsonSerializer.Serialize(container, SerializerOptions) != JsonSerializer.Serialize(currentContainer, SerializerOptions));
        HashSet<Guid> snapshotItems = snapshot.Containers.SelectMany(container => container.Items).Select(item => item.Id).ToHashSet();
        HashSet<Guid> currentItems = current.Containers.SelectMany(container => container.Items).Select(item => item.Id).ToHashSet();
        return new LayoutDifference
        {
            AddedContainers = currentContainers.Except(snapshotContainers).Count(),
            RemovedContainers = snapshotContainers.Except(currentContainers).Count(),
            ChangedContainers = changedContainers,
            AddedItems = currentItems.Except(snapshotItems).Count(),
            RemovedItems = snapshotItems.Except(currentItems).Count(),
            DisplayProfileChanged = !string.Equals(
                snapshot.DisplayProfile.Fingerprint,
                currentDisplayProfile.Fingerprint,
                StringComparison.Ordinal),
        };
    }

    public async Task RestoreAsync(Guid snapshotId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        LayoutArchive target = await GetAsync(connection, transaction, snapshotId, cancellationToken).ConfigureAwait(false);
        LayoutArchive recovery = (await CaptureAsync(
            connection,
            transaction,
            $"Before restore {DateTimeOffset.Now:g}",
            LayoutSnapshotKind.Recovery,
            target.DisplayProfile,
            cancellationToken).ConfigureAwait(false)).WithChecksum();
        await InsertAsync(connection, transaction, recovery, cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, transaction, "DELETE FROM containers;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, "DELETE FROM folder_portals;", cancellationToken).ConfigureAwait(false);
        foreach (ContainerDefinition container in target.Containers)
        {
            await InsertContainerAsync(connection, transaction, container.UpgradeToCurrent(), cancellationToken).ConfigureAwait(false);
        }

        foreach (FolderPortal portal in target.Portals)
        {
            await InsertPortalAsync(connection, transaction, portal, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportAsync(Guid snapshotId, string destinationPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        LayoutArchive archive = await GetAsync(snapshotId, cancellationToken).ConfigureAwait(false);
        string fullPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, archive, SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public async Task<LayoutArchive> ImportAsync(string sourcePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        FileInfo file = new(Path.GetFullPath(sourcePath));
        if (!file.Exists || file.Length > MaximumImportBytes)
        {
            throw new InvalidDataException("The layout import is missing or exceeds the 10 MB safety limit.");
        }

        await using FileStream stream = file.OpenRead();
        LayoutArchive archive = await JsonSerializer.DeserializeAsync<LayoutArchive>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The layout import was empty.");
        archive.EnsureValid();
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await InsertAsync(connection, transaction, archive, cancellationToken).ConfigureAwait(false);
        await PruneAsync(connection, transaction, archive.Kind, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return archive;
    }

    private async Task<LayoutArchive> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        LayoutArchive archive = await GetAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false);
        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        return archive;
    }

    private static async Task<LayoutArchive> GetAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT payload_json FROM layout_snapshots WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string json
            ? Deserialize(json)
            : throw new KeyNotFoundException($"Layout snapshot {id:D} was not found.");
    }

    private static async Task<LayoutArchive> CaptureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        LayoutSnapshotKind kind,
        DisplayProfile displayProfile,
        CancellationToken cancellationToken)
    {
        List<ContainerDefinition> containers = [];
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT payload_json FROM containers ORDER BY id;";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                containers.Add(JsonSerializer.Deserialize<ContainerDefinition>(reader.GetString(0), SerializerOptions)!.UpgradeToCurrent());
            }
        }

        List<FolderPortal> portals = [];
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT payload_json FROM folder_portals ORDER BY id;";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                portals.Add(JsonSerializer.Deserialize<FolderPortal>(reader.GetString(0), SerializerOptions)!.EnsureValid());
            }
        }

        return new LayoutArchive
        {
            Name = name,
            Kind = kind,
            DisplayProfile = displayProfile,
            Containers = [.. containers],
            Portals = [.. portals],
        };
    }

    private static async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LayoutArchive archive,
        CancellationToken cancellationToken)
    {
        archive.EnsureValid();
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO layout_snapshots (id, schema_version, payload_json, created_utc)
            VALUES ($id, $version, $json, $created);
            """;
        command.Parameters.AddWithValue("$id", archive.Id.ToString("D"));
        command.Parameters.AddWithValue("$version", archive.SchemaVersion);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(archive, SerializerOptions));
        command.Parameters.AddWithValue("$created", archive.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task PruneAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LayoutSnapshotKind kind,
        CancellationToken cancellationToken)
    {
        int limit = kind == LayoutSnapshotKind.Automatic ? AutomaticLimit : ManualLimit;
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM layout_snapshots
            WHERE id IN (
                SELECT id FROM layout_snapshots
                WHERE json_extract(payload_json, '$.kind') = $kind
                ORDER BY created_utc DESC, id
                LIMIT -1 OFFSET $limit
            );
            """;
        command.Parameters.AddWithValue("$kind", (int)kind);
        command.Parameters.AddWithValue("$limit", limit);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertContainerAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ContainerDefinition container,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO containers (id, schema_version, payload_json, updated_utc) VALUES ($id, $version, $json, $updated);";
        command.Parameters.AddWithValue("$id", container.Id.ToString("D"));
        command.Parameters.AddWithValue("$version", container.SchemaVersion);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(container, SerializerOptions));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertPortalAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FolderPortal portal,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO folder_portals (id, schema_version, payload_json, updated_utc) VALUES ($id, $version, $json, $updated);";
        command.Parameters.AddWithValue("$id", portal.Id.ToString("D"));
        command.Parameters.AddWithValue("$version", portal.SchemaVersion);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(portal, SerializerOptions));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(connectionString);
        try
        {
            await SqliteSchemaMigrator.MigrateAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static LayoutArchive Deserialize(string json) =>
        (JsonSerializer.Deserialize<LayoutArchive>(json, SerializerOptions)
         ?? throw new InvalidDataException("A stored layout snapshot was empty.")).EnsureValid();
}
