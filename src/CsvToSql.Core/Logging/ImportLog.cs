using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CsvToSql.Core.Logging;

/// <summary>
/// Minimal logger: warnings and errors go to stderr, everything is appended to an optional log file.
/// Several processes can share the log file: each line is appended with a single write.
/// </summary>
public sealed partial class ImportLog : IDisposable
{
    private readonly FileStream? _file;
    private readonly bool _verbose;
    private readonly TextWriter _console;
    private readonly Lock _sync = new();

    public ImportLog(string? filename = null, bool verbose = false, TextWriter? console = null)
    {
        _verbose = verbose;
        _console = console ?? Console.Error;
        if (!string.IsNullOrWhiteSpace(filename))
        {
            _file = OpenAppend(filename);
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

            if (_file is not null)
            {
                _file.Write(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.ffff}|{level}|CsvToDb|{message}{Environment.NewLine}")));
                _file.Flush();
            }
        }
    }

    /// <summary>
    /// Opens the file for appending. On Linux it uses O_APPEND, so lines written by concurrent runs never overwrite
    /// each other (FileMode.Append only seeks to the end once, when the file is opened).
    /// </summary>
    private static FileStream OpenAppend(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0);
        }

        const int oWrOnly = 0x1, oCreat = 0x40, oAppend = 0x400, oCloExec = 0x80000, mode = 0x1B6; // 0666, less umask
        var fd = Open(Path.GetFullPath(path), oWrOnly | oCreat | oAppend | oCloExec, mode);
        if (fd < 0)
        {
            throw new IOException($"Cannot open log file '{path}': {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}");
        }

        return new FileStream(new SafeFileHandle(fd, ownsHandle: true), FileAccess.Write, bufferSize: 0);
    }

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string path, int flags, int mode);
}
