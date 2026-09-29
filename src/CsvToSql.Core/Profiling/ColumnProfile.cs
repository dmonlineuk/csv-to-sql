using System.Globalization;

namespace CsvToSql.Core.Profiling;

/// <summary>
/// Per-column statistics gathered while sniffing/profiling a file.
/// </summary>
public sealed class ColumnProfile
{
    public ColumnProfile(NumberStyles integerParsingStyle)
    {
        IntegerParsingStyle = integerParsingStyle;
        Reset(string.Empty);
    }

    public string Name { get; set; } = string.Empty;

    public Type? BestDataType { get; set; }

    public Type? BaseDataType { get; set; }

    public int MinLength { get; set; }

    public int MaxLength { get; set; }

    public int NumPopulated { get; set; }

    public int RowsChecked { get; set; }

    public NumberStyles IntegerParsingStyle { get; }

    /// <summary>Distinct values in first-seen order, with occurrence counts.</summary>
    public Dictionary<string, int> Values { get; private set; } = new();

    public int NumDistinctValues => Values.Count;

    public IReadOnlyList<KeyValuePair<int, int>> LengthDistributions
    {
        get
        {
            var order = new List<int>();
            var d = new Dictionary<int, int>();
            if (BaseDataType == typeof(string))
            {
                foreach (var kv in Values)
                {
                    var l = kv.Key.Length;
                    if (d.TryGetValue(l, out var n))
                    {
                        d[l] = n + kv.Value;
                    }
                    else
                    {
                        d.Add(l, kv.Value);
                        order.Add(l);
                    }
                }
            }

            return order.Select(l => new KeyValuePair<int, int>(l, d[l])).ToList();
        }
    }

    public void Reset(string name)
    {
        Name = name;
        BestDataType = null;
        BaseDataType = null;
        MinLength = 0;
        MaxLength = 0;
        NumPopulated = 0;
        Values = new Dictionary<string, int>();
    }

    /// <summary>
    /// Formats a distinct value the way the legacy profiler did for value distributions.
    /// </summary>
    public string TypedValue(string value)
    {
        if (BestDataType == typeof(bool))
        {
            if (value is "yes" or "1" or "true" or "y")
            {
                return "True";
            }

            if (value is "no" or "0" or "false" or "n")
            {
                return "False";
            }
        }
        else if (BestDataType == typeof(long) || BestDataType == typeof(int) || BestDataType == typeof(short))
        {
            if (long.TryParse(value, IntegerParsingStyle, CultureInfo.InvariantCulture, out var l))
            {
                return l.ToString(CultureInfo.InvariantCulture);
            }
        }

        return value;
    }
}
