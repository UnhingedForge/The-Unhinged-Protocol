using System.Text.Json;
using Microsoft.Data.Sqlite;
using TheUnhingedProtocol.Application.Contracts;
using TheUnhingedProtocol.Domain.Contracts;

namespace TheUnhingedProtocol.Infrastructure.Persistence;

/// <summary>
/// Persists reference containers as versioned JSON inside the local SQLite catalog.
/// </summary>
public sealed class SqliteContainerService : IContainerService
{
    private readonly string connectionString;
    private readonly JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web);

    public SqliteContainerService(string databasePath)
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
        }.ToString();
    }

    public async Task<IReadOnlyList<ContainerDefinition>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenMigratedConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT payload_json FROM containers ORDER BY updated_utc, rowid;";

        List<ContainerDefinition> containers = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ContainerDefinition container = JsonSerializer.Deserialize<ContainerDefinition>(
                reader.GetString(0),
                serializerOptions)
                ?? throw new InvalidDataException("A stored container payload was empty.");
            containers.Add(container.UpgradeToCurrent());
        }

        return containers;
    }

    public async Task<ContainerDefinition> CreateReferenceGroupAsync(
        string name,
        ContainerBounds bounds,
        CancellationToken cancellationToken)
    {
        ContainerDefinition container = ContainerDefinition.CreateReferenceGroup(name, bounds);
        string payload = JsonSerializer.Serialize(container, serializerOptions);

        await using SqliteConnection connection = await OpenMigratedConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO containers (id, schema_version, payload_json, updated_utc)
            VALUES ($id, $schemaVersion, $payloadJson, $updatedUtc);
            """;
        command.Parameters.AddWithValue("$id", container.Id.ToString("D"));
        command.Parameters.AddWithValue("$schemaVersion", container.SchemaVersion);
        command.Parameters.AddWithValue("$payloadJson", payload);
        command.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return container;
    }

    public async Task<ContainerDefinition> UpdateBoundsAsync(
        Guid containerId,
        ContainerBounds bounds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        bounds.EnsureValid();

        await using SqliteConnection connection = await OpenMigratedConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand readCommand = connection.CreateCommand();
        readCommand.CommandText = "SELECT payload_json FROM containers WHERE id = $id;";
        readCommand.Parameters.AddWithValue("$id", containerId.ToString("D"));
        object? payloadValue = await readCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (payloadValue is not string payload)
        {
            throw new KeyNotFoundException($"Container {containerId:D} was not found.");
        }

        ContainerDefinition existing = JsonSerializer.Deserialize<ContainerDefinition>(
            payload,
            serializerOptions)
            ?? throw new InvalidDataException("The stored container payload was empty.");
        ContainerDefinition updated = existing.UpgradeToCurrent().WithBounds(bounds);

        return await UpdateAsync(connection, updated, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ContainerDefinition> UpdateAsync(
        ContainerDefinition container,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);
        ContainerDefinition validated = container.UpgradeToCurrent();

        await using SqliteConnection connection = await OpenMigratedConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        return await UpdateAsync(connection, validated, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ContainerDefinition> UpdateAsync(
        SqliteConnection connection,
        ContainerDefinition container,
        CancellationToken cancellationToken)
    {
        string payload = JsonSerializer.Serialize(container, serializerOptions);
        await using SqliteTransaction transaction = connection.BeginTransaction();

        await using SqliteCommand updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = """
            UPDATE containers
            SET schema_version = $schemaVersion,
                payload_json = $payloadJson,
                updated_utc = $updatedUtc
            WHERE id = $id;
            """;
        updateCommand.Parameters.AddWithValue("$schemaVersion", container.SchemaVersion);
        updateCommand.Parameters.AddWithValue("$payloadJson", payload);
        updateCommand.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
        updateCommand.Parameters.AddWithValue("$id", container.Id.ToString("D"));
        int affectedRows = await updateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException("The container layout update did not affect exactly one row.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return container;
    }

    private async Task<SqliteConnection> OpenMigratedConnectionAsync(
        CancellationToken cancellationToken)
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
