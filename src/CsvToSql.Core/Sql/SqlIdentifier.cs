namespace CsvToSql.Core.Sql;

public static class SqlIdentifier
{
    /// <summary>Quotes an identifier with square brackets, escaping embedded closing brackets (like QUOTENAME).</summary>
    public static string Quote(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length > 128)
        {
            throw new ArgumentException($"Identifier exceeds 128 characters: {name}");
        }

        return "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    public static string QuoteTwoPart(string schema, string table) => Quote(schema) + "." + Quote(table);
}
