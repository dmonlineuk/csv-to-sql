using System.Collections;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CsvToSql.Core.Csv;
using CsvToSql.Core.Filters;
using CsvToSql.Core.Profiling;

namespace CsvToSql.Core.Import;

/// <summary>
/// Streams CSV records as a <see cref="DbDataReader"/> suitable for <c>SqlBulkCopy</c>, applying
/// trimming, filters, derivations and (optionally) type conversion to each value. Values are computed
/// once per row, so per-row functions such as <c>$$guid</c> are stable across repeated reads.
/// </summary>
[SuppressMessage("Naming", "CA1010", Justification = "DbDataReader only exposes the non-generic enumerator.")]
public sealed class CsvDataReader : DbDataReader, IRowContext
{
    private readonly CsvRecordReader _reader;
    private readonly IReadOnlyList<FieldSpec> _fields;
    private readonly string[] _names;
    private readonly string[] _filterFieldNames;
    private readonly object?[] _values;
    private readonly bool[] _computed;
    private string[]? _record;
    private bool _closed;
    private long _dataRowsRead;
    private long _rowsReturned;

    public CsvDataReader(CsvRecordReader reader, IReadOnlyList<FieldSpec> fields, IReadOnlyList<string> destinationNames)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(destinationNames);
        if (fields.Count != destinationNames.Count)
        {
            throw new ArgumentException("Each field needs a destination name.", nameof(destinationNames));
        }

        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _fields = fields;
        _names = destinationNames.ToArray();
        _filterFieldNames = fields.Select(f => f.CamelName).ToArray();
        _values = new object?[fields.Count];
        _computed = new bool[fields.Count];
    }

    public bool StronglyTyped { get; init; }

    public bool WeaklyTyped { get; init; }

    public bool RawValues { get; init; }

    public bool TrimValues { get; init; }

    public bool SkipBlankRows { get; init; }

    public bool StripControlCharacters { get; init; } = true;

    public bool IgnoreTypeConversionErrors { get; init; }

    public int RowsToSkip { get; init; } = -1;

    public int MaxRowsToRead { get; init; } = -1;

    public NumberStyles IntegerParsingStyle { get; init; } = ColumnProfiler.DefaultIntegerParsingStyle;

    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();

    public long RowsRead => _rowsReturned;

    public long LineNumber => _reader.RecordNumber;

    IReadOnlyList<string> IRowContext.FieldNames => _filterFieldNames;

    public IReadOnlyList<FieldSpec> Fields => _fields;

    public override int FieldCount => _fields.Count;

    public override bool HasRows => true;

    public override bool IsClosed => _closed;

    public override int RecordsAffected => -1;

    public override int Depth => 0;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public string? GetRawValue(int ordinal)
    {
        var f = _fields[ordinal];
        if (f.Derived || _record is null || f.ColumnIndex >= _record.Length)
        {
            return null;
        }

        return _record[f.ColumnIndex];
    }

    public override bool Read()
    {
        if (_closed || (MaxRowsToRead >= 0 && _rowsReturned >= MaxRowsToRead))
        {
            return false;
        }

        while (true)
        {
            var record = _reader.ReadRecord();
            if (record is null)
            {
                _record = null;
                return false;
            }

            if (StripControlCharacters)
            {
                ControlCharacters.StripInPlace(record);
            }

            if (SkipBlankRows && record.All(string.IsNullOrEmpty))
            {
                continue;
            }

            _dataRowsRead++;
            if (RowsToSkip > 0 && _dataRowsRead <= RowsToSkip)
            {
                continue;
            }

            _record = record;
            Array.Clear(_computed);
            Array.Clear(_values);
            _rowsReturned++;
            return true;
        }
    }

    public override object GetValue(int ordinal)
    {
        if (_record is null)
        {
            throw new InvalidOperationException("No row has been read.");
        }

        if (!_computed[ordinal])
        {
            _values[ordinal] = ComputeValue(ordinal);
            _computed[ordinal] = true;
        }

        return _values[ordinal] ?? DBNull.Value;
    }

    private object? ComputeValue(int ordinal)
    {
        var f = _fields[ordinal];
        var raw = GetRawValue(ordinal);
        var value = raw;
        if (TrimValues && value is not null)
        {
            value = value.Trim();
        }

        foreach (var filter in f.Filters)
        {
            if (value is not null || f.Derived)
            {
                value = filter.Filter(value, this);
            }
        }

        if (RawValues)
        {
            return raw;
        }

        if (StronglyTyped && !WeaklyTyped)
        {
            try
            {
                return ValueConverter.Convert(value, f.FieldType, IntegerParsingStyle);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                if (IgnoreTypeConversionErrors)
                {
                    return null;
                }

                throw new FormatException(
                    string.Create(CultureInfo.InvariantCulture, $"Line {LineNumber}, column '{f.FieldName}': cannot convert '{value}' to {f.FieldType.Name}."), ex);
            }
        }

        return value;
    }

    public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;

    public override int GetValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var n = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < n; i++)
        {
            values[i] = GetValue(i);
        }

        return n;
    }

    public override string GetName(int ordinal) => _names[ordinal];

    public override int GetOrdinal(string name)
    {
        var i = Array.IndexOf(_names, name);
        if (i < 0)
        {
            i = Array.FindIndex(_names, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        }

#pragma warning disable CA2201 // IDataRecord.GetOrdinal is specified to throw IndexOutOfRangeException
        return i >= 0 ? i : throw new IndexOutOfRangeException($"No column named '{name}'.");
#pragma warning restore CA2201
    }

    public override Type GetFieldType(int ordinal) =>
        StronglyTyped && !WeaklyTyped && !RawValues ? _fields[ordinal].FieldType : typeof(string);

    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

    public override bool NextResult() => false;

    public override void Close()
    {
        if (!_closed)
        {
            _closed = true;
            _reader.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }

        base.Dispose(disposing);
    }

    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);

    public override byte GetByte(int ordinal) => System.Convert.ToByte(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
        throw new NotSupportedException();

    public override char GetChar(int ordinal) => GetString(ordinal)[0];

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var s = GetString(ordinal);
        if (buffer is null)
        {
            return s.Length;
        }

        var n = (int)Math.Max(0, Math.Min(length, s.Length - dataOffset));
        s.CopyTo((int)dataOffset, buffer, bufferOffset, n);
        return n;
    }

    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);

    public override decimal GetDecimal(int ordinal) => System.Convert.ToDecimal(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override double GetDouble(int ordinal) => System.Convert.ToDouble(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override float GetFloat(int ordinal) => System.Convert.ToSingle(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override Guid GetGuid(int ordinal) => GetValue(ordinal) is Guid g ? g : Guid.Parse(GetString(ordinal));

    public override short GetInt16(int ordinal) => System.Convert.ToInt16(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override int GetInt32(int ordinal) => System.Convert.ToInt32(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override long GetInt64(int ordinal) => System.Convert.ToInt64(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override string GetString(int ordinal) => System.Convert.ToString(GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;
}
