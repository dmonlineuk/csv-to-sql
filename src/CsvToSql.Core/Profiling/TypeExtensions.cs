using System.Data;

namespace CsvToSql.Core.Profiling;

public static class TypeExtensions
{
    public static bool IsNumericType(this Type? t)
    {
        return t == typeof(byte) || t == typeof(short) || t == typeof(int) || t == typeof(long)
            || t == typeof(decimal) || t == typeof(float) || t == typeof(double)
            || t == typeof(sbyte) || t == typeof(ushort) || t == typeof(uint) || t == typeof(ulong);
    }

    /// <summary>
    /// Widens two inferred column types into one that can hold both (legacy semantics).
    /// </summary>
    public static Type? GetBestType(this Type? t, Type? ot)
    {
        if (t is null)
        {
            return ot;
        }

        if (ot is null || t == ot)
        {
            return t;
        }

        if (!t.IsNumericType() || !ot.IsNumericType())
        {
            return typeof(string);
        }

        foreach (var candidate in new[] { typeof(double), typeof(float), typeof(decimal), typeof(long), typeof(int), typeof(short), typeof(byte) })
        {
            if (t == candidate || ot == candidate)
            {
                return candidate;
            }
        }

        return t;
    }

    public static SqlDbType GetSqlDbType(this Type t, bool useNvarchar = false)
    {
        ArgumentNullException.ThrowIfNull(t);
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t == typeof(long))
        {
            return SqlDbType.BigInt;
        }

        if (t == typeof(byte[]))
        {
            return SqlDbType.Binary;
        }

        if (t == typeof(bool))
        {
            return SqlDbType.Bit;
        }

        if (t == typeof(string))
        {
            return useNvarchar ? SqlDbType.NVarChar : SqlDbType.VarChar;
        }

        if (t == typeof(char))
        {
            return SqlDbType.Char;
        }

        if (t == typeof(decimal))
        {
            return SqlDbType.Decimal;
        }

        if (t == typeof(DateTime))
        {
            return SqlDbType.DateTime2;
        }

        if (t == typeof(DateOnly))
        {
            return SqlDbType.Date;
        }

        if (t == typeof(TimeSpan) || t == typeof(TimeOnly))
        {
            return SqlDbType.Time;
        }

        if (t == typeof(double))
        {
            return SqlDbType.Real;
        }

        if (t == typeof(float))
        {
            return SqlDbType.Float;
        }

        if (t == typeof(Guid))
        {
            return SqlDbType.UniqueIdentifier;
        }

        if (t == typeof(int))
        {
            return SqlDbType.Int;
        }

        if (t == typeof(short))
        {
            return SqlDbType.SmallInt;
        }

        if (t == typeof(byte))
        {
            return SqlDbType.TinyInt;
        }

        return SqlDbType.Variant;
    }
}
