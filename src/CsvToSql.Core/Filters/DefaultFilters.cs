namespace CsvToSql.Core.Filters;

/// <summary>
/// The filters the legacy tool attached to each column based on its inferred type.
/// </summary>
public static class DefaultFilters
{
    public static IReadOnlyList<IValueFilter> ForType(Type? type)
    {
        List<IValueFilter> basic = [new NullStringFilter(), new NumericDashFilter()];
        if (type == typeof(bool))
        {
            basic.AddRange([new TrimFilter(), new YesNoFilter()]);
        }
        else if (type == typeof(long) || type == typeof(decimal) || type == typeof(float) || type == typeof(double)
                 || type == typeof(int) || type == typeof(short) || type == typeof(byte))
        {
            basic.AddRange([new TrimFilter(), new CurrencySymbolFilter(), new NumericCommaFilter(), new PercentageSymbolFilter()]);
        }
        else if (type == typeof(DateTime) || type == typeof(TimeSpan))
        {
            basic.Add(new TrimFilter());
        }
        else if (type != typeof(string))
        {
            basic.Clear();
        }

        return basic;
    }

    /// <summary>Filters prepended to every sniffed column when <c>--processing.trim</c> is set.</summary>
    public static IReadOnlyList<IValueFilter> TrimFilters { get; } =
        [new TrimFilter(), new EmptyStringFilter(), new NaStringFilter()];
}
