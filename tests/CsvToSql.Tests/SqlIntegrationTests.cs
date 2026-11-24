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
            // Square brackets are stripped from column names, as in the legacy tool.
            var quoted = "[odd schema].[" + table.Replace("]", "]]", StringComparison.Ordinal) + "]";
            Assert.Equal(new DateTime(2026, 2, 3), Scalar<DateTime>(d, $"SELECT [when] FROM {quoted} WHERE id = 2"));
            Assert.Equal("a, b", Scalar<string>(d, $"SELECT [name] FROM {quoted} WHERE id = 1"));
            Assert.Equal(1.5m, Scalar<decimal>(d, $"SELECT amount FROM {quoted} WHERE id = 1"));
            Assert.Equal(1, Scalar<int>(d, $"SELECT COUNT(*) FROM {quoted} WHERE amount IS NULL AND [name] IS NULL"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConcurrentRunsCreatingTheSameSchemaAllSucceed()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "CSVTOSQL_TEST_SQL_SERVER not set");
        var schema = "concurrent_" + Guid.NewGuid().ToString("N");
        var results = new long[8];
        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = results.Length }, i =>
        {
            var o = ImporterTests.ProductionLikeOptions(TestFiles.Fixture);
            o.DestNone = false;
            o.Sql = Destination("t" + i, schema);
            o.Sql.RetryCount = 0;
            results[i] = new CsvImporter(o).Run().RowsWritten;
        });

        Assert.All(results, r => Assert.Equal(2, r));
    }

    [Fact]
    public async Task BulkCopyKilledMidwayIsRetriedFromTheStart()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "CSVTOSQL_TEST_SQL_SERVER not set");
        Assert.SkipWhen(
            Scalar<int>(Destination("x"), "SELECT HAS_PERMS_BY_NAME(NULL, NULL, 'ALTER ANY CONNECTION') & HAS_PERMS_BY_NAME(NULL, NULL, 'VIEW SERVER STATE')") == 0,
            "the test login cannot kill sessions; see scripts/start-test-sql.sh");
        const int rows = 300_000;
        var path = Path.Combine(Path.GetTempPath(), "csvtosql-" + Guid.NewGuid().ToString("N") + ".csv");
        using (var w = new StreamWriter(path))
        {
            w.WriteLine("id,name,payload");
            for (var i = 0; i < rows; i++)
            {
                w.WriteLine($"{i},name {i},{new string('x', 200)}");
            }
        }

        try
        {
            var table = "killed_" + Guid.NewGuid().ToString("N");
            var d = Destination(table);
            d.RetryWaitSeconds = 1;
            var console = new StringWriter();
            using var log = new CsvToSql.Core.Logging.ImportLog(console: TextWriter.Synchronized(console));
            var o = new ImportOptions { SourceFile = path, ColumnsFromHeader = true, Sql = d };

            var killed = false;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(2));
            var killer = Task.Run(() =>
            {
                var admin = Destination(table);
                while (!cts.IsCancellationRequested)
                {
                    var spid = Scalar<object>(admin,
                        "SELECT ISNULL((SELECT TOP 1 r.session_id FROM sys.dm_exec_requests r JOIN sys.dm_exec_sessions s ON s.session_id = r.session_id "
                        + "WHERE s.program_name = 'CsvToDb' AND r.command = 'BULK INSERT' AND r.session_id <> @@SPID), 0)");
                    if (Convert.ToInt32(spid, System.Globalization.CultureInfo.InvariantCulture) is var id and > 0)
                    {
                        using var conn = CsvToSql.Core.Sql.SqlConnectionFactory.Create(admin);
                        conn.Open();
                        using var cmd = new SqlCommand($"KILL {id}", conn);
                        cmd.ExecuteNonQuery();
                        killed = true;
                        return;
                    }

                    Thread.Sleep(20);
                }
            }, cts.Token);

            var result = new CsvImporter(o, log).Run();
            await killer;

            Assert.True(killed, "the bulk copy session was never seen, so nothing was killed");
            Assert.Equal(rows, result.RowsWritten);
            Assert.Equal(rows, Scalar<int>(d, $"SELECT COUNT(*) FROM [stagingUntyped].[{table}]"));
            Assert.Contains("failed with a transient error, retrying", console.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
