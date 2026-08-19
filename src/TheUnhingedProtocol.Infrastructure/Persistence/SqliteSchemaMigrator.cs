using System.Reflection;
using Microsoft.Data.Sqlite;

namespace TheUnhingedProtocol.Infrastructure.Persistence;

public static class SqliteSchemaMigrator
{
    private const string MigrationResourceMarker = ".Persistence.Migrations.";

    public static async Task MigrateAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        Assembly assembly = typeof(SqliteSchemaMigrator).Assembly;
        string[] resources = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(MigrationResourceMarker, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (resources.Length == 0)
        {
            throw new InvalidOperationException("No embedded SQLite migrations were found.");
        }

        foreach (string resourceName in resources)
        {
            await using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Migration resource '{resourceName}' could not be opened.");
            using StreamReader reader = new(stream);
            string sql = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
