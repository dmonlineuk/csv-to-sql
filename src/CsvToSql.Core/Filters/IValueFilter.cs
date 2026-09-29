namespace CsvToSql.Core.Filters;

/// <summary>
/// Values available to filters while a row is being processed.
/// </summary>
public interface IRowContext
{
    /// <summary>1-based physical record number of the current row in the file.</summary>
    long LineNumber { get; }

    /// <summary>Output field names (as exposed to the destination) in ordinal order.</summary>
    IReadOnlyList<string> FieldNames { get; }

    /// <summary>Raw (pre-filter) value of the output field at <paramref name="ordinal"/>; null for derived columns.</summary>
    string? GetRawValue(int ordinal);

    /// <summary>User supplied variables such as <c>$importLogId</c>.</summary>
    IReadOnlyDictionary<string, string> Variables { get; }
}

/// <summary>
/// Transforms a single column value. Filters are applied in order; returning null yields a SQL NULL.
/// </summary>
public interface IValueFilter
{
    /// <summary>Legacy display name, e.g. <c>Trim</c> or <c>Derivation</c>.</summary>
    string Name { get; }

    string? Filter(string? value, IRowContext row);
}
