using System.Globalization;
using CsvToSql.Core.Profiling;

namespace CsvToSql.Core.Import;

/// <summary>
/// Everything needed to import one CSV file. The legacy command line is mapped onto this model by the CLI.
/// </summary>
public sealed class ImportOptions
{
    public required string SourceFile { get; set; }

    /// <summary>Overrides the file extension when deciding whether the input is gzip compressed.</summary>
    public string? SourceFileType { get; set; }

    // --- format ---
    public string EncodingName { get; set; } = "UTF-8";

    public char Separator { get; set; } = ',';

    /// <summary>Quote character; <c>'\0'</c> means values are not quoted.</summary>
    public char Delimiter { get; set; } = '"';

    public char? Escape { get; set; }

    public bool HasHeaders { get; set; } = true;

    public IReadOnlyList<string> ManualHeaders { get; set; } = [];

    public IReadOnlyList<string> DerivedHeaders { get; set; } = [];

    public bool DeduplicateHeaders { get; set; }

    public int HeaderRowsToSkipBefore { get; set; } = -1;

    public int HeaderRowsToSkipAfter { get; set; } = -1;

    public bool ReplaceSpaceWithUnderscore { get; set; }

    public bool ReplaceUnderscoreWithSpace { get; set; }

    /// <summary>When false the source header names are kept rather than converted to the legacy camel case.</summary>
    public bool CamelCaseColumnNames { get; set; } = true;

    // --- sniffing ---
    public bool Sniff { get; set; }

    public int SniffSampleRate { get; set; } = 1;

    public bool SniffYesNoAsString { get; set; }

    public bool SniffLeadingZeroIsString { get; set; }

    public bool SniffIntegerAllowDecimalPoint { get; set; }

    public bool SniffForceUtf8 { get; set; }

    public bool SniffForceCsv { get; set; }

    public bool FailIfRagged { get; set; }

    public bool ForceNoDelimiter { get; set; }

    // --- processing ---
    public bool ColumnsFromHeader { get; set; }

    public bool TrimValues { get; set; }

    public bool ApplyDefaultFilters { get; set; }

    public IReadOnlyList<string> Filters { get; set; } = [];

    public bool SkipEmptyRows { get; set; }

    public bool StripControlCharacters { get; set; } = true;

    public bool WeaklyTyped { get; set; }

    public bool RawValues { get; set; }

    public bool IgnoreTypeConversionErrors { get; set; }

    public int SkipRows { get; set; } = -1;

    public int MaxRows { get; set; } = -1;

    public IReadOnlyDictionary<string, string> Variables { get; set; } = new Dictionary<string, string>();

    public bool AddSurrogateKey { get; set; }

    // --- profile ---
    public string? ProfileFilename { get; set; }

    public string ProfileFormat { get; set; } = "xml";

    public string ProfileOptions { get; set; } = "LNV";

    public bool ProfileOnly { get; set; }

    // --- destination ---
    /// <summary>When set rows are read and processed but not written anywhere.</summary>
    public bool DestNone { get; set; }

    public SqlDestinationOptions? Sql { get; set; }

    public NumberStyles IntegerParsingStyle =>
        SniffIntegerAllowDecimalPoint
            ? ColumnProfiler.DefaultIntegerParsingStyle | NumberStyles.AllowDecimalPoint
            : ColumnProfiler.DefaultIntegerParsingStyle;
}

public sealed class SqlDestinationOptions
{
    public required string Server { get; set; }

    public string? Database { get; set; }

    public string Schema { get; set; } = "staging";

    public string? Table { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public bool TrustServerCertificate { get; set; }

    public bool CreateSchema { get; set; }

    public bool CreateTable { get; set; } = true;

    public bool DropTable { get; set; }

    public bool TruncateTable { get; set; }

    public bool TableLock { get; set; }

    public int BatchSize { get; set; } = -1;

    public int TextLength { get; set; } = 255;

    public string DecimalSize { get; set; } = "18,10";

    public bool Unicode { get; set; }

    /// <summary>Extra connection string keywords, mainly for tests.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 30;
}
