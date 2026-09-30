using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CsvToSql.Core.Profiling;

/// <summary>
/// Writes column profiles in the SSIS Data Profiling Task XML format (and the legacy CSV formats).
/// </summary>
public static class DataProfileWriter
{
    public static readonly XNamespace Ns = "http://schemas.microsoft.com/sqlserver/2008/DataDebugger/";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XNamespace Xsd = "http://www.w3.org/2001/XMLSchema";

    public static void Write(string format, IReadOnlyList<ColumnProfile> results, ProfilerType types, bool ignoreSpace,
        string sourceFilename, string destination, string? server, string? database, string? schema, string? table)
    {
        switch ((format ?? "xml").Trim().ToLowerInvariant())
        {
            case "xml":
                WriteXml(BuildXml(results, types, ignoreSpace, sourceFilename, server, database, schema, table, Guid.NewGuid(), Guid.NewGuid()), destination);
                break;
            case "csv":
                File.WriteAllText(destination, BuildCompactCsv(results));
                break;
            case "csvdq":
                File.WriteAllText(destination, BuildDqCsv(results));
                break;
            default:
                throw new ArgumentException($"Unknown profile format '{format}'. Expected xml, csv or csvdq.");
        }
    }

    public static XDocument BuildXml(IReadOnlyList<ColumnProfile> results, ProfilerType types, bool ignoreSpace,
        string filename, string? server, string? database, string? schema, string? table, Guid srcGuid, Guid dstGuid)
    {
        ArgumentNullException.ThrowIfNull(results);
        server = string.IsNullOrEmpty(server) ? filename : server;
        database ??= string.Empty;
        schema ??= string.Empty;
        table = string.IsNullOrEmpty(table) ? filename : table;

        var nullProfiler = (types & ProfilerType.ColumnNullRatio) != 0;
        var lengthProfiler = (types & ProfilerType.ColumnLengthDistribution) != 0;
        var valueProfiler = (types & ProfilerType.ColumnValueDistribution) != 0;

        XElement RequestTable() => new(Ns + "Table", new XAttribute("Schema", "dbo"), new XAttribute("Table", filename));
        XElement OutputTable(ColumnProfile s) => new(Ns + "Table",
            new XAttribute("DataSource", server),
            new XAttribute("Database", database),
            new XAttribute("Schema", schema),
            new XAttribute("Table", table),
            new XAttribute("RowCount", s.RowsChecked));
        XElement Column(ColumnProfile s, Type type) => new(Ns + "Column",
            new XAttribute("Name", s.Name),
            new XAttribute("SqlDbType", type.GetSqlDbType()),
            new XAttribute("MaxLength", s.MaxLength.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("LCID", "1033"),
            new XAttribute("CodePage", "0"),
            new XAttribute("IsNullable", "true"),
            new XAttribute("StringCompareOptions", "0"));
        Type Best(ColumnProfile s) => s.BestDataType ?? typeof(string);

        return new XDocument(
            new XDeclaration("1.0", "utf-8", "no"),
            new XElement(Ns + "DataProfile",
                new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
                new XAttribute(XNamespace.Xmlns + "xsd", Xsd),
                new XElement(Ns + "ProfileVersion", "1.0"),
                new XElement(Ns + "DataSources",
                    new XElement(Ns + "DtsDataSource", new XAttribute("ID", srcGuid), new XAttribute("Name", filename),
                        new XElement(Ns + "DtsConnectionManagerID", filename)),
                    new XElement(Ns + "DtsDataSource", new XAttribute("ID", dstGuid), new XAttribute("Name", server + "." + database),
                        new XElement(Ns + "DtsConnectionManagerID", server + "." + database))),
                new XElement(Ns + "DataProfileInput",
                    new XElement(Ns + "ProfileMode", "Exact"),
                    new XElement(Ns + "Timeout", "0"),
                    new XElement(Ns + "Requests",
                        nullProfiler
                            ? new XElement(Ns + "ColumnNullRatioProfileRequest", new XAttribute("ID", "NullRatioReq"),
                                new XElement(Ns + "DataSourceID", dstGuid),
                                RequestTable(),
                                new XElement(Ns + "Column", new XAttribute("IsWildCard", "true")))
                            : null,
                        lengthProfiler
                            ? new XElement(Ns + "ColumnLengthDistributionProfileRequest", new XAttribute("ID", "LengthDistReq"),
                                new XElement(Ns + "DataSourceID", dstGuid),
                                RequestTable(),
                                new XElement(Ns + "Column", new XAttribute("IsWildCard", "true")),
                                new XElement(Ns + "IgnoreLeadingSpace", ignoreSpace),
                                new XElement(Ns + "IgnoreTrailingSpace", ignoreSpace))
                            : null,
                        valueProfiler
                            ? new XElement(Ns + "ColumnValueDistributionProfileRequest", new XAttribute("ID", "ValueDistReq"),
                                new XElement(Ns + "DataSourceID", dstGuid),
                                RequestTable(),
                                new XElement(Ns + "Column", new XAttribute("IsWildCard", "true")),
                                new XElement(Ns + "Option", "FrequentValues"),
                                new XElement(Ns + "FrequentValueThreshold", "0.001"))
                            : null)),
                new XElement(Ns + "DataProfileOutput",
                    new XElement(Ns + "Profiles",
                        results.Where(_ => nullProfiler).Select(s =>
                            new XElement(Ns + "ColumnNullRatioProfile", new XAttribute("ProfileRequestID", "NullRatioReq"), new XAttribute("IsExact", "true"),
                                new XElement(Ns + "DataSourceID", dstGuid),
                                OutputTable(s),
                                Column(s, Best(s)),
                                new XElement(Ns + "NullCount", s.RowsChecked - s.NumPopulated))),
                        results.Where(s => lengthProfiler && s.BaseDataType == typeof(string)).Select(s =>
                            new XElement(Ns + "ColumnLengthDistributionProfile", new XAttribute("ProfileRequestID", "LengthDistReq"), new XAttribute("IsExact", "true"),
                                new XElement(Ns + "DataSourceID", dstGuid),
                                OutputTable(s),
                                Column(s, Best(s)),
                                new XElement(Ns + "IgnoreLeadingSpace", ignoreSpace),
                                new XElement(Ns + "IgnoreTrailingSpace", ignoreSpace),
                                new XElement(Ns + "MinLength", s.MinLength),
                                new XElement(Ns + "MaxLength", s.MaxLength),
                                new XElement(Ns + "LengthDistribution",
                                    s.LengthDistributions.Select(ld =>
                                        new XElement(Ns + "LengthDistributionItem",
                                            new XElement(Ns + "Length", ld.Key),
                                            new XElement(Ns + "Count", ld.Value)))))),
                        results.Where(_ => valueProfiler).Select(s =>
                            new XElement(Ns + "ColumnValueDistributionProfile", new XAttribute("ProfileRequestID", "ValueDistReq"), new XAttribute("IsExact", "true"),
                                new XElement(Ns + "DataSourceID", dstGuid),
                                OutputTable(s),
                                Column(s, Best(s) == typeof(DateTime) ? typeof(string) : Best(s)),
                                new XElement(Ns + "NumberOfDistinctValues", s.NumDistinctValues),
                                new XElement(Ns + "ValueDistribution",
                                    s.Values.Select(v =>
                                        new XElement(Ns + "ValueDistributionItem",
                                            new XElement(Ns + "Value", s.TypedValue(v.Key)),
                                            new XElement(Ns + "Count", v.Value))))))))));
    }

    public static void WriteXml(XDocument doc, string destination)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var dir = Path.GetDirectoryName(Path.GetFullPath(destination));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var settings = new XmlWriterSettings
        {
            Indent = false,
            NewLineOnAttributes = false,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
        };
        using var w = XmlWriter.Create(destination, settings);
        doc.WriteTo(w);
    }

    public static string BuildCompactCsv(IReadOnlyList<ColumnProfile> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var sb = new StringBuilder();
        sb.AppendLine("Field,Type,SqlType,RowCount,NullCount,DistinctCount,MinLength,MaxLength");
        foreach (var s in results)
        {
            var best = s.BestDataType ?? typeof(string);
            sb.Append(CultureInfo.InvariantCulture,
                $"\"{s.Name}\",{best},{best.GetSqlDbType()},{s.RowsChecked},{s.RowsChecked - s.NumPopulated},{s.NumDistinctValues},{s.MinLength},{s.MaxLength}{Environment.NewLine}");
        }

        return sb.ToString();
    }

    public static string BuildDqCsv(IReadOnlyList<ColumnProfile> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var sb = new StringBuilder();
        sb.AppendLine("Field,Type,Value,RowCount");
        foreach (var s in results)
        {
            var typeName = (s.BestDataType ?? typeof(string)).GetSqlDbType();
            foreach (var v in s.Values)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"\"{s.Name}\",\"{typeName}\",\"{v.Key.Replace("\"", "\"\"", StringComparison.Ordinal)}\",{v.Value}");
            }
        }

        return sb.ToString();
    }
}
