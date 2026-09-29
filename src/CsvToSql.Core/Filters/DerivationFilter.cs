using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CsvToSql.Core.Filters;

/// <summary>
/// Port of the legacy <c>Derivation</c> filter. <see cref="Format"/> is a template containing literals,
/// variables (<c>$name</c>, <c>$0</c>) and functions (<c>$$guid</c>, <c>$$timestamp</c>,
/// <c>$$left(col,3)</c>, ...). A format without any <c>$</c> is a constant.
/// </summary>
public sealed partial class DerivationFilter : IValueFilter
{
    private static readonly string[] UnsupportedFunctions =
        ["$$lookup", "$$datediff", "$$hash", "$$hashinc", "$$hashex", "$$hashexder", "$$hashguid"];

    private static readonly string[] SimpleDateFormats = ["yyyyMMdd", "yyyy-MM-dd", "dd-MM-yyyy", "ddMMyyyy"];

    private readonly List<FormatToken> _tokens;
    private readonly Dictionary<string, Regex> _regexCache = new(StringComparer.Ordinal);

    public DerivationFilter(string? format)
    {
        Format = format ?? string.Empty;
        _tokens = FunctionRegex().Matches(Format).Select(m => new FormatToken(
            m.Index,
            m.Length,
            m.Groups[2].Captures.Count > 0 ? "$" + m.Groups[1].Value : m.Groups[0].Value,
            m.Groups[4].Captures.Select(c => c.Value).ToArray())).ToList();

        foreach (var t in _tokens)
        {
            if (UnsupportedFunctions.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"Derivation function '{t.Name}' is not supported yet.");
            }
        }
    }

    public string Name => "Derivation";

    public string Format { get; }

    public bool OnlyIfNull { get; init; }

    public bool ConcatNullIfBlank { get; init; } = true;

    public bool EqualNullIfBlank { get; init; } = true;

    public bool ReplaceExactMatch { get; init; }

    /// <summary>Function names used by the format, e.g. <c>$$guid</c>.</summary>
    public IEnumerable<string> Functions => _tokens.Select(t => t.Name);

    public string? Filter(string? value, IRowContext row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (OnlyIfNull && !string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (_tokens.Count == 0)
        {
            return string.IsNullOrEmpty(Format) ? value : Format;
        }

        var sb = new StringBuilder();
        var offset = 0;
        foreach (var m in _tokens)
        {
            var hasPrefix = m.Index - offset > 0;
            if (hasPrefix)
            {
                sb.Append(Format, offset, m.Index - offset);
            }

            offset = m.Index + m.Length;
            var shortCircuit = _tokens.Count == 1 && offset == Format.Length && !hasPrefix;
            var result = Evaluate(m, value, row);
            if (shortCircuit)
            {
                return result;
            }

            sb.Append(result);
        }

        if (offset < Format.Length)
        {
            sb.Append(Format, offset, Format.Length - offset);
        }

        return sb.ToString();
    }

    private string? Evaluate(FormatToken m, string? value, IRowContext row)
    {
        var p = m.Parameters;
        switch (m.Name)
        {
            case "$$current":
                return value;
            case "$$linenumber":
                return row.LineNumber.ToString(CultureInfo.InvariantCulture);
            case "$$guid":
                return Guid.NewGuid().ToString();
            case "$$sequentialguid":
                return Guid.CreateVersion7().ToString();
            case "$$timestamp":
                return DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            case "$$replace":
            case "$$replaceic":
                {
                    Require(m, p.Length >= 2);
                    var comparison = m.Name == "$$replaceic" ? StringComparison.CurrentCultureIgnoreCase : StringComparison.CurrentCulture;
                    var newVal = Resolve(p[0], row);
                    for (var j = 1; newVal is not null && j < p.Length; j += 2)
                    {
                        var search = p[j];
                        var replacement = j + 1 < p.Length ? p[j + 1] : string.Empty;
                        if (!ReplaceExactMatch || string.Equals(search, replacement, comparison))
                        {
                            newVal = newVal.Replace(search, replacement, comparison);
                        }
                    }

                    return newVal;
                }

            case "$$regex":
                {
                    Require(m, p.Length >= 2);
                    var newVal = Resolve(p[0], row);
                    for (var j = 1; newVal is not null && j < p.Length; j += 2)
                    {
                        var replacement = j + 1 < p.Length ? p[j + 1] : string.Empty;
                        if (!_regexCache.TryGetValue(p[j], out var regex))
                        {
                            var pattern = Resolve(p[j], row, checkFieldNames: false) ?? string.Empty;
                            regex = new Regex(pattern, RegexOptions.IgnoreCase);
                            _regexCache.Add(p[j], regex);
                        }

                        newVal = regex.Replace(newVal, replacement);
                    }

                    return newVal;
                }

            case "$$concat":
                {
                    Require(m, p.Length >= 1);
                    var sb = new StringBuilder();
                    foreach (var f in p)
                    {
                        var ordinal = f.StartsWith('$') ? -1 : IndexOfField(row, f);
                        if (!f.StartsWith('$') && ordinal < 0)
                        {
                            sb.Append(f);
                            continue;
                        }

                        var v = Resolve(f, row);
                        if (ConcatNullIfBlank && string.IsNullOrEmpty(v))
                        {
                            return null;
                        }

                        sb.Append(v);
                    }

                    return sb.ToString();
                }

            case "$$left":
                {
                    Require(m, p.Length == 2);
                    var s = Resolve(p[0], row) ?? string.Empty;
                    var len = ParseInt(Resolve(p[1], row));
                    return s.Length <= len ? s : s[..len];
                }

            case "$$right":
                {
                    Require(m, p.Length == 2);
                    var s = Resolve(p[0], row) ?? string.Empty;
                    var len = Math.Min(ParseInt(Resolve(p[1], row)), s.Length);
                    return s[(s.Length - len)..];
                }

            case "$$substring":
                {
                    Require(m, p.Length == 3);
                    var s = Resolve(p[0], row) ?? string.Empty;
                    var start = ParseInt(Resolve(p[1], row));
                    var len = Math.Max(0, Math.Min(ParseInt(Resolve(p[2], row)), s.Length - start));
                    return s.Substring(start, len);
                }

            case "$$equal":
            case "$$notequal":
                {
                    Require(m, p.Length is 2 or 3);
                    var a = Resolve(p[0], row);
                    var b = Resolve(p[1], row);
                    var ignoreCase = p.Length != 3 || p[2] == "1";
                    var equal = string.Compare(a, b, ignoreCase, CultureInfo.CurrentCulture) == 0;
                    if (m.Name == "$$notequal")
                    {
                        equal = !equal;
                    }

                    return EqualNullIfBlank && (a == string.Empty || b == string.Empty) ? string.Empty : equal ? "1" : "0";
                }

            case "$$year":
            case "$$month":
            case "$$day":
            case "$$eomonth":
                {
                    Require(m, p.Length == 1);
                    var d = Resolve(p[0], row);
                    if (string.IsNullOrWhiteSpace(d))
                    {
                        return string.Empty;
                    }

                    if (!DateTime.TryParseExact(d, SimpleDateFormats, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date))
                    {
                        throw new FormatException("Unable to parse date format, needs to be yyyyMMdd or yyyy-MM-dd or dd-MM-yyyy or ddMMyyyy");
                    }

                    return m.Name switch
                    {
                        "$$year" => date.Year.ToString(CultureInfo.InvariantCulture),
                        "$$month" => date.Month.ToString(CultureInfo.InvariantCulture),
                        "$$day" => date.Day.ToString(CultureInfo.InvariantCulture),
                        _ => new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    };
                }

            case "$$guidfromnumber":
                {
                    Require(m, p.Length == 1);
                    var v = Resolve(p[0], row);
                    if (Guid.TryParseExact(v, "D", out _) || Guid.TryParseExact(v, "B", out _) || Guid.TryParseExact(v, "P", out _))
                    {
                        return v;
                    }

                    if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    {
                        throw new FormatException($"Value {v} for $$guidfromnumber is not a number (and not already a guid either)");
                    }

                    var bytes = new byte[16];
                    BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), n);
                    if (!BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(bytes, 0, 4);
                    }

                    return new Guid(bytes).ToString();
                }

            default:
                if (row.Variables.TryGetValue(m.Name, out var variable))
                {
                    return variable;
                }

                if (TryFieldVariable(m.Name, row, out var fieldValue))
                {
                    return fieldValue;
                }

                return m.Name;
        }
    }

    private static void Require(FormatToken m, bool condition)
    {
        if (!condition)
        {
            throw new ArgumentException($"Invalid number of parameters for {m.Name}");
        }
    }

    private static int ParseInt(string? s) => int.Parse(s ?? string.Empty, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static int IndexOfField(IRowContext row, string name)
    {
        var names = row.FieldNames;
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary><c>$3</c> refers to the raw value of output field 3.</summary>
    private static bool TryFieldVariable(string name, IRowContext row, out string? value)
    {
        value = null;
        if (name.Length > 1 && name[0] == '$' && int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal)
            && ordinal < row.FieldNames.Count)
        {
            value = row.GetRawValue(ordinal);
            return true;
        }

        return false;
    }

    private static string? Resolve(string val, IRowContext row, bool checkFieldNames = true)
    {
        if (val.StartsWith('$'))
        {
            if (row.Variables.TryGetValue(val, out var v))
            {
                return v;
            }

            if (TryFieldVariable(val, row, out var fv))
            {
                return fv;
            }

            throw new KeyNotFoundException($"Unknown variable {val}");
        }

        if (checkFieldNames)
        {
            var i = IndexOfField(row, val);
            if (i >= 0)
            {
                return row.GetRawValue(i);
            }
        }

        return val;
    }

    [GeneratedRegex(@"\$(\$?[-.a-zA-Z0-9]+)(\(((([^,\\]|\\,|\\)+),?)+\))?", RegexOptions.Singleline)]
    private static partial Regex FunctionRegex();

    private sealed record FormatToken(int Index, int Length, string Name, string[] Parameters);
}
