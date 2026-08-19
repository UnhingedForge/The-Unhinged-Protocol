using System.Text.Json;
using Microsoft.Data.Sqlite;
using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.Infrastructure.Persistence;

/// <summary>
/// Persists independent portal and tab navigation state in the local SQLite catalog.
/// </summary>
public sealed class SqliteFolderPortalService : IFolderPortalService
{
    private readonly string connectionString;
    private readonly JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web);

    public SqliteFolderPortalService(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        string fullPath = Path.GetFullPath(databasePath);
        string? directoryPath = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("The database path must include a directory.", nameof(databasePath));
        }

        Directory.CreateDirectory(directoryPath);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
    }

    public async Task<IReadOnlyList<FolderPortal>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenMigratedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM folder_portals ORDER BY updated_utc, rowid;";
        List<FolderPortal> portals = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            FolderPortal portal = JsonSerializer.Deserialize<FolderPortal>(reader.GetString(0), serializerOptions)
                ?? throw new InvalidDataException("A stored folder portal payload was empty.");
            portals.Add(portal.EnsureValid());
        }

        return portals;
    }

    public async Task<FolderPortal> CreateAsync(
        string name,
        string folderPath,
        CancellationToken cancellationToken)
    {
        FolderPortal portal = FolderPortal.Create(name, folderPath);
        await using SqliteConnection connection = await OpenMigratedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO folder_portals (id, schema_version, payload_json, updated_utc)
            VALUES ($id, $schemaVersion, $payloadJson, $updatedUtc);
            """;
        AddParameters(command, portal);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return portal;
    }

    public async Task<FolderPortal> UpdateAsync(FolderPortal portal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(portal);
        FolderPortal validated = portal.EnsureValid();
        await using SqliteConnection connection = await OpenMigratedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE folder_portals
            SET schema_version = $schemaVersion,
                payload_json = $payloadJson,
                updated_utc = $updatedUtc
            WHERE id = $id;
            """;
        AddParameters(command, validated);
        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            throw new KeyNotFoundException($"Folder portal {validated.Id:D} was not found.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return validated;
    }

    public async Task DeleteAsync(Guid portalId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenMigratedConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM folder_portals WHERE id = $id;";
        command.Parameters.AddWithValue("$id", portalId.ToString("D"));
        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            throw new KeyNotFoundException($"Folder portal {portalId:D} was not found.");
        }
    }

    private void AddParameters(SqliteCommand command, FolderPortal portal)
    {
        command.Parameters.AddWithValue("$id", portal.Id.ToString("D"));
        command.Parameters.AddWithValue("$schemaVersion", portal.SchemaVersion);
        command.Parameters.AddWithValue("$payloadJson", JsonSerializer.Serialize(portal, serializerOptions));
        command.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
    }

    private async Task<SqliteConnection> OpenMigratedConnectionAsync(CancellationToken cancellationToken)
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
}
