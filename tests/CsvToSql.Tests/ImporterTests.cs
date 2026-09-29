using System.Xml.Linq;
using CsvToSql.Core.Import;
using CsvToSql.Core.Profiling;

namespace CsvToSql.Tests;

public class ImporterTests
{
    internal static ImportOptions ProductionLikeOptions(string source, string? profile = null) => new()
    {
        SourceFile = source,
        ColumnsFromHeader = true,
        Sniff = true,
        SniffYesNoAsString = true,
        CamelCaseColumnNames = false,
        DerivedHeaders = ["dmicRowId", "dmicFileId", "dmicFlowId", "dmicImportLogId", "dmicDtUpdated", "dmicDtAdded"],
        WeaklyTyped = true,
        TrimValues = true,
        ProfileFilename = profile,
        SkipEmptyRows = true,
        Filters =
        [
            "dmicRowId/Derivation?Format=$$guid",
            "dmicFileId/Derivation?Format=7554",
            "dmicFlowId/Derivation?Format=1152",
            "dmicImportLogId/Derivation?Format=b9073dde-8a73-4658-9038-2e9965aa29cb",
            "dmicDtUpdated/Derivation?Format=$$timestamp",
            "dmicDtAdded/Derivation?Format=$$timestamp",
        ],
        DestNone = true,
    };

    private static List<Dictionary<string, object>> ReadAll(ImportOptions options, out List<string> names)
    {
        using var reader = new CsvImporter(options).OpenReader(out _);
        names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new List<Dictionary<string, object>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    [Fact]
    public void FixtureProducesSourceAndDerivedColumns()
    {
        var rows = ReadAll(ProductionLikeOptions(TestFiles.Fixture), out var names);

        Assert.Equal(63 + 6, names.Count);
        Assert.Equal("Generated Record ID", names[0]);
        Assert.Contains("Critical Care Sequence Number (Derived)", names);
        Assert.Equal(["dmicRowId", "dmicFileId", "dmicFlowId", "dmicImportLogId", "dmicDtUpdated", "dmicDtAdded"], names[^6..]);
        Assert.Equal(2, rows.Count);

        Assert.Equal("CC000001", rows[0]["Generated Record ID"]);
        Assert.Equal("7554", rows[0]["dmicFileId"]);
        Assert.Equal("1152", rows[1]["dmicFlowId"]);
        Assert.Equal("b9073dde-8a73-4658-9038-2e9965aa29cb", rows[0]["dmicImportLogId"]);
        Assert.NotEqual(rows[0]["dmicRowId"], rows[1]["dmicRowId"]);
        Assert.True(Guid.TryParse((string)rows[0]["dmicRowId"], out _));
        Assert.Equal(DBNull.Value, rows[0]["Gestation Length At Delivery"]);
    }

    [Fact]
    public void DerivedValuesAreStableWithinARow()
    {
        using var reader = new CsvImporter(ProductionLikeOptions(TestFiles.Fixture)).OpenReader(out _);
        Assert.True(reader.Read());
        var ordinal = reader.GetOrdinal("dmicRowId");
        Assert.Equal(reader.GetValue(ordinal), reader.GetValue(ordinal));
    }

    [Fact]
    public void TrimsSkipsBlankRowsAndStripsControlCharacters()
    {
        var path = TestFiles.WriteTemp("Name,Code\r\n  alice  ,a\u0007b\r\n,\r\n\r\n bob ,  \r\n");
        try
        {
            var o = ProductionLikeOptions(path);
            o.DerivedHeaders = [];
            o.Filters = [];
            var rows = ReadAll(o, out var names);
            Assert.Equal(["Name", "Code"], names);
            Assert.Equal(2, rows.Count);
            Assert.Equal("alice", rows[0]["Name"]);
            Assert.Equal("ab", rows[0]["Code"]);
            Assert.Equal("bob", rows[1]["Name"]);
            Assert.Equal(DBNull.Value, rows[1]["Code"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CamelCasesNamesByDefault()
    {
        var path = TestFiles.WriteTemp("Patient name,Start Date (Episode)\r\nx,2026-01-01\r\n");
        try
        {
            var o = ProductionLikeOptions(path);
            o.CamelCaseColumnNames = true;
            o.DerivedHeaders = [];
            o.Filters = [];
            ReadAll(o, out var names);
            Assert.Equal(["PatientName", "StartDateEpisode"], names);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StronglyTypedModeConvertsValues()
    {
        var path = TestFiles.WriteTemp("n,d,s\r\n1,2026-01-02,x\r\n300,2026-01-03,\r\n");
        try
        {
            var o = ProductionLikeOptions(path);
            o.WeaklyTyped = false;
            o.DerivedHeaders = [];
            o.Filters = [];
            var rows = ReadAll(o, out _);
            Assert.Equal((short)300, rows[1]["n"]);
            Assert.Equal(new DateTime(2026, 1, 2), rows[0]["d"]);
            Assert.Equal(DBNull.Value, rows[1]["s"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FilterForUnknownColumnFails()
    {
        var o = ProductionLikeOptions(TestFiles.Fixture);
        o.Filters = ["noSuchColumn/Derivation?Format=1"];
        Assert.ThrowsAny<ArgumentException>(() => new CsvImporter(o).OpenReader(out _).Dispose());
    }

    [Fact]
    public void WritesProfileMatchingSsisSampleStructure()
    {
        var profile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            var result = new CsvImporter(ProductionLikeOptions(TestFiles.Fixture, profile)).Run();
            Assert.Equal(2, result.RowsWritten);

            var actual = XDocument.Load(profile);
            var sample = XDocument.Load(TestFiles.SampleProfile);
            var ns = DataProfileWriter.Ns;

            Assert.Equal(ns + "DataProfile", actual.Root!.Name);
            Assert.Equal(Shape(sample), Shape(actual));

            var bytes = File.ReadAllBytes(profile);
            Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);

            var nullProfiles = actual.Descendants(ns + "ColumnNullRatioProfile").ToList();
            Assert.Equal(63, nullProfiles.Count);
            Assert.All(nullProfiles, p => Assert.Equal("2", p.Element(ns + "Table")!.Attribute("RowCount")!.Value));

            string TypeOf(string name) => nullProfiles.Select(p => p.Element(ns + "Column")!)
                .Single(c => c.Attribute("Name")!.Value == name).Attribute("SqlDbType")!.Value;
            Assert.Equal("VarChar", TypeOf("Generated Record ID"));
            Assert.Equal("DateTime2", TypeOf("Event Date"));
            Assert.Equal("TinyInt", TypeOf("Critical Care Period Type"));
            Assert.Equal("VarChar", TypeOf("Critical Care Start Time"));

            var values = actual.Descendants(ns + "ColumnValueDistributionProfile")
                .Single(p => p.Element(ns + "Column")!.Attribute("Name")!.Value == "Generated Record ID");
            Assert.Equal("2", values.Element(ns + "NumberOfDistinctValues")!.Value);
        }
        finally
        {
            File.Delete(profile);
        }
    }

    private static List<string> Shape(XDocument doc) =>
        doc.Root!.Elements()
            .Select(e => e.Name.LocalName + ":" + string.Join(",", e.Descendants().Select(d => d.Name.LocalName).Distinct().Order(StringComparer.Ordinal)))
            .ToList();
}
