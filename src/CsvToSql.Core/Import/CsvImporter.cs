using System.Globalization;
using System.Text;
using CsvToSql.Core.Csv;
using CsvToSql.Core.Filters;
using CsvToSql.Core.Io;
using CsvToSql.Core.Logging;
using CsvToSql.Core.Profiling;
using CsvToSql.Core.Sql;

namespace CsvToSql.Core.Import;

/// <summary>Result of importing one file.</summary>
public sealed record ImportResult(long RowsWritten, IReadOnlyList<ColumnProfile>? Profile);

/// <summary>
/// Orchestrates one file: sniff, profile, build the output columns and filters, then stream into SQL Server.
/// </summary>
public sealed class CsvImporter(ImportOptions options, ImportLog? log = null)
{
    private readonly ImportLog _log = log ?? ImportLog.Null;

    /// <summary>Output fields and their destination names, available after <see cref="Prepare"/>.</summary>
    public sealed record Plan(
        Encoding Encoding,
        char Separator,
        char Delimiter,
        bool HasHeaders,
        bool StronglyTyped,
        IReadOnlyList<FieldSpec> Fields,
        IReadOnlyList<string> DestinationNames,
        IReadOnlyList<ColumnProfile>? Profile);

    public ImportResult Run()
    {
        using var reader = OpenReader(out var plan);
        if (options.DestNone || options.ProfileOnly || options.Sql is null)
        {
            if (!options.DestNone && !options.ProfileOnly)
            {
                throw new NotSupportedException("Only SQL Server destinations (-s/--dest.sql.server) and --dest.none are supported so far.");
            }

            long count = 0;
            if (!options.ProfileOnly)
            {
                var values = new object[reader.FieldCount];
                while (reader.Read())
                {
                    reader.GetValues(values);
                    count++;
                }
            }

            return new ImportResult(count, plan.Profile);
        }

        var sql = options.Sql;
        var table = string.IsNullOrWhiteSpace(sql.Table)
            ? Path.GetFileName(options.SourceFile).Replace('.', '_')
            : sql.Table;
        var columns = plan.Fields
            .Select((f, i) => new DestinationColumn(
                plan.DestinationNames[i],
                SqlDdl.SqlTypeFor(f, plan.StronglyTyped && !options.WeaklyTyped, sql.Unicode, sql.TextLength, sql.DecimalSize)))
            .ToList();
        var importer = new SqlBulkImporter(sql, _log);
        var rows = importer.Import(reader, table, columns, options.AddSurrogateKey);
        return new ImportResult(rows, plan.Profile);
    }

    /// <summary>Sniffs (if requested), writes the profile and opens a streaming reader over the file.</summary>
    public CsvDataReader OpenReader(out Plan plan)
    {
        plan = Prepare(out var recordReader);
        return new CsvDataReader(recordReader, plan.Fields, plan.DestinationNames)
        {
            StronglyTyped = plan.StronglyTyped,
            WeaklyTyped = options.WeaklyTyped,
            RawValues = options.RawValues,
            TrimValues = options.TrimValues,
            SkipBlankRows = options.SkipEmptyRows,
            StripControlCharacters = options.StripControlCharacters,
            IgnoreTypeConversionErrors = options.IgnoreTypeConversionErrors,
            RowsToSkip = options.SkipRows,
            MaxRowsToRead = options.MaxRows,
            IntegerParsingStyle = options.IntegerParsingStyle,
            Variables = options.Variables,
        };
    }

    private Plan Prepare(out CsvRecordReader recordReader)
    {
        var gzip = SourceFile.IsGzip(options.SourceFile, options.SourceFileType);
        var encoding = GetEncoding(options.EncodingName);
        var separator = options.Separator;
        var delimiter = options.ForceNoDelimiter ? '\0' : options.Delimiter;
        var hasHeaders = options.HasHeaders;
        var stronglyTyped = false;
        List<FieldSpec>? fields = null;
        IReadOnlyList<ColumnProfile>? profile = null;

        if (options.Sniff || options.ProfileOnly)
        {
            var sniffer = new CsvSniffer
            {
                TreatDashAsNull = true,
                TreatYesNoAsBool = !options.SniffYesNoAsString,
                TreatPercentageAsNumeric = true,
                TreatCurrencyAsNumeric = true,
                TreatNumericFieldsWithLeadingZeroAsString = options.SniffLeadingZeroIsString,
                TrimFields = options.TrimValues,
                HeaderRowsToSkipBefore = options.HeaderRowsToSkipBefore,
                HeaderRowsToSkipAfter = options.HeaderRowsToSkipAfter,
                DeduplicateHeaders = options.DeduplicateHeaders,
                IntegerParsingStyle = options.IntegerParsingStyle,
                ProfilerTypes = ProfilerTypeParser.Parse(options.ProfileOptions),
                ForceNoDelimiter = options.ForceNoDelimiter,
                FailIfRagged = options.FailIfRagged,
                NoHeaders = !options.HasHeaders,
                StripControlCharacters = options.StripControlCharacters,
                CsvEscape = options.Escape,
            };
            if (options.SniffForceUtf8)
            {
                sniffer.FileEncoding = new UTF8Encoding(false);
            }

            if (options.SniffForceCsv)
            {
                sniffer.Separator = ',';
                sniffer.Delimiter = '"';
                sniffer.SeparatorAndDelimiterFixed = true;
            }

            var writeProfile = !string.IsNullOrEmpty(options.ProfileFilename);
            if (!writeProfile)
            {
                sniffer.ProfilerTypes = ProfilerType.None;
            }

            sniffer.Sniff(options.SourceFile, options.SniffSampleRate, int.MaxValue, gzip);
            encoding = sniffer.FileEncoding ?? encoding;
            separator = sniffer.Separator;
            delimiter = sniffer.Delimiter;
            hasHeaders = sniffer.HasHeaders;
            profile = sniffer.Results;
            _log.Info(string.Create(CultureInfo.InvariantCulture,
                $"Sniffed {options.SourceFile}: encoding={encoding.WebName} separator={Printable(separator)} delimiter={Printable(delimiter)} headers={hasHeaders} columns={sniffer.Results.Count}"));

            if (writeProfile)
            {
                DataProfileWriter.Write(options.ProfileFormat, sniffer.Results, sniffer.ProfilerTypes, options.TrimValues,
                    options.SourceFile, options.ProfileFilename!, options.Sql?.Server, options.Sql?.Database, options.Sql?.Schema, options.Sql?.Table);
            }

            if (hasHeaders)
            {
                stronglyTyped = true;
                fields = sniffer.Results.Select((r, i) =>
                {
                    var type = r.BestDataType ?? typeof(string);
                    var f = new FieldSpec
                    {
                        FieldName = r.Name,
                        FieldType = type,
                        Size = type == typeof(string) ? r.MaxLength : -1,
                        ColumnIndex = i,
                        ReplaceSpaceWithUnderscore = options.ReplaceSpaceWithUnderscore,
                        ReplaceUnderscoreWithSpace = options.ReplaceUnderscoreWithSpace,
                    };
                    if (options.TrimValues)
                    {
                        f.Filters.AddRange(DefaultFilters.TrimFilters);
                    }

                    f.Filters.AddRange(DefaultFilters.ForType(type));
                    return f;
                }).ToList();
            }
        }

        var text = SourceFile.OpenText(options.SourceFile, gzip, encoding);
        recordReader = new CsvRecordReader(text, separator, delimiter == '\0' ? null : delimiter, options.Escape);
        string[]? header = null;
        try
        {
            SkipLines(text, options.HeaderRowsToSkipBefore);
            if (hasHeaders)
            {
                header = ReadHeader(recordReader);
                if (header is not null && options.ManualHeaders.Count > 0 && fields is null)
                {
                    var missing = options.ManualHeaders.Where(h => !header.Contains(h)).ToList();
                    if (missing.Count > 0)
                    {
                        throw new InvalidDataException($"Columns not found in header: {string.Join(", ", missing)}");
                    }

                    var hdr = header;
                    fields = options.ManualHeaders.Select(h => NewTextField(h, Array.IndexOf(hdr, h))).ToList();
                }

                if (header is not null && fields is not null)
                {
                    var expected = fields.Select(f => f.FieldName).Where(n => !options.DerivedHeaders.Contains(n)).ToList();
                    var missing = expected.Except(header).ToList();
                    if (missing.Count > 0)
                    {
                        throw new InvalidDataException(
                            $"Error with header record from file, missing: {string.Join(',', missing)} expected: {string.Join(',', expected)} actual: {string.Join(',', header)}");
                    }
                }

                fields ??= (header ?? []).Select((h, i) => NewTextField(h, i)).ToList();
            }
            else if (fields is null)
            {
                if (options.ManualHeaders.Count > 0)
                {
                    fields = options.ManualHeaders.Select((h, i) => NewTextField(h, i)).ToList();
                }
                else
                {
                    var width = PeekWidth(options.SourceFile, gzip, encoding, separator, delimiter);
                    fields = Enumerable.Range(0, width)
                        .Select(i => NewTextField("Column_" + i.ToString(CultureInfo.InvariantCulture), i)).ToList();
                }
            }

            if (hasHeaders)
            {
                for (var skip = options.HeaderRowsToSkipAfter; skip > 0 && recordReader.ReadRecord() is not null; skip--)
                {
                }
            }

            var outputFields = BuildOutputFields(fields);
            var names = DestinationNames(outputFields);
            return new Plan(encoding, separator, delimiter, hasHeaders, stronglyTyped, outputFields, names, profile);
        }
        catch
        {
            recordReader.Dispose();
            throw;
        }
    }

    private FieldSpec NewTextField(string name, int index) => new()
    {
        FieldName = name,
        ColumnIndex = index,
        ReplaceSpaceWithUnderscore = options.ReplaceSpaceWithUnderscore,
        ReplaceUnderscoreWithSpace = options.ReplaceUnderscoreWithSpace,
    };

    private string[]? ReadHeader(CsvRecordReader reader)
    {
        string[]? record;
        do
        {
            record = reader.ReadRecord();
            if (record is not null)
            {
                ControlCharacters.StripInPlace(record);
            }
        }
        while (record is not null && options.SkipEmptyRows && record.All(string.IsNullOrEmpty));

        if (record is null)
        {
            return null;
        }

        if (options.DeduplicateHeaders)
        {
            return Deduplicate(record);
        }

        var dup = record.GroupBy(h => h, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
        {
            throw new InvalidDataException($"Duplicate column name in header: '{dup.Key}'. Use --fmt.headers.deduplicate to rename duplicates.");
        }

        return record;
    }

    internal static string[] Deduplicate(IReadOnlyList<string> header)
    {
        var row = new string[header.Count];
        for (var i = 0; i < header.Count; i++)
        {
            var name = header[i];
            var j = 2;
            while (row.Take(i).Any(p => string.Equals(p, name, StringComparison.CurrentCultureIgnoreCase)))
            {
                name = string.Create(CultureInfo.InvariantCulture, $"{header[i]} {j}");
                j++;
            }

            row[i] = name;
        }

        return row;
    }

    private List<FieldSpec> BuildOutputFields(List<FieldSpec> fields)
    {
        var output = new List<FieldSpec>(fields);
        foreach (var dc in options.DerivedHeaders)
        {
            var existing = output.FindIndex(f => f.FieldName == dc);
            var derived = new FieldSpec
            {
                FieldName = dc,
                FieldType = typeof(string),
                Size = 255,
                ReplaceSpaceWithUnderscore = options.ReplaceSpaceWithUnderscore,
                ReplaceUnderscoreWithSpace = options.ReplaceUnderscoreWithSpace,
            };
            if (existing >= 0)
            {
                output[existing] = derived;
            }
            else
            {
                output.Add(derived);
            }
        }

        var dupNames = output.Where(f => f.Derived && output.Any(o => o != f && o.CamelName == f.CamelName)).Select(f => f.FieldName).ToList();
        if (dupNames.Count > 0)
        {
            throw new InvalidDataException($"Duplicate column names: {string.Join(", ", dupNames)}");
        }

        if (options.ApplyDefaultFilters)
        {
            foreach (var f in output)
            {
                foreach (var df in DefaultFilters.ForType(f.FieldType))
                {
                    if (!f.Filters.Exists(x => x.Name == df.Name))
                    {
                        f.Filters.Add(df);
                    }
                }
            }
        }

        foreach (var def in options.Filters.Select(FilterDefinition.Parse))
        {
            var filter = FilterFactory.Create(def);
            var targets = output.Where(f => def.Column.Length == 0 || f.FieldName == def.Column).ToList();
            if (targets.Count == 0)
            {
                _log.Warn($"Filter '{def.FilterName}' targets unknown column '{def.Column}' and was ignored.");
            }

            foreach (var f in targets)
            {
                f.Filters.Add(filter);
            }
        }

        return output;
    }

    private List<string> DestinationNames(List<FieldSpec> fields)
    {
        var names = new List<string>(fields.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            var name = options.CamelCaseColumnNames ? f.CamelName : f.SafeFieldName;
            if (string.IsNullOrEmpty(f.CamelName))
            {
                name = "Column_" + i.ToString(CultureInfo.InvariantCulture);
            }

            if (options.ColumnsFromHeader && !seen.Add(f.CamelName.Length == 0 ? name : f.CamelName))
            {
                throw new InvalidDataException($"Column {f.CamelName} already exists in mapping");
            }

            names.Add(name);
        }

        var dup = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
        {
            throw new InvalidDataException($"Duplicate destination column name: {dup.Key}");
        }

        return names;
    }

    private static void SkipLines(StreamReader reader, int count)
    {
        for (var i = 0; i < count && reader.ReadLine() is not null; i++)
        {
        }
    }

    private int PeekWidth(string path, bool gzip, Encoding encoding, char separator, char delimiter)
    {
        using var text = SourceFile.OpenText(path, gzip, encoding);
        SkipLines(text, options.HeaderRowsToSkipBefore);
        using var r = new CsvRecordReader(text, separator, delimiter == '\0' ? null : delimiter, options.Escape);
        return r.ReadRecord()?.Length ?? 0;
    }

    private static Encoding GetEncoding(string name)
    {
        EncodingDetector.EnsureCodePages();
        return string.Equals(name, "UTF-8", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "utf8", StringComparison.OrdinalIgnoreCase)
            ? new UTF8Encoding(false)
            : Encoding.GetEncoding(name);
    }

    private static string Printable(char c) => c switch
    {
        '\0' => "none",
        '\t' => "\\t",
        _ => c.ToString(),
    };
}
