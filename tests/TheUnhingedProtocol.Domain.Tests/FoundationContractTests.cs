using System.Text.Json;
using Microsoft.Data.Sqlite;
using TheUnhingedProtocol.Domain.Contracts;
using TheUnhingedProtocol.Infrastructure.Persistence;

namespace TheUnhingedProtocol.Domain.Tests;

public sealed class FoundationContractTests
{
    [Fact]
    public void ExportBundleRoundTripsThroughJson()
    {
        ExportBundle original = new()
        {
            Containers =
            [
                new ContainerDefinition
                {
                    Name = "Foundation",
                    Kind = ContainerKind.ReferenceGroup,
                },
            ],
        };

        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        string json = JsonSerializer.Serialize(original, options);
        ExportBundle? restored = JsonSerializer.Deserialize<ExportBundle>(json, options);

        Assert.NotNull(restored);
        Assert.Equal(ContractSchema.CurrentVersion, restored.SchemaVersion);
        Assert.Single(restored.Containers);
        Assert.Equal("Foundation", restored.Containers[0].Name);
    }

    [Fact]
    public void SafetySensitiveContractsRequireConfirmationByDefault()
    {
        RuleAction action = new();
        ActionPlan plan = new();
        FileOperation operation = new();

        Assert.True(action.RequiresConfirmation);
        Assert.True(plan.RequiresUserConfirmation);
        Assert.Equal(ConflictPolicy.RequireDecision, operation.ConflictPolicy);
    }

    [Fact]
    public async Task FoundationMigrationCreatesExpectedTables()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await SqliteSchemaMigrator.MigrateAsync(connection, TestContext.Current.CancellationToken);

        string[] expectedTables =
        [
            "containers",
            "file_transactions",
            "item_references",
            "layout_snapshots",
            "rules",
            "schema_info",
        ];

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";

        List<string> actualTables = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            actualTables.Add(reader.GetString(0));
        }

        Assert.Equal(expectedTables, actualTables);
    }

    [Fact]
    public void UnsupportedSchemaVersionIsRejected()
    {
        Assert.Throws<NotSupportedException>(() => ContractSchema.EnsureSupported(ContractSchema.CurrentVersion + 1));
    }
}
