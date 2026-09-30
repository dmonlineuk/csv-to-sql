# csv-to-sql

A drop-in replacement for the legacy `CsvToDb` utility (from `zDataLoading`): it streams CSV files into
Microsoft SQL Server tables using SQL authentication, and writes SSIS-compatible Data Profile XML.

* .NET 10, published as a single self-contained `CsvToDb` binary (no runtime needed on the server).
* Existing `CsvToDb` command lines work unchanged. Option names are case-insensitive, as before.
* Rows are streamed from the file through `SqlBulkCopy`, so the file never has to fit in memory.

## Build, test and publish

Requires the .NET 10 SDK (`global.json`).

```bash
dotnet build
dotnet test
scripts/publish.sh            # -> artifacts/linux-x64/CsvToDb  (pass another RID, e.g. linux-arm64, if needed)
```

SQL Server integration tests are skipped unless `CSVTOSQL_TEST_SQL_SERVER`, `CSVTOSQL_TEST_SQL_USER` and
`CSVTOSQL_TEST_SQL_PASSWORD` are set. `scripts/start-test-sql.sh` starts a local SQL Server 2022 container with a
SQL login and prints the variables to export:

```bash
eval "$(scripts/start-test-sql.sh)"
dotnet test
```

## Usage

```bash
CsvToDb -i "/path/file.csv" --processing.columns.fromHeader \
  -s myserver.database.windows.net -d mydb --dest.sql.schema stagingUntyped -t MyTable \
  --fmt.sniff --fmt.sniff.yesnoasstring --fmt.headers.nocamelcase \
  --fmt.headers.Derived dmicRowId dmicDtAdded \
  --processing.filters "dmicRowId/Derivation?Format=\$\$guid" "dmicDtAdded/Derivation?Format=\$\$timestamp" \
  --processing.weaklyTyped --processing.trim --processing.filters.skipEmptyRows \
  --processing.profile.filename /path/profile.xml \
  --dest.sql.table.drop --dest.sql.format.textLength 4000 --ui.waitForKeyPress.no \
  --dest.sql.auth.username CsvToDbUser
```

`CsvToDb --help` lists every option. If `--dest.sql.auth.username` is given without `--dest.sql.auth.password`, the
password is read from the `CSVTODB_SQL_PASSWORD` environment variable, which keeps it out of `ps` output.

Exit codes: `0` success, `1` import failed (details on stderr / in the log), `2` invalid command line. With
`--dest.return.rowcount` the exit code is the number of rows imported, as in the legacy tool.

### Behaviour notes

* `--fmt.sniff` detects encoding (BOM, UTF-8, else Windows-1252), separator, quote character and column types
  (bool, date, byte/short/int/long, decimal, else text). `--fmt.sniff.yesNoAsString` stops Yes/No/Y/N becoming bool.
  As in the legacy tool, `HH:mm:ss` times are profiled as text.
* `--processing.weaklyTyped` creates every column as `varchar(textLength)` (`nvarchar` with
  `--dest.sql.format.unicode`; `-1` means `max`). Otherwise the sniffed SQL types are used.
* Column names come from the header. By default they are "camel cased" as in the legacy tool (`Start Date (Episode)` →
  `StartDateEpisode`); `--fmt.headers.nocamelcase` keeps them as they are, with `[` and `]` removed.
* `--processing.trim` trims values and turns empty strings and `N/A` into NULL. Control characters (U+0000–U+001F)
  are removed unless `--processing.noremovectrlchars` is given.
* Filters take the form `[column/]Name[?Param=value&...]`. A filter without a column applies to every column, and a
  filter for a column that doesn't exist is ignored with a warning, matching the legacy tool.
* `Derivation?Format=` accepts a literal, or a template using `$$guid` (new per row), `$$sequentialguid`,
  `$$timestamp` (local time, ISO 8601 round-trip), `$$current`, `$$linenumber`, `$0`/`$column`, `$variable` (from
  `--processing.variables '{"$name":"value"}'`), and `$$replace`, `$$replaceic`, `$$regex`, `$$concat`, `$$left`,
  `$$right`, `$$substring`, `$$equal`, `$$notequal`, `$$year`, `$$month`, `$$day`, `$$eomonth`, `$$guidfromnumber`.
  Function and variable names include `-` and `.`, so write `$$linenumber_x` rather than `$$linenumber-x`.
* Schema, table and column names are quoted in all generated SQL. `--dest.sql.table.drop` drops and recreates the
  table; `--dest.sql.table.truncate` empties it; `--dest.sql.table.nocreate` requires it to already exist.
* The profile (`--processing.profile.filename`, options `LNV` = length, null ratio, value distribution) follows the
  SSIS Data Profiling Task XML format and opens in the SSIS Data Profile Viewer.

### Not implemented yet

These options are recognised, but the tool stops with a "not supported yet" error if you use them: folder input
(`-f`, `-p`, include/exclude), file and console output (`-o`, `--dest.console`, ...), searches, column mappings,
unpivot, filename/folder/creation-time fields, import-log entries, measures, `fmt.fromTable`, and
`processing.profile.typeFilename`. The `$$lookup`, `$$datediff` and `$$hash*` derivation functions are also not
supported yet.

## Layout

* `src/CsvToSql.Core`: CSV reader, sniffer/profiler, filters, streaming `DbDataReader`, SQL DDL and bulk import.
* `src/CsvToSql.Cli`: the `CsvToDb` executable and the legacy command-line shim (`LegacyCommandLine`, `OptionsMapper`).
* `tests/CsvToSql.Tests`: unit tests and SQL Server integration tests. `TestData` holds the sample CCMDS CSV and the
  sample SSIS profile.
