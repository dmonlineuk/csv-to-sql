using CsvToSql.Core.Logging;
using CsvToSql.Core.Sql;

namespace CsvToSql.Tests;

public class SqlRetryTests
{
    private static readonly SqlRetryPolicy Policy = new(3, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(25));

    [Fact]
    public void DelayDoublesUpToTheCap()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), Policy.DelayBefore(1));
        Assert.Equal(TimeSpan.FromSeconds(20), Policy.DelayBefore(2));
        Assert.Equal(TimeSpan.FromSeconds(25), Policy.DelayBefore(3));
        Assert.Equal(TimeSpan.FromSeconds(25), Policy.DelayBefore(60));
    }

    [Theory]
    [InlineData(-2, 11, true)] // command/connect timeout
    [InlineData(1205, 13, true)] // deadlock victim
    [InlineData(40501, 20, true)] // Azure SQL: service busy
    [InlineData(10928, 16, true)] // Azure SQL: resource (worker/session) limit reached
    [InlineData(0, 20, true)] // transport-level error, e.g. connection reset
    [InlineData(18456, 14, false)] // login failed
    [InlineData(208, 16, false)] // invalid object name
    [InlineData(2627, 14, false)] // primary key violation
    public void ClassifiesSqlErrors(int number, byte severity, bool transient) =>
        Assert.Equal(transient, SqlRetry.IsTransient([(number, severity)]));

    [Fact]
    public void CertificateFailureIsNotTransient() =>
        Assert.False(SqlRetry.IsTransient([(0, 20)], certificateError: true));

    [Fact]
    public void RetriesTransientFailuresAndLogsEachRetry()
    {
        var console = new StringWriter();
        using var log = new ImportLog(console: console);
        var waits = new List<TimeSpan>();
        var result = SqlRetry.Run(
            attempt => attempt < 2 ? throw new TimeoutException("busy") : attempt,
            SqlRetry.IsTransient,
            Policy,
            log,
            "Import",
            waits.Add);

        Assert.Equal(2, result);
        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)], waits);
        Assert.Contains("Import failed with a transient error, retrying in 10s (1/3): busy", console.ToString(), StringComparison.Ordinal);
        Assert.Contains("(2/3)", console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GivesUpAfterMaxRetries()
    {
        var attempts = 0;
        var ex = Assert.Throws<TimeoutException>(() => SqlRetry.Run<int>(
            _ => { attempts++; throw new TimeoutException("busy"); }, SqlRetry.IsTransient, Policy, ImportLog.Null, "Import", _ => { }));
        Assert.Equal("busy", ex.Message);
        Assert.Equal(4, attempts);
    }

    [Fact]
    public void DoesNotRetryOtherErrors()
    {
        var attempts = 0;
        Assert.Throws<InvalidOperationException>(() => SqlRetry.Run<int>(
            _ => { attempts++; throw new InvalidOperationException("bad data"); }, SqlRetry.IsTransient, Policy, ImportLog.Null, "Import", _ => { }));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void ConcurrentLogsSharingAFileKeepEveryLine()
    {
        var path = Path.Combine(Path.GetTempPath(), "csvtosql-log-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            Parallel.For(0, 4, i =>
            {
                using var log = new ImportLog(path, console: TextWriter.Null);
                for (var n = 0; n < 500; n++)
                {
                    log.Info($"writer {i} line {n}");
                }
            });

            var lines = File.ReadAllLines(path);
            Assert.Equal(2000, lines.Length);
            Assert.All(lines, l => Assert.Matches(@"^\S+ \S+\|I\|CsvToDb\|writer \d line \d+$", l));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
