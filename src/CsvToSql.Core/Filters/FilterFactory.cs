namespace CsvToSql.Core.Filters;

/// <summary>
/// A parsed <c>--processing.filters</c> entry: <c>[column/]FilterName[?Param=value&amp;...]</c>.
/// An empty <see cref="Column"/> applies the filter to every column.
/// </summary>
public sealed record FilterDefinition(string Column, string FilterName, IReadOnlyDictionary<string, string> Parameters)
{
    public static FilterDefinition Parse(string definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var query = string.Empty;
        var head = definition;
        var q = definition.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0)
        {
            head = definition[..q];
            query = definition[(q + 1)..];
        }

        var column = string.Empty;
        var name = head;
        var slash = head.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            column = head[..slash];
            name = head[(slash + 1)..];
        }

        return new FilterDefinition(column, name, ParseParameters(query));
    }

    public static Dictionary<string, string> ParseParameters(string? parameters)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return result;
        }

        foreach (var userParam in parameters.Split('&'))
        {
            var eq = userParam.IndexOf('=', StringComparison.Ordinal);
            var key = eq < 0 ? userParam : userParam[..eq];
            var val = eq < 0 ? string.Empty : userParam[(eq + 1)..];
            if (!result.TryAdd(key, val))
            {
                throw new ArgumentException($"Duplicate filter parameter '{key}'.");
            }
        }

        return result;
    }
}

public static class FilterFactory
{
    private static readonly string[] KnownButUnsupported = ["Truncate", "NonNumericDate", "DateFormat", "Substring"];

    /// <summary>Reports parameters that the named filter does not understand (the legacy tool ignored them).</summary>
    public static event Action<string>? Warning;

    public static IValueFilter Create(FilterDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var p = definition.Parameters;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? Get(string key)
        {
            used.Add(key);
            return p.TryGetValue(key, out var v) ? v : null;
        }

        IValueFilter filter = definition.FilterName.ToUpperInvariant() switch
        {
            "DERIVATION" => new DerivationFilter(Get("Format"))
            {
                OnlyIfNull = Get("OnlyIfNull") is { } oin && FilterParameterParsing.ParseBool("OnlyIfNull", oin),
                ConcatNullIfBlank = Get("ConcatNullIfBlank") is not { } cnb || FilterParameterParsing.ParseBool("ConcatNullIfBlank", cnb),
                EqualNullIfBlank = Get("EqualNullIfBlank") is not { } enb || FilterParameterParsing.ParseBool("EqualNullIfBlank", enb),
                ReplaceExactMatch = Get("ReplaceExactMatch") is { } rem && FilterParameterParsing.ParseBool("ReplaceExactMatch", rem),
            },
            "TRIM" => new TrimFilter(),
            "EMPTY" => new EmptyStringFilter(),
            "NA" => new NaStringFilter(),
            "NULL" => new NullStringFilter(),
            "NUMERICDASH" => new NumericDashFilter(),
            "NUMERICCOMMA" => new NumericCommaFilter(),
            "CURRENCYSYMBOL" => new CurrencySymbolFilter(),
            "PERCENTAGESYMBOL" => new PercentageSymbolFilter(),
            "YESNO" => new YesNoFilter(),
            "LOWER" => new LowerFilter(),
            "UPPER" => new UpperFilter(),
            "LEFT" => new LeftFilter { Length = FilterParameterParsing.ParseInt("Length", Get("Length") ?? "0") },
            "RIGHT" => new RightFilter { Length = FilterParameterParsing.ParseInt("Length", Get("Length") ?? "0") },
            "SPLIT" => new SplitFilter
            {
                Separator = FilterParameterParsing.ParseChar("Separator", Get("Separator") ?? "\0"),
                Index = FilterParameterParsing.ParseInt("Index", Get("Index") ?? "0"),
            },
            "REPLACE" => new ReplaceFilter(
                Get("Mappings"),
                Get("CaseSensitive") is { } cs && FilterParameterParsing.ParseBool("CaseSensitive", cs)),
            _ when KnownButUnsupported.Contains(definition.FilterName, StringComparer.OrdinalIgnoreCase) =>
                throw new NotSupportedException($"Filter '{definition.FilterName}' is not supported yet."),
            _ => throw new ArgumentException($"Unknown filter '{definition.FilterName}'."),
        };

        foreach (var key in p.Keys.Where(k => !used.Contains(k)))
        {
            Warning?.Invoke($"Filter '{definition.FilterName}' ignores unknown parameter '{key}'.");
        }

        return filter;
    }
}
