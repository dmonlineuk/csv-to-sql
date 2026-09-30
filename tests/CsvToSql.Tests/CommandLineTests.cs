using CsvToSql.Cli;

namespace CsvToSql.Tests;

public class CommandLineTests
{
    internal static readonly string[] ProductionArgs =
    [
        "-i", "/fileshares/inbound/Data/Files Received/Test/x_SEM 999 CCMDS.csv",
        "--processing.columns.fromHeader",
        "-s", "axym-nep005-sql-srv.database.windows.net",
        "-d", "full-dfms-medal-pcdchecker",
        "--dest.sql.schema", "stagingUntyped",
        "-t", "DF0001152_b9073dde-8a73-4658-9038-2e9965aa29cb",
        "--fmt.sniff",
        "--fmt.sniff.yesnoasstring",
        "--fmt.headers.nocamelcase",
        "--fmt.headers.Derived", "dmicRowId", "dmicFileId", "dmicFlowId", "dmicImportLogId", "dmicDtUpdated", "dmicDtAdded",
        "--processing.weaklyTyped",
        "--processing.trim",
        "--processing.profile.filename", "/tmp/DF0001152_DataProfile.xml",
        "--processing.filters.skipEmptyRows",
        "--processing.filters",
        "dmicRowId/Derivation?Format=$$guid",
        "dmicFileId/Derivation?Format=7554",
        "dmicFlowId/Derivation?Format=1152",
        " ",
        "dmicImportLogId/Derivation?Format=b9073dde-8a73-4658-9038-2e9965aa29cb",
        "dmicDtUpdated/Derivation?Format=$$timestamp",
        "dmicDtAdded/Derivation?Format=$$timestamp",
        "--dest.sql.table.drop",
        "--dest.sql.format.textLength", "4000",
        "--ui.waitForKeyPress.no",
        "--dest.sql.auth.username", "CsvToDbUser",
        "--dest.sql.auth.password", "#RedactedPassword#",
    ];

    [Fact]
    public void ParsesTheProductionCommandLine()
    {
        var cl = LegacyCommandLine.Parse(ProductionArgs);
        var o = OptionsMapper.Map(cl, _ => null);
        var host = OptionsMapper.MapHost(cl);

        Assert.Equal("/fileshares/inbound/Data/Files Received/Test/x_SEM 999 CCMDS.csv", o.SourceFile);
        Assert.True(o.ColumnsFromHeader);
        Assert.True(o.Sniff);
        Assert.True(o.SniffYesNoAsString);
        Assert.False(o.CamelCaseColumnNames);
        Assert.Equal(["dmicRowId", "dmicFileId", "dmicFlowId", "dmicImportLogId", "dmicDtUpdated", "dmicDtAdded"], o.DerivedHeaders);
        Assert.True(o.WeaklyTyped);
        Assert.True(o.TrimValues);
        Assert.True(o.SkipEmptyRows);
        Assert.Equal("/tmp/DF0001152_DataProfile.xml", o.ProfileFilename);
        Assert.Equal(6, o.Filters.Count);
        Assert.Equal("dmicRowId/Derivation?Format=$$guid", o.Filters[0]);
        Assert.False(host.WaitForKeyPress);

        var sql = Assert.IsType<CsvToSql.Core.Import.SqlDestinationOptions>(o.Sql);
        Assert.Equal("axym-nep005-sql-srv.database.windows.net", sql.Server);
        Assert.Equal("full-dfms-medal-pcdchecker", sql.Database);
        Assert.Equal("stagingUntyped", sql.Schema);
        Assert.Equal("DF0001152_b9073dde-8a73-4658-9038-2e9965aa29cb", sql.Table);
        Assert.Equal("CsvToDbUser", sql.Username);
        Assert.Equal("#RedactedPassword#", sql.Password);
        Assert.True(sql.DropTable);
        Assert.True(sql.CreateTable);
        Assert.Equal(4000, sql.TextLength);
        Assert.False(sql.Unicode);
    }

    [Fact]
    public void OptionNamesAreCaseInsensitiveAndSupportLongAliases()
    {
        var cl = LegacyCommandLine.Parse(["--SRC.FILENAME", "a.csv", "--Dest.Sql.Server=srv", "--FMT.SNIFF.YESNOASSTRING", "-T", "tbl"]);
        var o = OptionsMapper.Map(cl, _ => null);
        Assert.Equal("a.csv", o.SourceFile);
        Assert.Equal("srv", o.Sql!.Server);
        Assert.Equal("tbl", o.Sql.Table);
        Assert.True(o.SniffYesNoAsString);
    }

    [Fact]
    public void AppliesLegacyDefaults()
    {
        var o = OptionsMapper.Map(LegacyCommandLine.Parse(["-i", "a.csv", "-s", "srv"]), _ => null);
        Assert.Equal("staging", o.Sql!.Schema);
        Assert.Equal(255, o.Sql.TextLength);
        Assert.Equal("18,10", o.Sql.DecimalSize);
        Assert.Equal(',', o.Separator);
        Assert.Equal('"', o.Delimiter);
        Assert.Equal("LNV", o.ProfileOptions);
        Assert.True(o.CamelCaseColumnNames);
        Assert.True(o.StripControlCharacters);
        Assert.True(OptionsMapper.MapHost(LegacyCommandLine.Parse(["-i", "a.csv"])).WaitForKeyPress);
    }

    [Theory]
    [InlineData("--no.such.option")]
    [InlineData("-x")]
    [InlineData("stray")]
    public void RejectsUnknownArguments(string arg) =>
        Assert.Throws<CommandLineException>(() => LegacyCommandLine.Parse(["-i", "a.csv", arg]));

    [Fact]
    public void RejectsMissingValue() =>
        Assert.Throws<CommandLineException>(() => LegacyCommandLine.Parse(["-i", "a.csv", "-s"]));

    [Fact]
    public void ReportsKnownButUnsupportedOptions()
    {
        var ex = Assert.Throws<CommandLineException>(() => OptionsMapper.Map(LegacyCommandLine.Parse(["-i", "a.csv", "-n", "--dest.console"])));
        Assert.Contains("--dest.console", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PasswordFallsBackToEnvironmentVariable()
    {
        var o = OptionsMapper.Map(
            LegacyCommandLine.Parse(["-i", "a.csv", "-s", "srv", "--dest.sql.auth.username", "u"]),
            n => n == OptionsMapper.PasswordEnvironmentVariable ? "from-env" : null);
        Assert.Equal("from-env", o.Sql!.Password);
    }

    [Fact]
    public void RequiresADestination() =>
        Assert.Throws<CommandLineException>(() => OptionsMapper.Map(LegacyCommandLine.Parse(["-i", "a.csv"])));

    [Fact]
    public void NegativeNumbersAreValues()
    {
        var cl = LegacyCommandLine.Parse(["-i", "a.csv", "-n", "--processing.limits.maxRows", "-1"]);
        Assert.Equal(-1, OptionsMapper.Map(cl).MaxRows);
    }

    [Fact]
    public void SeparatorAcceptsTabEscape()
    {
        var o = OptionsMapper.Map(LegacyCommandLine.Parse(["-i", "a.csv", "-n", "--fmt.separator", "\\t", "--fmt.delimiter.none"]));
        Assert.Equal('\t', o.Separator);
        Assert.Equal('\0', o.Delimiter);
        Assert.True(o.ForceNoDelimiter);
    }

    [Fact]
    public void VariablesAreParsedFromJson()
    {
        var o = OptionsMapper.Map(LegacyCommandLine.Parse(["-i", "a.csv", "-n", "--processing.variables", "{\"$batch\":\"42\"}"]));
        Assert.Equal("42", o.Variables["$batch"]);
    }

    [Fact]
    public void WarnsAboutBlankListValuesAndMissingSqlUsername()
    {
        var warnings = OptionsMapper.Warnings(LegacyCommandLine.Parse(
            ["-i", "a.csv", "-s", "srv", "--processing.filters", "a/Derivation?Format=1", " "]));

        Assert.Equal(2, warnings.Count);
        Assert.Contains("--processing.filters", warnings[0], StringComparison.Ordinal);
        Assert.Contains("--dest.sql.auth.username", warnings[1], StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionCommandLineHasOnlyTheBlankFilterWarning()
    {
        var warnings = OptionsMapper.Warnings(LegacyCommandLine.Parse(ProductionArgs));

        Assert.Single(warnings);
    }
}
