using System.Data;
using BackendApi.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace BackendApi.ServiceMigration;

public static class MigrationBaselineCompatibility
{
    public const string BaselineMigrationId =
        "20260614152246_ConsolidatedBaseline20260614";

    private const string ProductVersion = "8.0.11";

    private static readonly string[] LegacyMigrationIds =
    [
        "20260522094410_InitialCreate",
        "20260525040356_Phase3EnterpriseSpatialScaling",
        "20260525044545_AddCustomerMenuFcmExtensions",
        "20260527030012_AddProcessedEventsTable",
        "20260527074129_AddOrderBatchFields",
        "20260528080227_AddShopIdToUser",
        "20260602045055_AddChatMessageAndBatchSize",
        "20260609035732_MeltdownRemediationIndexes",
        "20260611040646_AddDistributedLocks"
    ];

    public static async Task EnsureBaselineHistoryAsync(
        ApplicationDbContext context,
        CancellationToken cancellationToken = default)
    {
        var connection = context.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen)
            await connection.OpenAsync(cancellationToken);

        try
        {
            if (!await HistoryTableExistsAsync(connection, cancellationToken))
                return;

            if (await MigrationExistsAsync(
                    connection,
                    BaselineMigrationId,
                    cancellationToken))
            {
                return;
            }

            var legacyCount = await CountLegacyMigrationsAsync(
                connection,
                cancellationToken);

            if (legacyCount == 0)
                return;

            if (legacyCount != LegacyMigrationIds.Length)
            {
                throw new InvalidOperationException(
                    "Database has a partial legacy EF migration chain. " +
                    "Apply the previous application release through " +
                    $"{LegacyMigrationIds[^1]} before deploying the consolidated baseline.");
            }

            await InsertBaselineHistoryAsync(connection, cancellationToken);
            Log.Information(
                "[ServiceMigration] Stamped consolidated EF baseline {BaselineMigrationId} " +
                "for a database with the complete legacy migration chain.",
                BaselineMigrationId);
        }
        finally
        {
            if (!wasOpen)
                await connection.CloseAsync();
        }
    }

    private static async Task<bool> HistoryTableExistsAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL;";
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<bool> MigrationExistsAsync(
        System.Data.Common.DbConnection connection,
        string migrationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM "__EFMigrationsHistory"
                WHERE "MigrationId" = @migrationId
            );
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@migrationId";
        parameter.Value = migrationId;
        command.Parameters.Add(parameter);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<int> CountLegacyMigrationsAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var parameterNames = new List<string>(LegacyMigrationIds.Length);

        for (var index = 0; index < LegacyMigrationIds.Length; index++)
        {
            var parameterName = $"@legacy{index}";
            parameterNames.Add(parameterName);
            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.Value = LegacyMigrationIds[index];
            command.Parameters.Add(parameter);
        }

        command.CommandText = $"""
            SELECT COUNT(*)
            FROM "__EFMigrationsHistory"
            WHERE "MigrationId" IN ({string.Join(", ", parameterNames)});
            """;

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertBaselineHistoryAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES (@migrationId, @productVersion)
            ON CONFLICT ("MigrationId") DO NOTHING;
            """;

        var migrationParameter = command.CreateParameter();
        migrationParameter.ParameterName = "@migrationId";
        migrationParameter.Value = BaselineMigrationId;
        command.Parameters.Add(migrationParameter);

        var versionParameter = command.CreateParameter();
        versionParameter.ParameterName = "@productVersion";
        versionParameter.Value = ProductVersion;
        command.Parameters.Add(versionParameter);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
