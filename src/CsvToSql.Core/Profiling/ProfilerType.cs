namespace CsvToSql.Core.Profiling;

[Flags]
public enum ProfilerType
{
    None = 0,
    ColumnLengthDistribution = 1,
    ColumnNullRatio = 2,
    ColumnStatistics = 4,
    ColumnValueDistribution = 8,
    Basic = ColumnLengthDistribution | ColumnNullRatio,
    All = 255,
}

public static class ProfilerTypeParser
{
    /// <summary>
    /// Parses the legacy <c>--processing.profile.options</c> letters: L (length distribution),
    /// N (null ratio) and V (value distribution).
    /// </summary>
    public static ProfilerType Parse(string? options)
    {
        var result = ProfilerType.None;
        if (string.IsNullOrEmpty(options))
        {
            return result;
        }

        if (options.Contains('L', StringComparison.OrdinalIgnoreCase))
        {
            result |= ProfilerType.ColumnLengthDistribution;
        }

        if (options.Contains('N', StringComparison.OrdinalIgnoreCase))
        {
            result |= ProfilerType.ColumnNullRatio;
        }

        if (options.Contains('V', StringComparison.OrdinalIgnoreCase))
        {
            result |= ProfilerType.ColumnValueDistribution;
        }

        return result;
    }
}
