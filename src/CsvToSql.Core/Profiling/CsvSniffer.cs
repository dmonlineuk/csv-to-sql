using System.Text;
using CsvToSql.Core.Csv;
using CsvToSql.Core.Io;

namespace CsvToSql.Core.Profiling;

/// <summary>
/// Detects encoding, separator, quote delimiter, headers and column types of a CSV file.
/// </summary>
public sealed class CsvSniffer : ColumnProfiler
{
    private static readonly char[] CandidateSeparators = [',', '\t', ';', ':', '|', '¬', '‡'];
    private static readonly char[] CandidateQualifiers = ['\0', '"', '\''];

    public char Separator { get; set; } = ',';

    /// <summary>Quote delimiter; <c>'\0'</c> means none.</summary>
    public char Delimiter { get; set; } = '\0';

    /// <summary>When true the separator and delimiter are not sniffed.</summary>
    public bool SeparatorAndDelimiterFixed { get; set; }

    public bool ForceNoDelimiter { get; set; }

    public char? CsvEscape { get; set; }

    public bool CsvEscapeDelimiterOnly { get; set; }

    public bool HasHeaders { get; private set; }

    public bool NoHeaders { get; set; }

    public bool DeduplicateHeaders { get; set; }

    public int HeaderRowsToSkipBefore { get; set; } = -1;

    public int HeaderRowsToSkipAfter { get; set; } = -1;

    public Encoding? FileEncoding { get; set; }

    public bool FailIfRagged { get; set; }

    public IReadOnlyList<string> HeaderColumns => Results.Select(r => r.Name).ToList();

    public int Sniff(string filename, int sampleRate, int maxRows, bool gzip)
    {
        FileEncoding ??= EncodingDetector.Detect(filename, gzip);
        var delimRowsChecked = 0;
        if (!SeparatorAndDelimiterFixed)
        {
            delimRowsChecked = SniffSeparatorAndDelimiter(filename, sampleRate, maxRows, gzip);
        }

        var dataRowsChecked = SniffDataTypes(filename, sampleRate, maxRows, gzip);
        return Math.Max(delimRowsChecked, dataRowsChecked);
    }

    public int SniffSeparatorAndDelimiter(string filename, int sampleRate, int maxRows, bool gzip)
    {
        sampleRate = Math.Max(1, sampleRate);
        var seps = CandidateSeparators.ToDictionary(c => c, _ => 0);
        var qualifiers = CandidateQualifiers.ToDictionary(c => c, _ => 0);

        // Counts accumulate across lines, as in the legacy implementation.
        var lineSeps = CandidateSeparators.ToDictionary(c => c, _ => 0);
        var lineQuals = CandidateQualifiers.ToDictionary(c => c, _ => 0);

        using var sr = SourceFile.OpenText(filename, gzip, FileEncoding ?? new UTF8Encoding());
        var skipRows = HeaderRowsToSkipBefore;
        string? line = string.Empty;
        while (skipRows > 0 && line is not null)
        {
            line = sr.ReadLine();
            skipRows--;
        }

        var rowNum = 0;
        var rowsChecked = 0;
        while (rowsChecked < maxRows && (line = sr.ReadLine()) is not null)
        {
            if (rowNum % sampleRate == 0)
            {
                rowsChecked++;
                var pc = '\0';
                var qualified = false;
                var qualChar = '\0';
                foreach (var c in line)
                {
                    var isQualifier = lineQuals.ContainsKey(c);
                    var isSeparator = lineSeps.ContainsKey(c);
                    if ((pc == '\0' || lineSeps.ContainsKey(pc)) && isQualifier)
                    {
                        qualified = true;
                        qualChar = c;
                    }

                    if (isSeparator)
                    {
                        if (qualified && qualChar == pc)
                        {
                            qualified = false;
                            lineQuals[qualChar]++;
                            qualChar = '\0';
                        }

                        if (!qualified)
                        {
                            lineSeps[c]++;
                        }
                    }

                    pc = c;
                }

                var max = lineSeps.Values.Max();
                foreach (var kv in lineSeps.Where(kv => kv.Value == max))
                {
                    seps[kv.Key]++;
                }

                max = lineQuals.Values.Max();
                if (max > 0)
                {
                    foreach (var kv in lineQuals.Where(kv => kv.Value == max))
                    {
                        qualifiers[kv.Key]++;
                    }
                }
            }

            rowNum++;
        }

        var allMax = seps.Values.Max();
        if (allMax > 0)
        {
            Separator = seps.OrderBy(p => p.Key).First(p => p.Value == allMax).Key;
        }

        allMax = qualifiers.Values.Max();
        if (allMax > 0)
        {
            Delimiter = qualifiers.OrderBy(p => p.Key).First(p => p.Value == allMax).Key;
        }

        if (ForceNoDelimiter)
        {
            Delimiter = '\0';
        }

        return rowsChecked;
    }

    public int SniffDataTypes(string filename, int sampleRate, int maxRows, bool gzip)
    {
        sampleRate = Math.Max(1, sampleRate);
        Results.Clear();
        ProfileStart();

        using var sr = SourceFile.OpenText(filename, gzip, FileEncoding ?? new UTF8Encoding());
        var skipRows = HeaderRowsToSkipBefore;
        string? line = string.Empty;
        while (skipRows > 0 && line is not null)
        {
            line = sr.ReadLine();
            skipRows--;
        }

        using var reader = new CsvRecordReader(sr, Separator, Delimiter == '\0' ? null : Delimiter, CsvEscape, CsvEscapeDelimiterOnly);
        string[]? header = null;
        var process = NoHeaders;
        if (!NoHeaders)
        {
            header = reader.ReadRecord();
            if (header is not null)
            {
                ControlCharacters.StripInPlace(header);
            }

            // The legacy heuristic always concluded that a header row is present unless told otherwise.
            HasHeaders = header is not null;
            process = header is not null;
        }
        else
        {
            HasHeaders = false;
        }

        var rowCount = 0;
        if (process)
        {
            skipRows = HeaderRowsToSkipAfter;
            while (skipRows > 0 && reader.ReadRecord() is not null)
            {
                skipRows--;
            }

            string?[]? values = header is null ? null : new string?[header.Length];
            string[]? record;
            while (rowCount < maxRows && (record = reader.ReadRecord()) is not null)
            {
                if (header is null)
                {
                    header = Enumerable.Range(0, record.Length).Select(i => $"Column{i}").ToArray();
                    values = new string?[header.Length];
                }

                if (rowCount % sampleRate == 0)
                {
                    if (FailIfRagged && record.Length != header.Length)
                    {
                        throw new InvalidDataException(
                            $"File appears to be ragged. header: {string.Join(',', header)}\nRow ({rowCount}): {string.Join(',', record.Take(5))}");
                    }

                    if (record.Length > header.Length)
                    {
                        throw new InvalidDataException(
                            $"Row has more values than the header ({record.Length} > {header.Length}). header: {string.Join(',', header)}\nRow ({rowCount}): {string.Join(',', record.Take(5))}");
                    }

                    Array.Clear(values!);
                    Array.Copy(record, values!, record.Length);
                    ProfileStep(values!);
                }

                rowCount++;
            }

            if (rowCount == 0)
            {
                ProfileStep(new string?[header?.Length ?? 1]);
            }

            if (header is not null)
            {
                for (var i = 0; i < Results.Count && i < header.Length; i++)
                {
                    Results[i].Name = DeduplicateHeaders ? DeduplicatedName(header, i) : header[i];
                }
            }
        }

        return ProfileEnd();
    }

    internal static string DeduplicatedName(IReadOnlyList<string> header, int index)
    {
        var name = header[index];
        var k = 0;
        for (var j = 0; j < index; j++)
        {
            if (string.Equals(name, header[j], StringComparison.CurrentCultureIgnoreCase))
            {
                k++;
            }
        }

        return k > 0 ? $"{name} {k + 1}" : name;
    }
}
