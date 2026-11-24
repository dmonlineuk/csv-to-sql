namespace CsvToSql.Core.Sql;

/// <summary>The destination table or one of its columns is missing.</summary>
public sealed class SqlDestinationException : InvalidOperationException
{
    public SqlDestinationException()
    {
    }

    public SqlDestinationException(string message)
        : base(message)
    {
    }

    public SqlDestinationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
