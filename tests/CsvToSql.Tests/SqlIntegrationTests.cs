using CsvToSql.Core.Import;
using Microsoft.Data.SqlClient;

namespace CsvToSql.Tests;

/// <summary>
/// Runs against a real SQL Server when CSVTOSQL_TEST_SQL_SERVER, CSVTOSQL_TEST_SQL_USER and CSVTOSQL_TEST_SQL_PASSWORD are set,
/// e.g. the mcr.microsoft.com/mssql/server container started by scripts/start-test-sql.sh.
/// </summary>
public class SqlIntegrationTests
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("CSVTOSQL_TEST_SQL_SERVER");
    private static readonly string? User = Environment.GetEnvironmentVariable("CSVTOSQL_TEST_SQL_USER");
    private static readonly string? Password = Environment.GetEnvironmentVariable("CSVTOSQL_TEST_SQL_PASSWORD");

    private static SqlDestinationOptions Destination(string table, string schema = "stagingUntyped") => new()
    {
        Server = Server!,
        Database = "master",
        Schema = schema,
        Table = table,
        Username = User,
        Password = Password,
        TrustServerCertificate = true,
        CreateSchema = true,
        DropTable = true,
        TextLength = 4000,
    };

    private static T Scalar<T>(SqlDestinationOptions d, string sql)
    {
        using var conn = CsvToSql.Core.Sql.SqlConnectionFactory.Create(d);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        return (T)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void ImportsFixtureWithProductionOptions()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "CSVTOSQL_TEST_SQL_SERVER not set");
        var table = "DF0001152_" + Guid.NewGuid().ToString("D");
        var d = Destination(table);
        var o = ImporterTests.ProductionLikeOptions(TestFiles.Fixture);
        o.DestNone = false;
        o.Sql = d;

        Assert.Equal(2, new CsvImporter(o).Run().RowsWritten);
        Assert.Equal(2, Scalar<int>(d, $"SELECT COUNT(*) FROM [stagingUntyped].[{table}]"));
        Assert.Equal(69, Scalar<int>(d, $"SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'[stagingUntyped].[{table}]')"));
        Assert.Equal("varchar", Scalar<string>(d, $"SELECT TOP 1 t.name FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID(N'[stagingUntyped].[{table}]') AND c.name = 'Event Date'"));
        Assert.Equal(4000, Scalar<short>(d, $"SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID(N'[stagingUntyped].[{table}]') AND name = 'Event Date'"));
        Assert.Equal(2, Scalar<int>(d, $"SELECT COUNT(DISTINCT dmicRowId) FROM [stagingUntyped].[{table}]"));
        Assert.Equal(2, Scalar<int>(d, $"SELECT COUNT(*) FROM [stagingUntyped].[{table}] WHERE [Gestation Length At Delivery] IS NULL AND dmicFileId = '7554'"));

        // Re-running with drop recreates the table rather than appending.
        Assert.Equal(2, new CsvImporter(o).Run().RowsWritten);
        Assert.Equal(2, Scalar<int>(d, $"SELECT COUNT(*) FROM [stagingUntyped].[{table}]"));
    }

    [Fact]
    public void StronglyTypedImportWithAwkwardNames()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "CSVTOSQL_TEST_SQL_SERVER not set");
        var path = TestFiles.WriteTemp("id,\"na]me\",when,amount\r\n1,\"a, b\",2026-01-02,1.5\r\n2,,2026-02-03,\r\n");
        try
        {
            var table = "typed ]" + Guid.NewGuid().ToString("N");
            var d = Destination(table, "odd schema");
            var o = new ImportOptions { SourceFile = path, Sniff = true, ColumnsFromHeader = true, CamelCaseColumnNames = false, TrimValues = true, Sql = d };
            Assert.Equal(2, new CsvImporter(o).Run().RowsWritten);
            var quoted = "[odd schema].[" + table.Replace("]", "]]", StringComparison.Ordinal) + "]";
            Assert.Equal(new DateTime(2026, 2, 3), Scalar<DateTime>(d, $"SELECT [when] FROM {quoted} WHERE id = 2"));
            Assert.Equal("a, b", Scalar<string>(d, $"SELECT [na]]me] FROM {quoted} WHERE id = 1"));
            Assert.Equal(1.5m, Scalar<decimal>(d, $"SELECT amount FROM {quoted} WHERE id = 1"));
            Assert.Equal(1, Scalar<int>(d, $"SELECT COUNT(*) FROM {quoted} WHERE amount IS NULL AND [na]]me] IS NULL"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
