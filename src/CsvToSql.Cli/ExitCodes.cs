using CsvToSql.Core.Sql;
using Microsoft.Data.SqlClient;

namespace CsvToSql.Cli;

/// <summary>Process exit codes, one per kind of failure, so a scheduler can tell failures apart.</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int Unexpected = 1;
    public const int InvalidCommandLine = 2;
    public const int FileError = 3;
    public const int DataError = 4;
    public const int SqlAccessDenied = 5;
    public const int SqlUnavailable = 6;
    public const int SqlError = 7;
    public const int OutOfMemory = 8;

    // Login failed, password expired/locked, cannot open database, permission denied.
    private static readonly HashSet<int> AccessDeniedNumbers = [229, 230, 262, 297, 300, 916, 4060, 18452, 18456, 18486, 18487, 18488];

    public static string Describe(int code) => code switch
    {
        Success => "success",
        Unexpected => "unexpected error",
        InvalidCommandLine => "invalid command line",
        FileError => "file error (source, log or profile file can't be read or written)",
        DataError => "data error (CSV header, row or value)",
        SqlAccessDenied => "SQL login, certificate or permission failure",
        SqlUnavailable => "SQL Server busy or unreachable (transient error, retries used up or not safe)",
        SqlError => "SQL error",
        OutOfMemory => "out of memory",
        _ => "unknown",
    };

    public static int For(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        for (var e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case SqlException sql:
                    return ForSql(sql.Errors.Cast<SqlError>().Select(x => (x.Number, x.Class)), SqlRetry.IsCertificateError(sql));
                case SqlDestinationException:
                    return SqlError;
                case OutOfMemoryException:
                    return OutOfMemory;
                case InvalidDataException or FormatException or OverflowException or InvalidCastException:
                    return DataError;
                case IOException or UnauthorizedAccessException:
                    return FileError;
                case TimeoutException:
                    return SqlUnavailable;
            }
        }

        return Unexpected;
    }

    public static int ForSql(IEnumerable<(int Number, byte Class)> errors, bool certificateError)
    {
        var list = errors.ToList();
        if (certificateError || list.Exists(e => AccessDeniedNumbers.Contains(e.Number)))
        {
            return SqlAccessDenied;
        }

        return SqlRetry.IsTransient(list) ? SqlUnavailable : SqlError;
    }
}
