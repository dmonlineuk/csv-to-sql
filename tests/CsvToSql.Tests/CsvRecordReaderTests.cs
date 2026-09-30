using CsvToSql.Core.Csv;

namespace CsvToSql.Tests;

public class CsvRecordReaderTests
{
    private static List<string[]> ReadAll(string text, char sep = ',', char? delim = '"', char? escape = null, bool onlyEscapeDelimiter = false)
    {
        using var r = new CsvRecordReader(new StringReader(text), sep, delim, escape, onlyEscapeDelimiter);
        var rows = new List<string[]>();
        while (r.ReadRecord() is { } rec)
        {
            rows.Add(rec);
        }

        return rows;
    }

    [Fact]
    public void ReadsSimpleRecordsWithMixedLineEndings()
    {
        var rows = ReadAll("a,b,c\r\n1,2,3\n4,5,6\r7,8,9");
        Assert.Equal(4, rows.Count);
        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["7", "8", "9"], rows[3]);
    }

    [Fact]
    public void HandlesQuotedSeparatorsNewlinesAndDoubledQuotes()
    {
        var rows = ReadAll("\"a,b\",\"line1\nline2\",\"say \"\"hi\"\"\"\r\n");
        Assert.Single(rows);
        Assert.Equal(["a,b", "line1\nline2", "say \"hi\""], rows[0]);
    }

    [Fact]
    public void EmptyFieldsAndTrailingSeparator()
    {
        var rows = ReadAll("a,,c,\r\n");
        Assert.Equal(["a", "", "c", ""], rows[0]);
    }

    [Fact]
    public void BlankLineIsSingleEmptyValue()
    {
        var rows = ReadAll("a,b\r\n\r\nc,d\r\n");
        Assert.Equal(3, rows.Count);
        Assert.Equal([""], rows[1]);
    }

    [Fact]
    public void KeepsWhitespaceInsideQuotesButNotOutside()
    {
        var rows = ReadAll("  \"  x  \"  ,  y  \n");
        Assert.Equal("  x  ", rows[0][0]);
    }

    [Fact]
    public void SupportsOtherSeparatorsAndNoQuoting()
    {
        var rows = ReadAll("a|\"b\"|c\n", '|', null);
        Assert.Equal(["a", "\"b\"", "c"], rows[0]);
    }

    [Fact]
    public void SupportsEscapeCharacter()
    {
        var rows = ReadAll("\"a\\\"b\",c\\,d\n", escape: '\\');
        Assert.Equal(["a\"b", "c,d"], rows[0]);
    }

    [Fact]
    public void OnlyEscapeDelimiterLeavesOtherBackslashes()
    {
        var rows = ReadAll("\"a\\\"b\\n\"\n", escape: '\\', onlyEscapeDelimiter: true);
        Assert.Equal(["a\"b\\n"], rows[0]);
    }

    [Fact]
    public void StreamsRecordsLongerThanTheBuffer()
    {
        var big = new string('x', 20_000);
        var rows = ReadAll($"\"{big}\",\"{big}\"\"\"\r\n1,2\r\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal(big, rows[0][0]);
        Assert.Equal(big + "\"", rows[0][1]);
        Assert.Equal(["1", "2"], rows[1]);
    }

    [Fact]
    public void CountsRecords()
    {
        using var r = new CsvRecordReader(new StringReader("a\nb\nc"));
        while (r.ReadRecord() is not null)
        {
        }

        Assert.Equal(3, r.RecordNumber);
    }
}
