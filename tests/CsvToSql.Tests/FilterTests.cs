using CsvToSql.Core.Filters;

namespace CsvToSql.Tests;

internal sealed class FakeRow : IRowContext
{
    public long LineNumber { get; set; } = 7;

    public IReadOnlyList<string> FieldNames { get; set; } = ["Code", "Name", "Date"];

    public string?[] Raw { get; set; } = ["ABC123", "Smith", "2026-02-10"];

    public IReadOnlyDictionary<string, string> Variables { get; set; } = new Dictionary<string, string> { ["$batch"] = "B1" };

    public string? GetRawValue(int ordinal) => Raw[ordinal];
}

public class FilterTests
{
    private static readonly FakeRow Row = new();

    private static string? Apply(string definition, string? value) =>
        FilterFactory.Create(FilterDefinition.Parse(definition)).Filter(value, Row);

    [Fact]
    public void ParsesColumnNameAndParameters()
    {
        var d = FilterDefinition.Parse("dmicRowId/Derivation?Format=a=b&OnlyIfNull=true");
        Assert.Equal("dmicRowId", d.Column);
        Assert.Equal("Derivation", d.FilterName);
        Assert.Equal("a=b", d.Parameters["Format"]);
        Assert.Equal("true", d.Parameters["onlyifnull"]);
    }

    [Fact]
    public void ValuesMayContainSlashes()
    {
        var d = FilterDefinition.Parse("col/Derivation?Format=a/b?c");
        Assert.Equal("col", d.Column);
        Assert.Equal("a/b?c", d.Parameters["Format"]);
    }

    [Fact]
    public void FilterWithoutColumnAppliesToAll() =>
        Assert.Equal(string.Empty, FilterDefinition.Parse("Trim").Column);

    [Fact]
    public void LiteralDerivation() => Assert.Equal("7554", Apply("x/Derivation?Format=7554", null));

    [Fact]
    public void GuidDerivationIsNewEachCall()
    {
        var f = FilterFactory.Create(FilterDefinition.Parse("x/Derivation?Format=$$guid"));
        var a = f.Filter(null, Row);
        var b = f.Filter(null, Row);
        Assert.True(Guid.TryParse(a, out _));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void TimestampDerivationIsLocalRoundTrip()
    {
        var v = Apply("x/Derivation?Format=$$timestamp", null)!;
        var parsed = DateTime.Parse(v, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.Equal(DateTimeKind.Local, parsed.Kind);
        Assert.True((DateTime.Now - parsed).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void TemplatesMixLiteralsFunctionsAndVariables()
    {
        Assert.Equal("L7-B1-ABC", Apply("x/Derivation?Format=L$$linenumber-$batch-$$left(Code,3)", null));
        Assert.Equal("Smith", Apply("x/Derivation?Format=$1", null));
        Assert.Equal("ABC123Smith", Apply("x/Derivation?Format=$$concat(Code,Name)", null));
        Assert.Equal("2026-02-28", Apply("x/Derivation?Format=$$eomonth(Date)", null));
        Assert.Equal("X-C123", Apply("x/Derivation?Format=$$replace(Code,AB,X-)", null));
    }

    [Fact]
    public void OnlyIfNullKeepsExistingValues()
    {
        Assert.Equal("keep", Apply("x/Derivation?Format=new&OnlyIfNull=true", "keep"));
        Assert.Equal("new", Apply("x/Derivation?Format=new&OnlyIfNull=true", null));
    }

    [Theory]
    [InlineData("Trim", "  a \t", "a")]
    [InlineData("Empty", "   ", null)]
    [InlineData("NA", "N/a", null)]
    [InlineData("NULL", "NULL", null)]
    [InlineData("NULL", "null", "null")]
    [InlineData("NumericDash", "-", null)]
    [InlineData("YesNo", "Yes", "true")]
    [InlineData("YesNo", "n", "false")]
    [InlineData("CurrencySymbol", "£12.50", "12.50")]
    [InlineData("NumericComma", "1,234", "1234")]
    [InlineData("PercentageSymbol", "12.5%", "12.5")]
    [InlineData("PercentageSymbol", "abc%", "abc%")]
    [InlineData("Upper", "abc", "ABC")]
    [InlineData("Left?Length=2", "abc", "ab")]
    [InlineData("Right?Length=2", "abc", "bc")]
    [InlineData("Split?Separator=-&Index=1", "a-b-c", "b")]
    [InlineData("Replace?Mappings=a:x;b:y", "B", "y")]
    public void BasicFilters(string definition, string? input, string? expected) => Assert.Equal(expected, Apply(definition, input));

    [Fact]
    public void UnknownFilterIsRejected() =>
        Assert.Throws<ArgumentException>(() => FilterFactory.Create(FilterDefinition.Parse("col/NoSuchFilter")));

    [Fact]
    public void UnsupportedDerivationFunctionIsRejected() =>
        Assert.Throws<NotSupportedException>(() => FilterFactory.Create(FilterDefinition.Parse("col/Derivation?Format=$$lookup(a,b)")));

    [Fact]
    public void DefaultFiltersMatchLegacyTypes()
    {
        Assert.Equal(["NULL", "NumericDash"], DefaultFilters.ForType(typeof(string)).Select(f => f.Name));
        Assert.Equal(["NULL", "NumericDash", "Trim", "YesNo"], DefaultFilters.ForType(typeof(bool)).Select(f => f.Name));
        Assert.Equal(["NULL", "NumericDash", "Trim", "CurrencySymbol", "NumericComma", "PercentageSymbol"], DefaultFilters.ForType(typeof(int)).Select(f => f.Name));
    }
}
