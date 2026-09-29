using System.Globalization;

namespace CsvToSql.Core.Logging;

/// <summary>
/// Minimal logger: warnings and errors go to stderr, everything is appended to an optional log file.
/// </summary>
public sealed class ImportLog : IDisposable
{
    private readonly StreamWriter? _file;
    private readonly bool _verbose;
    private readonly TextWriter _console;
    private readonly Lock _sync = new();

    public ImportLog(string? filename = null, bool verbose = false, TextWriter? console = null)
    {
        _verbose = verbose;
        _console = console ?? Console.Error;
        if (!string.IsNullOrWhiteSpace(filename))
        {
            _file = new StreamWriter(filename, append: true) { AutoFlush = true };
        }
    }

    public static ImportLog Null { get; } = new(console: TextWriter.Null);

    public void Debug(string message)
    {
        if (_verbose)
        {
            Write("D", message, toConsole: false);
        }
    }

    public void Info(string message) => Write("I", message, toConsole: false);

    public void Warn(string message) => Write("W", message, toConsole: true);

    public void Error(string message) => Write("E", message, toConsole: true);

    public void Dispose() => _file?.Dispose();

    private void Write(string level, string message, bool toConsole)
    {
        lock (_sync)
        {
            if (toConsole)
            {
                _console.WriteLine(message);
            }

            _file?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.ffff}|{level}|CsvToDb|{message}"));
        }
    }
}
