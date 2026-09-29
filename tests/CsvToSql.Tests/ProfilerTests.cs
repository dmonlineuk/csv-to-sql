using System.Text;
using CsvToSql.Core.Io;
using CsvToSql.Core.Profiling;

namespace CsvToSql.Tests;

public class ProfilerTests
{
    private static Type? Infer(string value, bool yesNoAsBool = true, Type? previous = null)
    {
        var p = new ColumnProfiler { TreatYesNoAsBool = yesNoAsBool, TreatDashAsNull = true, TreatCurrencyAsNumeric = true, TreatPercentageAsNumeric = true };
        return p.InferType(value, previous).Type;
    }

    [Theory]
    [InlineData("2026-09-29", typeof(DateTime))]
    [InlineData("29/09/2026", typeof(DateTime))]
    [InlineData("20260929", typeof(int))]
    [InlineData("1", typeof(byte))]
    [InlineData("300", typeof(short))]
    [InlineData("70000", typeof(int))]
    [InlineData("5000000000", typeof(long))]
    [InlineData("6.3", typeof(decimal))]
    [InlineData("CC000001", typeof(string))]
    [InlineData("08:30:00", typeof(string))]
    [InlineData("true", typeof(bool))]
    [InlineData("Y", typeof(bool))]
    public void InfersLegacyTypes(string value, Type expected) => Assert.Equal(expected, Infer(value));

    [Fact]
    public void YesNoAsStringKeepsYesNoAsText()
    {
        Assert.Equal(typeof(string), Infer("Y", yesNoAsBool: false));
        Assert.Equal(typeof(string), Infer("No", yesNoAsBool: false));
    }

    [Fact]
    public void WidensNumericTypesAndFallsBackToString()
    {
        Assert.Equal(typeof(short), typeof(byte).GetBestType(typeof(short)));
        Assert.Equal(typeof(decimal), typeof(int).GetBestType(typeof(decimal)));
        Assert.Equal(typeof(string), typeof(DateTime).GetBestType(typeof(int)));
        Assert.Equal(typeof(int), ((Type?)null).GetBestType(typeof(int)));
    }

    [Fact]
    public void StripsControlCharacters() => Assert.Equal("ab", ControlCharacters.Strip("a\u0001\u001Fb"));

    [Fact]
    public void SniffsSeparatorQuotesAndTypes()
    {
        var path = TestFiles.WriteTemp("id;name;when;flag\r\n1;\"a;b\";2026-01-02;Y\r\n2;\"c\";2026-01-03;N\r\n");
        try
        {
            var s = new CsvSniffer { TreatYesNoAsBool = true, ProfilerTypes = ProfilerType.All };
            s.Sniff(path, 1, int.MaxValue, false);
            Assert.Equal(';', s.Separator);
            Assert.Equal('"', s.Delimiter);
            Assert.True(s.HasHeaders);
            Assert.Equal(["id", "name", "when", "flag"], s.Results.Select(r => r.Name));
            Assert.Equal([typeof(byte), typeof(string), typeof(DateTime), typeof(bool)], s.Results.Select(r => r.BestDataType));
            Assert.Equal(2, s.Results[0].RowsChecked);
            Assert.Equal(3, s.Results[1].MaxLength);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("a\tb\n1\t2\n", '\t')]
    [InlineData("a|b\n1|2\n", '|')]
    [InlineData("a,b\n1,2\n", ',')]
    public void SniffsCommonSeparators(string content, char expected)
    {
        var path = TestFiles.WriteTemp(content);
        try
        {
            var s = new CsvSniffer();
            s.Sniff(path, 1, int.MaxValue, false);
            Assert.Equal(expected, s.Separator);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FailIfRaggedRejectsShortRows()
    {
        var path = TestFiles.WriteTemp("a,b,c\n1,2\n");
        try
        {
            var s = new CsvSniffer { FailIfRagged = true };
            Assert.Throws<InvalidDataException>(() => s.Sniff(path, 1, int.MaxValue, false));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DetectsEncodings()
    {
        var utf16 = TestFiles.WriteTemp("a,b\n1,2\n", new UnicodeEncoding(false, true));
        var utf8Bom = TestFiles.WriteTemp("a,b\n1,2\n", new UTF8Encoding(true));
        var utf8 = TestFiles.WriteTemp("a,b\ncafé,2\n");
        EncodingDetector.EnsureCodePages();
        var cp1252 = TestFiles.WriteTemp("a,b\ncaf\u00e9,2\n", Encoding.GetEncoding(1252));
        try
        {
            Assert.Equal(Encoding.Unicode.WebName, EncodingDetector.Detect(utf16, false).WebName);
            Assert.Equal("utf-8", EncodingDetector.Detect(utf8Bom, false).WebName);
            Assert.Equal("utf-8", EncodingDetector.Detect(utf8, false).WebName);
            Assert.Equal("windows-1252", EncodingDetector.Detect(cp1252, false).WebName);
        }
        finally
        {
            foreach (var f in new[] { utf16, utf8Bom, utf8, cp1252 })
            {
                File.Delete(f);
            }
        }
    }

    [Fact]
    public void ReadsGzipInput()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv.gz");
        using (var fs = File.Create(path))
        using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Fastest))
        {
            gz.Write(Encoding.UTF8.GetBytes("x,y\n1,2\n3,4\n"));
        }

        try
        {
            Assert.True(SourceFile.IsGzip(path));
            var s = new CsvSniffer();
            Assert.Equal(2, s.Sniff(path, 1, int.MaxValue, true));
            Assert.Equal(["x", "y"], s.Results.Select(r => r.Name));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
