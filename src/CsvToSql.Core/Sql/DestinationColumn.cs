namespace CsvToSql.Core.Sql;

/// <summary>A destination table column and its SQL type declaration, e.g. <c>varchar(4000)</c>.</summary>
public sealed record DestinationColumn(string Name, string SqlType, bool NotNull = false);
