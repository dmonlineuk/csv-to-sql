using CsvToSql.Cli;
using CsvToSql.Core.Sql;

namespace CsvToSql.Tests;

public class ExitCodeTests
{
    [Theory]
    [InlineData(18456, 14, ExitCodes.SqlAccessDenied)] // login failed
    [InlineData(4060, 11, ExitCodes.SqlAccessDenied)] // cannot open database
    [InlineData(262, 14, ExitCodes.SqlAccessDenied)] // CREATE TABLE permission denied
    [InlineData(-2, 11, ExitCodes.SqlUnavailable)] // timeout
    [InlineData(40501, 20, ExitCodes.SqlUnavailable)] // Azure SQL: service busy
    [InlineData(208, 16, ExitCodes.SqlError)] // invalid object name
    [InlineData(2627, 14, ExitCodes.SqlError)] // primary key violation
    public void ClassifiesSqlErrors(int number, byte severity, int expected) =>
        Assert.Equal(expected, ExitCodes.ForSql([(number, severity)], certificateError: false));

    [Fact]
    public void CertificateFailureIsAccessDenied() =>
        Assert.Equal(ExitCodes.SqlAccessDenied, ExitCodes.ForSql([(0, 20)], certificateError: true));

    [Fact]
    public void ClassifiesOtherFailures()
    {
        Assert.Equal(ExitCodes.FileError, ExitCodes.For(new FileNotFoundException("x")));
        Assert.Equal(ExitCodes.FileError, ExitCodes.For(new UnauthorizedAccessException("x")));
        Assert.Equal(ExitCodes.DataError, ExitCodes.For(new InvalidDataException("x")));
        Assert.Equal(ExitCodes.DataError, ExitCodes.For(new InvalidOperationException("bulk copy", new FormatException("x"))));
        Assert.Equal(ExitCodes.SqlError, ExitCodes.For(new SqlDestinationException("x")));
        Assert.Equal(ExitCodes.OutOfMemory, ExitCodes.For(new InsufficientMemoryException()));
        Assert.Equal(ExitCodes.Unexpected, ExitCodes.For(new InvalidOperationException("x")));
    }

    [Fact]
    public void MissingSourceFileExitsWithFileError()
    {
        var stderr = new StringWriter();
        var code = CliApp.Run(["-i", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv"), "-n", "--ui.wait.maxConcurrent", "0"], new StringWriter(), stderr);
        Assert.Equal(ExitCodes.FileError, code);
        Assert.Contains("Exit code 3: file error", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BadDataExitsWithDataError()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        File.WriteAllText(file, "a,a\n1,2\n");
        try
        {
            var stderr = new StringWriter();
            Assert.Equal(ExitCodes.DataError, CliApp.Run(["-i", file, "-n", "--ui.wait.maxConcurrent", "0"], new StringWriter(), stderr));
            Assert.Contains("Exit code 4: data error", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
