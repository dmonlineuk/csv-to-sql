using System.Text;

namespace CsvToSql.Core.Csv;

/// <summary>
/// Streaming CSV record parser. Mirrors the semantics of the KBCsv build used by the legacy tool:
/// <list type="bullet">
/// <item>unquoted leading/trailing spaces and tabs are trimmed, quoted content is preserved;</item>
/// <item>a delimiter (quote) character may open a quoted section anywhere in a value;</item>
/// <item>doubled delimiters inside a quoted section yield a literal delimiter;</item>
/// <item>an optional escape character makes the next character literal (optionally only when it
/// precedes the delimiter);</item>
/// <item>CR, LF and CRLF all terminate a record; an empty line yields a single empty value.</item>
/// </list>
/// </summary>
public sealed class CsvRecordReader : IDisposable
{
    private const int BufferSize = 1 << 16;

    private readonly TextReader _reader;
    private readonly char[] _buffer = new char[BufferSize];
    private readonly StringBuilder _value = new();
    private readonly List<string> _values = new(64);
    private int _bufferIndex;
    private int _bufferLength;
    private int? _delimitedStart;
    private int? _delimitedEnd;

    public CsvRecordReader(TextReader reader, char separator = ',', char? delimiter = '"', char? escape = null, bool onlyEscapeDelimiter = false)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (delimiter == '\0')
        {
            delimiter = null;
        }

        if (delimiter == separator)
        {
            throw new ArgumentException("The value separator and delimiter cannot be the same character.");
        }

        if (escape.HasValue && escape == separator)
        {
            throw new ArgumentException("The value separator and escape character cannot be the same character.");
        }

        _reader = reader;
        Separator = separator;
        Delimiter = delimiter;
        Escape = escape;
        OnlyEscapeDelimiter = onlyEscapeDelimiter;
    }

    public char Separator { get; }

    public char? Delimiter { get; }

    public char? Escape { get; }

    public bool OnlyEscapeDelimiter { get; }

    /// <summary>Number of records returned so far.</summary>
    public long RecordNumber { get; private set; }

    /// <summary>Reads the next record, or returns null at end of input.</summary>
    public string[]? ReadRecord()
    {
        _values.Clear();
        ResetValue();
        var inQuotes = false;
        var escapeNext = false;
        var consumedAny = false;

        while (true)
        {
            if (_bufferIndex >= _bufferLength && !Fill())
            {
                if (!consumedAny)
                {
                    return null;
                }

                if (escapeNext && Escape.HasValue)
                {
                    Append(Escape.Value, inQuotes);
                }

                return Complete();
            }

            var c = _buffer[_bufferIndex++];
            consumedAny = true;

            if (escapeNext)
            {
                Append(c, delimited: true);
                escapeNext = false;
                continue;
            }

            if (Escape.HasValue && c == Escape.Value)
            {
                if (OnlyEscapeDelimiter)
                {
                    var next = Peek();
                    if (next < 0 || next != Delimiter)
                    {
                        Append(c, inQuotes);
                        continue;
                    }
                }

                escapeNext = true;
                continue;
            }

            if (!inQuotes)
            {
                if (c == Separator)
                {
                    _values.Add(TakeValue());
                    continue;
                }

                if (Delimiter.HasValue && c == Delimiter.Value)
                {
                    inQuotes = true;
                    continue;
                }

                if (c == '\r')
                {
                    if (Peek() == '\n')
                    {
                        _bufferIndex++;
                    }

                    return Complete();
                }

                if (c == '\n')
                {
                    return Complete();
                }

                Append(c, delimited: false);
                continue;
            }

            if (c == Delimiter)
            {
                if (Peek() == Delimiter)
                {
                    _bufferIndex++;
                    Append(c, delimited: true);
                }
                else
                {
                    inQuotes = false;
                }

                continue;
            }

            Append(c, delimited: true);
        }
    }

    public void Dispose() => _reader.Dispose();

    private string[] Complete()
    {
        _values.Add(TakeValue());
        RecordNumber++;
        return _values.ToArray();
    }

    private int Peek()
    {
        if (_bufferIndex >= _bufferLength && !Fill())
        {
            return -1;
        }

        return _buffer[_bufferIndex];
    }

    private bool Fill()
    {
        _bufferLength = _reader.Read(_buffer, 0, _buffer.Length);
        _bufferIndex = 0;
        return _bufferLength > 0;
    }

    private void Append(char c, bool delimited)
    {
        _value.Append(c);
        if (delimited)
        {
            _delimitedStart ??= _value.Length - 1;
            _delimitedEnd = _value.Length;
        }
    }

    private void ResetValue()
    {
        _value.Clear();
        _delimitedStart = null;
        _delimitedEnd = null;
    }

    private string TakeValue()
    {
        var end = _value.Length;
        var start = 0;
        var leadLimit = _delimitedStart ?? end;
        while (start < leadLimit && IsWhiteSpace(_value[start]))
        {
            start++;
        }

        var trailLimit = _delimitedEnd ?? start;
        while (end > trailLimit && IsWhiteSpace(_value[end - 1]))
        {
            end--;
        }

        var s = end > start ? _value.ToString(start, end - start) : string.Empty;
        ResetValue();
        return s;
    }

    private static bool IsWhiteSpace(char c) => c == ' ' || c == '\t';
}
