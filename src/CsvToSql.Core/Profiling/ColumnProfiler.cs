using System.Globalization;

namespace CsvToSql.Core.Profiling;

/// <summary>
/// Infers column types and gathers statistics from rows of raw string values.
/// </summary>
public class ColumnProfiler
{
    public const NumberStyles DefaultIntegerParsingStyle =
        NumberStyles.Integer | NumberStyles.AllowParentheses | NumberStyles.AllowThousands;

    internal static readonly string[] DateFormats =
    [
        "yyyyMMdd", "yyyyMMdd HH:mm", "yyyyMMdd HH:mm:ss", "yyyyMMddTHH:mm:ss",
        "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss",
        "dd-MM-yyyy", "dd-MM-yyyy HH:mm", "dd-MM-yyyy HH:mm:ss",
        "dd/MM/yyyy", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss",
        "dd-MM-yy", "dd-MM-yy HH:mm", "dd-MM-yy HH:mm:ss",
        "dd/MM/yy", "dd/MM/yy HH:mm", "dd/MM/yy HH:mm:ss",
    ];

    // Kept for parity with the legacy tool. "HH" is not a valid TimeSpan specifier, so exact time
    // parsing never succeeds and time-only columns are profiled as strings, as they were before.
    internal static readonly string[] TimeFormats = ["HH:mm:ss", "HH:mm"];

    private static readonly int DateMinLen = DateFormats.Min(f => f.Length);
    private static readonly int DateMaxLen = DateFormats.Max(f => f.Length);
    private static readonly int TimeMinLen = TimeFormats.Min(f => f.Length);
    private static readonly int TimeMaxLen = TimeFormats.Max(f => f.Length);
    private static readonly string[] CurrencyPrefixes = ["Â£", "$", "£", "€"];

    private int _rowsChecked;

    public List<ColumnProfile> Results { get; } = new();

    public NumberStyles IntegerParsingStyle { get; set; } = DefaultIntegerParsingStyle;

    public ProfilerType ProfilerTypes { get; set; } = ProfilerType.Basic;

    public bool TreatDashAsNull { get; set; }

    public bool TreatNullAsNull { get; set; }

    public bool TreatYesNoAsBool { get; set; }

    public bool TreatPercentageAsNumeric { get; set; }

    public bool TreatCurrencyAsNumeric { get; set; }

    public bool TrimFields { get; set; }

    public bool TreatNumericFieldsWithLeadingZeroAsString { get; set; }

    public bool StripControlCharacters { get; set; } = true;

    public void ProfileStart()
    {
        _rowsChecked = 0;
    }

    public void ProfileStep(string?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _rowsChecked++;
        if (StripControlCharacters)
        {
            ControlCharacters.StripInPlace(values);
        }

        ProfileRecord(values);
    }

    public int ProfileEnd()
    {
        var i = 0;
        foreach (var md in Results)
        {
            md.BestDataType ??= typeof(string);
            md.RowsChecked = _rowsChecked;
            if (string.IsNullOrEmpty(md.Name))
            {
                md.Name = $"Column_{i}";
            }

            i++;
        }

        return _rowsChecked;
    }

    private void ProfileRecord(string?[] values)
    {
        while (Results.Count < values.Length)
        {
            Results.Add(new ColumnProfile(IntegerParsingStyle));
        }

        for (var colIndex = 0; colIndex < values.Length; colIndex++)
        {
            var meta = Results[colIndex];
            meta.BaseDataType = typeof(string);
            var val = values[colIndex];
            if (TrimFields && val is not null)
            {
                val = val.Trim();
            }

            var metaVal = val;
            var (hasVal, type, len) = InferType(val, meta.BestDataType);

            if (hasVal && ProfilerTypes != ProfilerType.None && metaVal is not null)
            {
                meta.NumPopulated++;
                meta.Values[metaVal] = meta.Values.TryGetValue(metaVal, out var count) ? count + 1 : 1;
            }

            meta.BestDataType = meta.BestDataType.GetBestType(type);
            meta.MaxLength = Math.Max(meta.MaxLength, len);
            meta.MinLength = meta.MinLength == 0 ? len : Math.Min(meta.MinLength, len);
        }
    }

    /// <summary>
    /// Infers the type of a single (already trimmed) value given the column's best type so far.
    /// </summary>
    internal (bool HasValue, Type? Type, int Length) InferType(string? val, Type? previousBest)
    {
        if (string.IsNullOrEmpty(val)
            || (TreatDashAsNull && val is "-" or "–" or "—")
            || (TreatNullAsNull && val == "null"))
        {
            return (false, null, 0);
        }

        var len = val.Length;
        if (previousBest == typeof(string))
        {
            return (true, typeof(string), len);
        }

        var parseDate = previousBest is null || previousBest == typeof(DateTime);
        var parseTime = previousBest is null || previousBest == typeof(TimeSpan);

        var isBool = bool.TryParse(val, out _);
        if (!isBool && TreatYesNoAsBool)
        {
            isBool = string.Equals(val, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(val, "no", StringComparison.OrdinalIgnoreCase)
                || string.Equals(val, "y", StringComparison.OrdinalIgnoreCase)
                || string.Equals(val, "n", StringComparison.OrdinalIgnoreCase);
        }

        var isDate = false;
        if (parseDate && len >= DateMinLen && len <= DateMaxLen)
        {
            foreach (var fmt in DateFormats)
            {
                if (val.Length == fmt.Length && DateTime.TryParseExact(val, fmt, CultureInfo.CurrentCulture, DateTimeStyles.None, out _))
                {
                    isDate = true;
                    break;
                }
            }
        }

        var isTime = false;
        if (parseTime && !isDate && len >= TimeMinLen && len <= TimeMaxLen && TimeFormats.Any(f => f.Length == val.Length))
        {
            isTime = TimeSpan.TryParseExact(val, TimeFormats, CultureInfo.CurrentCulture, out _);
        }

        bool isByte = false, isShort = false, isInt = false, isLong = false, isDecimal = false;
        var numeric = val;
        if (!isDate && !isTime)
        {
            if (TreatPercentageAsNumeric && numeric.EndsWith('%'))
            {
                numeric = numeric.TrimEnd('%');
            }

            if (TreatCurrencyAsNumeric)
            {
                var prefix = CurrencyPrefixes.FirstOrDefault(p => numeric.StartsWith(p, StringComparison.Ordinal));
                if (prefix is not null)
                {
                    if (numeric.Length > prefix.Length)
                    {
                        numeric = numeric[prefix.Length..];
                    }
                    else
                    {
                        // A bare currency symbol carries no value.
                        return (false, null, 0);
                    }
                }
            }

            isLong = long.TryParse(numeric, IntegerParsingStyle, CultureInfo.InvariantCulture, out var longVal);
            if (isLong)
            {
                isByte = longVal is >= byte.MinValue and <= byte.MaxValue;
                isShort = !isByte && longVal is >= short.MinValue and <= short.MaxValue;
                isInt = !isShort && longVal is >= int.MinValue and <= int.MaxValue;
            }
            else
            {
                isDecimal = decimal.TryParse(numeric, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out _);
            }
        }

        Type t = typeof(string);
        if (isBool)
        {
            t = typeof(bool);
        }
        else if (isDate)
        {
            t = typeof(DateTime);
        }
        else if (isTime)
        {
            t = typeof(TimeSpan);
        }
        else if (TreatNumericFieldsWithLeadingZeroAsString && numeric.StartsWith('0') && numeric != "0")
        {
            t = typeof(string);
        }
        else if (isByte)
        {
            t = typeof(byte);
        }
        else if (isShort)
        {
            t = typeof(short);
        }
        else if (isInt)
        {
            t = typeof(int);
        }
        else if (isLong)
        {
            t = typeof(long);
        }
        else if (isDecimal)
        {
            t = typeof(decimal);
        }

        return (true, t, len);
    }
}
