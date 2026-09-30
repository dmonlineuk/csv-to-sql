using CsvToSql.Core.Import;
using CsvToSql.Core.Sql;

namespace CsvToSql.Tests;

public class SqlDdlTests
{
    [Theory]
    [InlineData("plain", "[plain]")]
    [InlineData("with space", "[with space]")]
    [InlineData("evil]; DROP TABLE x;--", "[evil]]; DROP TABLE x;--]")]
    public void QuotesIdentifiers(string name, string expected) => Assert.Equal(expected, SqlIdentifier.Quote(name));

    [Fact]
    public void RejectsOverlongIdentifiers() => Assert.Throws<ArgumentException>(() => SqlIdentifier.Quote(new string('a', 129)));

    [Fact]
    public void CreateTableQuotesEveryName()
    {
        var sql = SqlDdl.CreateTableSql("stagingUntyped", "DF0001152_b9073dde", [new("Generated Record ID", "varchar(4000)"), new("a]b", "int")], false);
        Assert.Equal(
            "IF OBJECT_ID(@object) IS NULL CREATE TABLE [stagingUntyped].[DF0001152_b9073dde] ([Generated Record ID] varchar(4000), [a]]b] int)",
            sql);
    }

    [Fact]
    public void WeaklyTypedColumnsAreText()
    {
        var f = new FieldSpec { FieldName = "n", FieldType = typeof(int), ColumnIndex = 0 };
        Assert.Equal("varchar(4000)", SqlDdl.SqlTypeFor(f, stronglyTyped: false, unicode: false, 4000, "18,10"));
        Assert.Equal("nvarchar(4000)", SqlDdl.SqlTypeFor(f, stronglyTyped: false, unicode: true, 4000, "18,10"));
        Assert.Equal("varchar(max)", SqlDdl.SqlTypeFor(f, stronglyTyped: false, unicode: false, -1, "18,10"));
    }

    [Fact]
    public void StronglyTypedColumnsUseInferredTypes()
    {
        string T(Type t, int? size = null) => SqlDdl.SqlTypeFor(new FieldSpec { FieldName = "c", FieldType = t, Size = size, ColumnIndex = 0 }, true, false, 255, "18,10");
        Assert.Equal("tinyint", T(typeof(byte)));
        Assert.Equal("datetime2", T(typeof(DateTime)));
        Assert.Equal("decimal(18,10)", T(typeof(decimal)));
        Assert.Equal("bit", T(typeof(bool)));
        Assert.Equal("varchar(12)", T(typeof(string), 12));
        Assert.Equal("varchar(255)", T(typeof(string), 0));
    }

    [Fact]
    public void RejectsInvalidDecimalSize() =>
        Assert.Throws<ArgumentException>(() => SqlDdl.SqlTypeFor(new FieldSpec { FieldName = "c", FieldType = typeof(decimal), ColumnIndex = 0 }, true, false, 255, "18);drop"));

    [Fact]
    public void SqlAuthConnectionKeepsPasswordOutOfConnectionString()
    {
        var o = new SqlDestinationOptions { Server = "srv.database.windows.net", Database = "db", Username = "CsvToDbUser", Password = "s3cret!" };
        var cs = SqlConnectionFactory.BuildConnectionString(o);
        Assert.DoesNotContain("s3cret", cs, StringComparison.Ordinal);
        Assert.Contains("Integrated Security=False", cs, StringComparison.Ordinal);
        Assert.Contains("Initial Catalog=db", cs, StringComparison.Ordinal);
        var cred = SqlConnectionFactory.BuildCredential(o)!;
        Assert.Equal("CsvToDbUser", cred.UserId);
        Assert.Equal(7, cred.Password.Length);
    }

    [Fact]
    public void NoUsernameMeansIntegratedSecurity()
    {
        var o = new SqlDestinationOptions { Server = "srv" };
        Assert.Contains("Integrated Security=True", SqlConnectionFactory.BuildConnectionString(o), StringComparison.Ordinal);
        Assert.Null(SqlConnectionFactory.BuildCredential(o));
    }
}
