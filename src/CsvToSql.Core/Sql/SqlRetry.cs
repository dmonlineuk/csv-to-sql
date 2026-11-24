using System.Globalization;
using System.Security.Authentication;
using CsvToSql.Core.Logging;
using Microsoft.Data.SqlClient;

namespace CsvToSql.Core.Sql;

/// <summary>How often and how long to wait before retrying SQL work that failed with a transient error.</summary>
public sealed record SqlRetryPolicy(int MaxRetries, TimeSpan InitialDelay, TimeSpan MaxDelay)
{
    public static SqlRetryPolicy None { get; } = new(0, TimeSpan.Zero, TimeSpan.Zero);

    /// <summary>Delay before retry number <paramref name="retry"/> (1-based): doubles each time, capped at <see cref="MaxDelay"/>.</summary>
    public TimeSpan DelayBefore(int retry)
    {
        var ticks = InitialDelay.Ticks * Math.Pow(2, Math.Max(0, retry - 1));
        return ticks >= MaxDelay.Ticks ? MaxDelay : TimeSpan.FromTicks((long)ticks);
    }
}

/// <summary>
/// Retries SQL work that fails because the server or client is busy (timeouts, deadlocks, Azure SQL throttling,
/// dropped connections), waiting between attempts, as the legacy CsvToDb did. Other errors are rethrown at once.
/// </summary>
public static class SqlRetry
{
    // Timeouts, deadlocks/lock limits, connection drops, low memory and Azure SQL throttling, failover and
    // serverless resume (https://learn.microsoft.com/azure/azure-sql/database/troubleshoot-common-errors-issues).
    private static readonly HashSet<int> TransientNumbers =
    [
        -2, 20, 64, 121, 233, 701, 997, 1204, 1205, 1222, 4060, 4221, 8645, 8651, 10053, 10054, 10060, 10928, 10929,
        40143, 40197, 40501, 40540, 40613, 42108, 42109, 49918, 49919, 49920,
    ];

    public static bool IsTransient(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return ex switch
        {
            SqlException sql => IsTransient(sql.Errors.Cast<SqlError>().Select(e => (e.Number, e.Class)), IsCertificateError(sql)),
            TimeoutException => true,
            _ => false,
        };
    }

    /// <summary>
    /// True if any error is a known transient number, or a connection-fatal error (severity 20+) that isn't a
    /// TLS certificate failure.
    /// </summary>
    public static bool IsTransient(IEnumerable<(int Number, byte Class)> errors, bool certificateError = false)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Any(e => TransientNumbers.Contains(e.Number) || (e.Class >= 20 && !certificateError));
    }

    /// <summary>
    /// Runs <paramref name="attempt"/>, retrying while <paramref name="canRetry"/> accepts the exception and
    /// retries remain. Each retry is logged as a warning.
    /// </summary>
    public static T Run<T>(
        Func<int, T> attempt,
        Func<Exception, bool> canRetry,
        SqlRetryPolicy policy,
        ImportLog log,
        string description,
        Action<TimeSpan>? sleep = null)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(canRetry);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(log);
        sleep ??= Thread.Sleep;
        for (var retry = 0; ; retry++)
        {
            try
            {
                return attempt(retry);
            }
            catch (Exception ex) when (retry < policy.MaxRetries && canRetry(ex))
            {
                var delay = policy.DelayBefore(retry + 1);
                log.Warn(string.Create(CultureInfo.InvariantCulture,
                    $"{description} failed with a transient error, retrying in {delay.TotalSeconds:0.#}s ({retry + 1}/{policy.MaxRetries}): {Describe(ex)}"));
                sleep(delay);
            }
        }
    }

    private static string Describe(Exception ex) => ex is SqlException sql
        ? string.Create(CultureInfo.InvariantCulture, $"SQL error {sql.Number}: {sql.Message}")
        : ex.Message;

    /// <summary>True when the connection failed because the server's TLS certificate was rejected.</summary>
    public static bool IsCertificateError(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
        {
            if (e is AuthenticationException)
            {
                return true;
            }
        }

        return false;
    }
}
