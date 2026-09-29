using System.Text.Json;
using CsvToSql.Core.Import;

namespace CsvToSql.Cli;

/// <summary>Settings that only affect the command line host.</summary>
public sealed record HostOptions(bool WaitForKeyPress, string? LogFilename, bool Verbose, bool ReturnRowCount);

/// <summary>Maps the legacy command line onto <see cref="ImportOptions"/>.</summary>
public static class OptionsMapper
{
    public const string PasswordEnvironmentVariable = "CSVTODB_SQL_PASSWORD";

    public static HostOptions MapHost(ParsedCommandLine cl)
    {
        ArgumentNullException.ThrowIfNull(cl);
        return new HostOptions(
            !cl.Switch("ui.waitForKeyPress.no"),
            cl.Value("ui.logger.filename"),
            cl.Switch("ui.logger.verbose"),
            cl.Switch("dest.return.rowcount"));
    }

    public static ImportOptions Map(ParsedCommandLine cl, Func<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(cl);
        environment ??= Environment.GetEnvironmentVariable;

        var unsupported = cl.OptionNames
            .Select(n => LegacyCommandLine.Options.First(o => string.Equals(o.Name, n, StringComparison.OrdinalIgnoreCase)))
            .Where(o => !o.Supported)
            .Select(o => "--" + o.Name)
            .ToList();
        if (unsupported.Count > 0)
        {
            throw new CommandLineException($"Not supported yet: {string.Join(", ", unsupported)}");
        }

        var source = cl.Value("src.filename") ?? throw new CommandLineException("A source file (-i/--src.filename) is required.");

        var delimiter = cl.CharValue("fmt.delimiter") ?? '"';
        if (cl.Switch("fmt.delimiter.doublequote"))
        {
            delimiter = '"';
        }

        if (cl.Switch("fmt.delimiter.singlequote"))
        {
            delimiter = '\'';
        }

        if (cl.Switch("fmt.delimiter.none"))
        {
            delimiter = '\0';
        }

        var profileOnly = cl.Switch("processing.profile.only");
        var options = new ImportOptions
        {
            SourceFile = source,
            SourceFileType = cl.Value("src.filetype"),
            EncodingName = cl.Value("fmt.encoding") ?? "UTF-8",
            Separator = cl.CharValue("fmt.separator") ?? ',',
            Delimiter = delimiter,
            ForceNoDelimiter = cl.Switch("fmt.delimiter.none"),
            Escape = cl.CharValue("fmt.escape"),
            HasHeaders = !cl.Switch("fmt.headers.notincluded"),
            ManualHeaders = cl.List("fmt.headers"),
            DerivedHeaders = cl.List("fmt.headers.Derived"),
            ReplaceUnderscoreWithSpace = cl.Switch("fmt.headers.replace.underscoreWithSpace"),
            ReplaceSpaceWithUnderscore = cl.Switch("fmt.headers.replace.spaceWithUnderScore"),
            DeduplicateHeaders = cl.Switch("fmt.headers.deduplicate"),
            HeaderRowsToSkipBefore = cl.IntValue("fmt.headers.skipRows.before", -1),
            HeaderRowsToSkipAfter = cl.IntValue("fmt.headers.skipRows.after", -1),
            Sniff = cl.Switch("fmt.sniff") || profileOnly,
            SniffSampleRate = cl.IntValue("fmt.sniff.samplerate", 1),
            SniffLeadingZeroIsString = cl.Switch("fmt.sniff.leadingZeroIsString"),
            SniffIntegerAllowDecimalPoint = cl.Switch("fmt.sniff.integer.allowDecimalPoint"),
            SniffYesNoAsString = cl.Switch("fmt.sniff.yesNoAsString"),
            SniffForceUtf8 = cl.Switch("fmt.sniff.forceUtf8"),
            SniffForceCsv = cl.Switch("fmt.sniff.forceCsv"),
            CamelCaseColumnNames = !cl.Switch("fmt.headers.nocamelcase"),
            FailIfRagged = cl.Switch("fmt.sniff.failIfRagged"),
            AddSurrogateKey = cl.Switch("processing.fields.surrogateKey"),
            TrimValues = cl.Switch("processing.trim"),
            ApplyDefaultFilters = cl.Switch("processing.filters.applyDefault"),
            Filters = cl.List("processing.filters").Where(f => !string.IsNullOrWhiteSpace(f)).ToList(),
            SkipEmptyRows = cl.Switch("processing.filters.skipEmptyRows"),
            ColumnsFromHeader = cl.Switch("processing.columns.fromHeader"),
            SkipRows = cl.IntValue("processing.limits.skipRows", -1),
            MaxRows = cl.IntValue("processing.limits.maxRows", -1),
            Variables = ParseVariables(cl.Value("processing.variables")),
            StripControlCharacters = !cl.Switch("processing.noremovectrlchars"),
            ProfileFilename = cl.Value("processing.profile.filename"),
            ProfileFormat = cl.Value("processing.profile.format") ?? "xml",
            ProfileOptions = cl.Value("processing.profile.options") ?? "LNV",
            ProfileOnly = profileOnly,
            WeaklyTyped = cl.Switch("processing.weaklyTyped"),
            RawValues = cl.Switch("processing.rawValues"),
            IgnoreTypeConversionErrors = cl.Switch("processing.errors.type.ignore"),
            DestNone = cl.Switch("dest.none") || profileOnly,
        };

        var server = cl.Value("dest.sql.server");
        if (!string.IsNullOrWhiteSpace(server))
        {
            var username = cl.Value("dest.sql.auth.username");
            var password = cl.Value("dest.sql.auth.password");
            if (!string.IsNullOrWhiteSpace(username) && password is null)
            {
                password = environment(PasswordEnvironmentVariable);
            }

            options.Sql = new SqlDestinationOptions
            {
                Server = server,
                Database = cl.Value("dest.sql.database"),
                Schema = cl.Value("dest.sql.schema") ?? "staging",
                Table = cl.Value("dest.sql.table"),
                Username = username,
                Password = password,
                TrustServerCertificate = cl.Switch("dest.sql.server.trustcertificate"),
                CreateSchema = cl.Switch("dest.sql.schema.create"),
                CreateTable = !cl.Switch("dest.sql.table.nocreate"),
                DropTable = cl.Switch("dest.sql.table.drop"),
                TruncateTable = cl.Switch("dest.sql.table.truncate"),
                TableLock = cl.Switch("dest.sql.table.lock"),
                BatchSize = cl.IntValue("dest.sql.table.bufferSize", -1),
                TextLength = cl.IntValue("dest.sql.format.textLength", 255),
                DecimalSize = cl.Value("dest.sql.format.decimalSize") ?? "18,10",
                Unicode = cl.Switch("dest.sql.format.unicode"),
            };
        }
        else if (!options.DestNone)
        {
            throw new CommandLineException("A destination is required: -s/--dest.sql.server or -n/--dest.none.");
        }

        return options;
    }

    private static Dictionary<string, string> ParseVariables(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
            return new Dictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            throw new CommandLineException($"--processing.variables must be a JSON object of strings: {ex.Message}");
        }
    }
}
