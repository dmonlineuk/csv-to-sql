namespace CsvToSql.Core.Profiling;

/// <summary>
/// Removes C0 control characters (U+0000 to U+001F), matching the legacy <c>[\u0000-\u001F]</c> filter.
/// </summary>
public static class ControlCharacters
{
    public static string Strip(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var i = 0;
        while (i < value.Length && value[i] >= ' ')
        {
            i++;
        }

        if (i == value.Length)
        {
            return value;
        }

        return string.Create(value.Length - Count(value), value, static (span, src) =>
        {
            var j = 0;
            foreach (var c in src)
            {
                if (c >= ' ')
                {
                    span[j++] = c;
                }
            }
        });
    }

    public static void StripInPlace(string?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] is { } v)
            {
                values[i] = Strip(v);
            }
        }
    }

    private static int Count(string value)
    {
        var n = 0;
        foreach (var c in value)
        {
            if (c < ' ')
            {
                n++;
            }
        }

        return n;
    }
}
