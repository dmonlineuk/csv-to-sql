using System.Globalization;
using System.Text.RegularExpressions;

namespace CsvToSql.Core.Filters;

public sealed class TrimFilter : IValueFilter
{
    private static readonly char[] TrimChars = [' ', '\t', '\r', '\n', '\uFFEE'];

    public string Name => "Trim";

    public string? Filter(string? value, IRowContext row) => Apply(value);

    public static string? Apply(string? value) => value?.Trim(TrimChars);
}

public sealed class EmptyStringFilter : IValueFilter
{
    public string Name => "Empty";

    public string? Filter(string? value, IRowContext row) => string.IsNullOrWhiteSpace(value) ? null : value;
}

public sealed class NaStringFilter : IValueFilter
{
    public string Name => "NA";

    public string? Filter(string? value, IRowContext row) =>
        value is "n/a" or "N/A" or "N/a" or "n/A" ? null : value;
}

public sealed class NullStringFilter : IValueFilter
{
    public string Name => "NULL";

    public string? Filter(string? value, IRowContext row) => value == "NULL" ? null : value;
}

public sealed class NumericDashFilter : IValueFilter
{
    public string Name => "NumericDash";

    public string? Filter(string? value, IRowContext row) => value == "-" ? null : value;
}

public sealed partial class CurrencySymbolFilter : IValueFilter
{
    public string Name => "CurrencySymbol";

    public string? Filter(string? value, IRowContext row)
    {
        if (value is null)
        {
            return null;
        }

        var m = CurrencyRegex().Match(value);
        return m.Success ? value[m.Length..] : value;
    }

    [GeneratedRegex("^([$£€]|Â£)")]
    private static partial Regex CurrencyRegex();
}

public sealed class NumericCommaFilter : IValueFilter
{
    public string Name => "NumericComma";

    public string? Filter(string? value, IRowContext row) => value?.Replace(",", string.Empty, StringComparison.Ordinal);
}

public sealed partial class PercentageSymbolFilter : IValueFilter
{
    public string Name => "PercentageSymbol";

    public string? Filter(string? value, IRowContext row) =>
        value is not null && PercentageRegex().IsMatch(value) ? value.TrimEnd('%') : value;

    [GeneratedRegex("^-?[0-9,.]+%$")]
    private static partial Regex PercentageRegex();
}

public sealed class YesNoFilter : IValueFilter
{
    public string Name => "YesNo";

    public string? Filter(string? value, IRowContext row)
    {
        if (value is null)
        {
            return null;
        }

        if (string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) || value is "Y" or "y")
        {
            return "true";
        }

        if (string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) || value is "N" or "n")
        {
            return "false";
        }

        return value;
    }
}

public sealed class LowerFilter : IValueFilter
{
    public string Name => "Lower";

    public string? Filter(string? value, IRowContext row) => value?.ToLowerInvariant();
}

public sealed class UpperFilter : IValueFilter
{
    public string Name => "Upper";

    public string? Filter(string? value, IRowContext row) => value?.ToUpperInvariant();
}

public sealed class LeftFilter : IValueFilter
{
    public int Length { get; init; }

    public string Name => "Left";

    public string? Filter(string? value, IRowContext row) =>
        value is null ? null : value[..Math.Clamp(Length, 0, value.Length)];
}

public sealed class RightFilter : IValueFilter
{
    public int Length { get; init; }

    public string Name => "Right";

    public string? Filter(string? value, IRowContext row)
    {
        if (value is null || value.Length <= Length)
        {
            return value;
        }

        return value[(value.Length - Math.Max(Length, 0))..];
    }
}

public sealed class SplitFilter : IValueFilter
{
    public char Separator { get; init; }

    public int Index { get; init; }

    public string Name => "Split";

    public string? Filter(string? value, IRowContext row)
    {
        if (value is null)
        {
            return null;
        }

        var parts = value.Split(Separator);
        return Index >= 0 && Index < parts.Length ? parts[Index] : null;
    }
}

public sealed class ReplaceFilter : IValueFilter
{
    private readonly Dictionary<string, string> _mapping;

    public ReplaceFilter(string? mappings, bool caseSensitive)
    {
        _mapping = new Dictionary<string, string>(caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        foreach (var pair in (mappings ?? string.Empty).Split(';'))
        {
            var kv = pair.Split(':');
            if (kv.Length == 2)
            {
                _mapping.TryAdd(kv[0], kv[1]);
            }
        }
    }

    public string Name => "Replace";

    public string? Filter(string? value, IRowContext row) =>
        value is not null && _mapping.TryGetValue(value, out var mapped) ? mapped : value;
}

internal static class FilterParameterParsing
{
    public static int ParseInt(string name, string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i
            : throw new ArgumentException($"Filter parameter '{name}' must be an integer, got '{value}'.");

    public static bool ParseBool(string name, string value) =>
        bool.TryParse(value, out var b)
            ? b
            : throw new ArgumentException($"Filter parameter '{name}' must be true or false, got '{value}'.");

    public static char ParseChar(string name, string value) =>
        value.Length == 1
            ? value[0]
            : throw new ArgumentException($"Filter parameter '{name}' must be a single character, got '{value}'.");
}
