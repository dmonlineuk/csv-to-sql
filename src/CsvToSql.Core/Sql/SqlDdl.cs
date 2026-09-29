using System.Globalization;
using System.Text;
using CsvToSql.Core.Import;

namespace CsvToSql.Core.Sql;

/// <summary>
/// Builds the schema/table DDL. Object names are always passed as parameters or quoted identifiers.
/// </summary>
public static class SqlDdl
{
    public const string CreateSchemaSql = "IF SCHEMA_ID(@schema) IS NULL EXEC(@sql);";

    public const string DropTableSql =
        "IF OBJECT_ID(@object, 'U') IS NOT NULL EXEC(N'DROP TABLE ' + @quoted);";

    public const string TruncateTableSql =
        "IF OBJECT_ID(@object, 'U') IS NOT NULL EXEC(N'TRUNCATE TABLE ' + @quoted);";

    public static string CreateSchemaStatement(string schema) => "CREATE SCHEMA " + SqlIdentifier.Quote(schema);

    /// <summary>
    /// <c>CREATE TABLE</c> body, executed only when the table does not exist (checked via <c>OBJECT_ID(@object)</c>).
    /// </summary>
    public static string CreateTableSql(string schema, string table, IReadOnlyList<DestinationColumn> columns, bool addSurrogateKey)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0 && !addSurrogateKey)
        {
            throw new ArgumentException("A table needs at least one column.", nameof(columns));
        }

        var sb = new StringBuilder();
        sb.Append("IF OBJECT_ID(@object) IS NULL CREATE TABLE ").Append(SqlIdentifier.QuoteTwoPart(schema, table)).Append(" (");
        var first = true;
        if (addSurrogateKey)
        {
            sb.Append("[ID] BIGINT NOT NULL IDENTITY(1,1)");
            first = false;
        }

        foreach (var c in columns)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(SqlIdentifier.Quote(c.Name)).Append(' ').Append(c.SqlType);
            if (c.NotNull)
            {
                sb.Append(" NOT NULL");
            }
        }

        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// SQL type for a field, following the legacy rules: every column is text when weakly typed
    /// (or not sniffed), otherwise the inferred type.
    /// </summary>
    public static string SqlTypeFor(FieldSpec field, bool stronglyTyped, bool unicode, int textLength, string decimalSize)
    {
        ArgumentNullException.ThrowIfNull(field);
        var text = unicode ? "nvarchar" : "varchar";
        if (!stronglyTyped)
        {
            return FormattableString.Invariant($"{text}({FormatLength(textLength)})");
        }

        var t = field.FieldType;
        string Sized(string baseType) => field.Size is > 0
            ? FormattableString.Invariant($"{baseType}({FormatLength(field.Size.Value)})")
            : FormattableString.Invariant($"{baseType}({FormatLength(textLength)})");

        if (t == typeof(string))
        {
            return Sized(field.Derived ? "nvarchar" : text);
        }

        if (t == typeof(bool))
        {
            return "bit";
        }

        if (t == typeof(DateTime))
        {
            return "datetime2";
        }

        if (t == typeof(TimeSpan))
        {
            return "time";
        }

        if (t == typeof(byte))
        {
            return "tinyint";
        }

        if (t == typeof(short))
        {
            return "smallint";
        }

        if (t == typeof(int))
        {
            return "int";
        }

        if (t == typeof(long))
        {
            return "bigint";
        }

        if (t == typeof(decimal))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(decimalSize, @"^\s*\d+\s*(,\s*\d+\s*)?$"))
            {
                throw new ArgumentException($"Invalid decimal size '{decimalSize}'.");
            }

            return "decimal(" + decimalSize.Replace(" ", string.Empty, StringComparison.Ordinal) + ")";
        }

        if (t == typeof(double))
        {
            return "float";
        }

        return Sized(text);
    }

    private static string FormatLength(int length) =>
        length is <= 0 or > 8000 ? "max" : length.ToString(CultureInfo.InvariantCulture);
}
