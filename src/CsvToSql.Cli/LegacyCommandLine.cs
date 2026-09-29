using System.Globalization;

namespace CsvToSql.Cli;

public enum OptionKind
{
    Switch,
    Value,
    List,
}

/// <summary>A legacy CsvToDb option. <see cref="Supported"/> is false for options that parse but are not implemented yet.</summary>
public sealed record OptionDefinition(string Name, char? ShortName, OptionKind Kind, bool Supported = true, string? Help = null);

public sealed class CommandLineException(string message) : Exception(message);

/// <summary>
/// Parses the legacy CsvToDb command line (CommandLineParser style, case-insensitive): <c>--long.name value</c>,
/// <c>--long.name=value</c>, <c>-x value</c>, switches without values and space separated lists.
/// </summary>
public sealed class ParsedCommandLine
{
    private readonly Dictionary<string, List<string>> _values;

    internal ParsedCommandLine(Dictionary<string, List<string>> values) => _values = values;

    public bool Help { get; init; }

    public bool Version { get; init; }

    public IEnumerable<string> OptionNames => _values.Keys;

    public bool Has(string name) => _values.ContainsKey(name);

    public bool Switch(string name) => _values.ContainsKey(name);

    public string? Value(string name) => _values.TryGetValue(name, out var v) ? v[0] : null;

    public IReadOnlyList<string> List(string name) => _values.TryGetValue(name, out var v) ? v : [];

    public int IntValue(string name, int defaultValue)
    {
        var v = Value(name);
        if (v is null)
        {
            return defaultValue;
        }

        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i
            : throw new CommandLineException($"Option '{name}' expects an integer, got '{v}'.");
    }

    public char? CharValue(string name)
    {
        var v = Value(name);
        return v switch
        {
            null => null,
            "\\t" or "tab" or "TAB" => '\t',
            "\\0" => '\0',
            { Length: 1 } => v[0],
            _ => throw new CommandLineException($"Option '{name}' expects a single character, got '{v}'."),
        };
    }
}

public static class LegacyCommandLine
{
    public static IReadOnlyList<OptionDefinition> Options { get; } =
    [
        new("src.filename", 'i', OptionKind.Value, Help: "Source file (plain or .gz)"),
        new("src.folder", 'f', OptionKind.Value, false),
        new("src.pattern", 'p', OptionKind.Value, false),
        new("src.filetype", null, OptionKind.Value, Help: "Override the file type used to detect gzip, e.g. gz"),
        new("src.include", null, OptionKind.List, false),
        new("src.exclude", null, OptionKind.List, false),
        new("fmt.delimiter", null, OptionKind.Value, Help: "Quote character (default \")"),
        new("fmt.delimiter.doublequote", null, OptionKind.Switch),
        new("fmt.delimiter.singlequote", null, OptionKind.Switch),
        new("fmt.delimiter.none", null, OptionKind.Switch),
        new("fmt.separator", null, OptionKind.Value, Help: "Field separator (default ,)"),
        new("fmt.escape", null, OptionKind.Value),
        new("fmt.headers.notincluded", null, OptionKind.Switch),
        new("fmt.headers", 'h', OptionKind.List, Help: "Column names for files without a header, or the columns to read"),
        new("fmt.headers.Derived", null, OptionKind.List, Help: "Extra columns not present in the file"),
        new("fmt.headers.replace.underscoreWithSpace", null, OptionKind.Switch),
        new("fmt.headers.replace.spaceWithUnderScore", null, OptionKind.Switch),
        new("fmt.headers.deduplicate", null, OptionKind.Switch),
        new("fmt.headers.skipRows.before", null, OptionKind.Value),
        new("fmt.headers.skipRows.after", null, OptionKind.Value),
        new("fmt.fromTable", null, OptionKind.Switch, false),
        new("fmt.sniff", null, OptionKind.Switch, Help: "Detect encoding, separator, quoting and column types"),
        new("fmt.sniff.samplerate", null, OptionKind.Value),
        new("fmt.sniff.leadingZeroIsString", 'z', OptionKind.Switch),
        new("fmt.sniff.integer.allowDecimalPoint", null, OptionKind.Switch),
        new("fmt.sniff.yesNoAsString", null, OptionKind.Switch),
        new("fmt.sniff.forceUtf8", null, OptionKind.Switch),
        new("fmt.sniff.forceCsv", null, OptionKind.Switch),
        new("fmt.encoding", null, OptionKind.Value),
        new("fmt.headers.nocamelcase", null, OptionKind.Switch),
        new("fmt.sniff.failIfRagged", null, OptionKind.Switch),
        new("processing.fields.surrogateKey", 'k', OptionKind.Switch),
        new("processing.fields.fileCreationTime", null, OptionKind.Switch, false),
        new("processing.fields.filename", null, OptionKind.Switch, false),
        new("processing.fields.foldername", null, OptionKind.Switch, false),
        new("processing.trim", null, OptionKind.Switch),
        new("processing.filters.applyDefault", null, OptionKind.Switch),
        new("processing.filters", null, OptionKind.List, Help: "Filters: [column/]Name[?Param=value&...]"),
        new("processing.filters.search", null, OptionKind.List, false),
        new("processing.filters.search.skipFile", null, OptionKind.Value, false),
        new("processing.filters.search.regex", null, OptionKind.List, false),
        new("processing.filters.skipEmptyRows", null, OptionKind.Switch),
        new("processing.columns.mapping", null, OptionKind.Value, false),
        new("processing.columns.fromHeader", null, OptionKind.Switch),
        new("processing.columns.unpivotKey", null, OptionKind.Value, false),
        new("processing.columns.unpivotSequence", null, OptionKind.Value, false),
        new("processing.limits.skipRows", null, OptionKind.Value),
        new("processing.limits.maxRows", null, OptionKind.Value),
        new("processing.variables", null, OptionKind.Value, Help: "JSON object of $variables for derivations"),
        new("processing.batchreference", null, OptionKind.Value, false),
        new("processing.noremovectrlchars", null, OptionKind.Switch),
        new("processing.profile.filename", null, OptionKind.Value, Help: "Write an SSIS Data Profile XML"),
        new("processing.profile.format", null, OptionKind.Value),
        new("processing.profile.options", null, OptionKind.Value),
        new("processing.profile.only", null, OptionKind.Switch),
        new("processing.profile.typeFilename", null, OptionKind.Value, false),
        new("processing.weaklyTyped", null, OptionKind.Switch, Help: "Create every column as (n)varchar(textLength)"),
        new("processing.rawValues", null, OptionKind.Switch),
        new("processing.errors.type.ignore", null, OptionKind.Switch),
        new("dest.none", 'n', OptionKind.Switch, Help: "Read and validate only"),
        new("dest.headers.none", null, OptionKind.Switch, false),
        new("dest.console", null, OptionKind.Switch, false),
        new("dest.filename", 'o', OptionKind.Value, false),
        new("dest.foldername", null, OptionKind.Value, false),
        new("dest.file.headerFilename", null, OptionKind.Value, false),
        new("dest.file.rowsPerFile", null, OptionKind.Value, false),
        new("dest.sql.server", 's', OptionKind.Value, Help: "SQL Server host"),
        new("dest.sql.server.trustcertificate", null, OptionKind.Switch),
        new("dest.sql.server.disableTransparentNetworkIPResolution", null, OptionKind.Switch),
        new("dest.sql.database", 'd', OptionKind.Value),
        new("dest.sql.schema", null, OptionKind.Value, Help: "Destination schema (default staging)"),
        new("dest.sql.schema.create", null, OptionKind.Switch),
        new("dest.sql.table", 't', OptionKind.Value),
        new("dest.sql.table.nocreate", null, OptionKind.Switch),
        new("dest.sql.table.drop", null, OptionKind.Switch),
        new("dest.sql.table.truncate", null, OptionKind.Switch),
        new("dest.sql.table.lock", null, OptionKind.Switch),
        new("dest.sql.table.bufferSize", null, OptionKind.Value),
        new("dest.sql.format.textLength", null, OptionKind.Value),
        new("dest.sql.format.decimalSize", null, OptionKind.Value),
        new("dest.sql.format.unicode", null, OptionKind.Switch),
        new("dest.sql.auth.username", null, OptionKind.Value, Help: "SQL authentication login"),
        new("dest.sql.auth.password", null, OptionKind.Value, Help: "SQL password (or set CSVTODB_SQL_PASSWORD)"),
        new("dest.sql.createImportLogEntry", null, OptionKind.Switch, false),
        new("dest.return.rowcount", null, OptionKind.Switch),
        new("measure.columns.sum", null, OptionKind.List, false),
        new("measure.columns.min", null, OptionKind.List, false),
        new("measure.columns.max", null, OptionKind.List, false),
        new("ui.waitForKeyPress.no", null, OptionKind.Switch),
        new("ui.logger.filename", 'l', OptionKind.Value),
        new("ui.logger.verbose", 'v', OptionKind.Switch),
    ];

    private static readonly Dictionary<string, OptionDefinition> ByName =
        Options.ToDictionary(o => o.Name, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<char, OptionDefinition> ByShortName =
        Options.Where(o => o.ShortName.HasValue).ToDictionary(o => char.ToLowerInvariant(o.ShortName!.Value));

    public static ParsedCommandLine Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var help = false;
        var version = false;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is "--help" or "-?" or "help")
            {
                help = true;
                continue;
            }

            if (arg is "--version" or "version")
            {
                version = true;
                continue;
            }

            if (!TryGetOption(arg, out var def, out var inlineValue))
            {
                throw new CommandLineException($"Unknown argument '{arg}'.");
            }

            if (values.ContainsKey(def.Name))
            {
                throw new CommandLineException($"Option '{def.Name}' is specified more than once.");
            }

            var list = new List<string>();
            switch (def.Kind)
            {
                case OptionKind.Switch:
                    if (inlineValue is not null)
                    {
                        throw new CommandLineException($"Option '{def.Name}' does not take a value.");
                    }

                    break;
                case OptionKind.Value:
                    if (inlineValue is not null)
                    {
                        list.Add(inlineValue);
                    }
                    else if (i + 1 < args.Count && !IsOption(args[i + 1]))
                    {
                        list.Add(args[++i]);
                    }
                    else
                    {
                        throw new CommandLineException($"Option '{def.Name}' requires a value.");
                    }

                    break;
                case OptionKind.List:
                    if (inlineValue is not null)
                    {
                        list.Add(inlineValue);
                    }

                    while (i + 1 < args.Count && !IsOption(args[i + 1]))
                    {
                        list.Add(args[++i]);
                    }

                    break;
            }

            values.Add(def.Name, list);
        }

        return new ParsedCommandLine(values) { Help = help, Version = version };
    }

    private static bool IsOption(string arg) => TryGetOption(arg, out _, out _) || arg.StartsWith("--", StringComparison.Ordinal);

    private static bool TryGetOption(string arg, out OptionDefinition def, out string? inlineValue)
    {
        inlineValue = null;
        def = null!;
        if (arg.StartsWith("--", StringComparison.Ordinal) && arg.Length > 2)
        {
            var name = arg[2..];
            var eq = name.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0)
            {
                inlineValue = name[(eq + 1)..];
                name = name[..eq];
            }

            if (ByName.TryGetValue(name, out var d))
            {
                def = d;
                return true;
            }

            return false;
        }

        if (arg.Length == 2 && arg[0] == '-' && ByShortName.TryGetValue(char.ToLowerInvariant(arg[1]), out var s))
        {
            def = s;
            return true;
        }

        return false;
    }
}
