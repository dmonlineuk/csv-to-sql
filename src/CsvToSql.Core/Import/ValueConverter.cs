using System.Globalization;
using CsvToSql.Core.Profiling;

namespace CsvToSql.Core.Import;

/// <summary>
/// Converts filtered string values to the inferred column type for strongly typed loads.
/// </summary>
public static class ValueConverter
{
    public static object? Convert(string? value, Type type, System.Globalization.NumberStyles integerStyle)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (string.IsNullOrEmpty(value) || type == typeof(string))
        {
            return string.IsNullOrEmpty(value) && type != typeof(string) ? null : value;
        }

        if (type == typeof(bool))
        {
            if (bool.TryParse(value, out var b))
            {
                return b;
            }

            return value.ToUpperInvariant() switch
            {
                "YES" or "Y" or "1" => true,
                "NO" or "N" or "0" => false,
                _ => throw new FormatException($"'{value}' is not a boolean"),
            };
        }

        if (type == typeof(DateTime))
        {
            if (DateTime.TryParseExact(value, ColumnProfiler.DateFormats, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d)
                || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out d))
            {
                return d;
            }

            throw new FormatException($"'{value}' is not a date");
        }

        if (type == typeof(TimeSpan))
        {
            return TimeSpan.Parse(value, CultureInfo.InvariantCulture);
        }

        if (type == typeof(byte))
        {
            return byte.Parse(value, integerStyle, CultureInfo.InvariantCulture);
        }

        if (type == typeof(short))
        {
            return short.Parse(value, integerStyle, CultureInfo.InvariantCulture);
        }

        if (type == typeof(int))
        {
            return int.Parse(value, integerStyle, CultureInfo.InvariantCulture);
        }

        if (type == typeof(long))
        {
            return long.Parse(value, integerStyle, CultureInfo.InvariantCulture);
        }

        if (type == typeof(decimal))
        {
            return decimal.Parse(value, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);
        }

        if (type == typeof(double))
        {
            return double.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        }

        return value;
    }
}
