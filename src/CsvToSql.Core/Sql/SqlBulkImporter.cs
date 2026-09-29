using System.Data;
using CsvToSql.Core.Import;
using CsvToSql.Core.Logging;
using Microsoft.Data.SqlClient;

namespace CsvToSql.Core.Sql;

/// <summary>
/// Prepares the destination table (schema, drop, create, truncate) and streams rows into it with <see cref="SqlBulkCopy"/>.
/// </summary>
public sealed class SqlBulkImporter(SqlDestinationOptions options, ImportLog log)
{
    public long Import(IDataReader reader, string table, IReadOnlyList<DestinationColumn> columns, bool addSurrogateKey)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(columns);
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema;
        var objectName = SqlIdentifier.QuoteTwoPart(schema, table);

        using var conn = SqlConnectionFactory.Create(options);
        conn.Open();

        if (options.CreateSchema)
        {
            Execute(conn, SqlDdl.CreateSchemaSql, ("@schema", schema), ("@sql", SqlDdl.CreateSchemaStatement(schema)));
        }

        if (options.DropTable)
        {
            Execute(conn, SqlDdl.DropTableSql, ("@object", objectName), ("@quoted", objectName));
        }

        if (options.CreateTable)
        {
            Execute(conn, SqlDdl.CreateTableSql(schema, table, columns, addSurrogateKey), ("@object", objectName));
        }

        if (options.TruncateTable)
        {
            Execute(conn, SqlDdl.TruncateTableSql, ("@object", objectName), ("@quoted", objectName));
        }

        var bulkOptions = SqlBulkCopyOptions.KeepNulls;
        if (options.TableLock)
        {
            bulkOptions |= SqlBulkCopyOptions.TableLock;
        }

        using var bc = new SqlBulkCopy(conn, bulkOptions, null)
        {
            DestinationTableName = objectName,
            BulkCopyTimeout = 0,
            EnableStreaming = true,
        };
        if (options.BatchSize > 0)
        {
            bc.BatchSize = options.BatchSize;
        }

        var existing = GetColumnNames(conn, objectName);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var source = reader.GetName(i);
            var dest = existing.FirstOrDefault(c => string.Equals(c, source, StringComparison.Ordinal))
                ?? existing.FirstOrDefault(c => string.Equals(c, source, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Destination table {objectName} has no column '{source}'.");
            bc.ColumnMappings.Add(i, dest);
        }

        log.Info($"Bulk copy into {objectName} started");
        bc.WriteToServer(reader);
        log.Info($"Bulk copy into {objectName} finished: {bc.RowsCopied64} rows");
        return bc.RowsCopied64;
    }

    private static List<string> GetColumnNames(SqlConnection conn, string objectName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@object) ORDER BY column_id";
        cmd.Parameters.Add(new SqlParameter("@object", SqlDbType.NVarChar, 520) { Value = objectName });
        using var r = cmd.ExecuteReader();
        var names = new List<string>();
        while (r.Read())
        {
            names.Add(r.GetString(0));
        }

        if (names.Count == 0)
        {
            throw new InvalidOperationException($"Destination table {objectName} does not exist.");
        }

        return names;
    }

    private void Execute(SqlConnection conn, string sql, params (string Name, string Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 0;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, -1) { Value = value });
        }

        log.Debug(sql);
        cmd.ExecuteNonQuery();
    }
}
