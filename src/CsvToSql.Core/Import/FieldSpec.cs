using System.Globalization;
using System.Text;
using CsvToSql.Core.Filters;

namespace CsvToSql.Core.Import;

/// <summary>
/// An output column: either a column read from the file (<see cref="ColumnIndex"/> &gt;= 0) or a derived column.
/// </summary>
public sealed class FieldSpec
{
    public required string FieldName { get; init; }

    /// <summary>Inferred CLR type; <c>string</c> when not sniffed.</summary>
    public Type FieldType { get; init; } = typeof(string);

    /// <summary>Column size for strongly typed tables, when known.</summary>
    public int? Size { get; init; }

    /// <summary>Zero-based position in the source record, or -1 for derived columns.</summary>
    public int ColumnIndex { get; init; } = -1;

    public bool Derived => ColumnIndex < 0;

    public List<IValueFilter> Filters { get; } = new();

    public bool ReplaceSpaceWithUnderscore { get; init; }

    public bool ReplaceUnderscoreWithSpace { get; init; }

    /// <summary>Name with <c>( ) [ ] .</c> removed.</summary>
    public string CleanFieldName => ApplyReplacements(Remove(FieldName, "()[]."));

    /// <summary>Name with square brackets removed.</summary>
    public string SafeFieldName => ApplyReplacements(Remove(FieldName, "[]"));

    /// <summary>The legacy "camel" (actually Pascal) case name: lower, title case, spaces removed.</summary>
    public string CamelName
    {
        get
        {
            var name = CleanFieldName.ToLower(CultureInfo.CurrentCulture);
            name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);
            return name.Replace(" ", string.Empty, StringComparison.Ordinal);
        }
    }

    private string ApplyReplacements(string name)
    {
        if (ReplaceSpaceWithUnderscore)
        {
            name = name.Replace(' ', '_');
        }

        if (ReplaceUnderscoreWithSpace)
        {
            name = name.Replace('_', ' ');
        }

        return name;
    }

    private static string Remove(string s, string chars)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (!chars.Contains(c, StringComparison.Ordinal))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
