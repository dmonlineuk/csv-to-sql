using System.Net;
using CsvToSql.Core.Import;
using Microsoft.Data.SqlClient;

namespace CsvToSql.Core.Sql;

public static class SqlConnectionFactory
{
    /// <summary>
    /// Connection string without credentials. SQL authentication is supplied separately via
    /// <see cref="SqlCredential"/> so the password never appears in the connection string.
    /// </summary>
    public static string BuildConnectionString(SqlDestinationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var cb = new SqlConnectionStringBuilder
        {
            DataSource = options.Server,
            IntegratedSecurity = string.IsNullOrWhiteSpace(options.Username),
            TrustServerCertificate = options.TrustServerCertificate,
            ConnectTimeout = options.ConnectTimeoutSeconds,
            ApplicationName = "CsvToDb",
        };
        if (!string.IsNullOrEmpty(options.Database))
        {
            cb.InitialCatalog = options.Database;
        }

        return cb.ConnectionString;
    }

    public static SqlCredential? BuildCredential(SqlDestinationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Username))
        {
            return null;
        }

        var pwd = new NetworkCredential(options.Username, options.Password ?? string.Empty).SecurePassword;
        pwd.MakeReadOnly();
        return new SqlCredential(options.Username, pwd);
    }

    public static SqlConnection Create(SqlDestinationOptions options) =>
        new(BuildConnectionString(options), BuildCredential(options));
}
