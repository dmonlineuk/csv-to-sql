using System.Text;

namespace CsvToSql.Core.Io;

/// <summary>
/// Detects the text encoding of a source file: byte order marks first, then a UTF-8 heuristic,
/// falling back to Windows-1252.
/// </summary>
public static class EncodingDetector
{
    private const int Utf8SampleSize = 10 * 1024 * 1024;

    static EncodingDetector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding Windows1252 => Encoding.GetEncoding(1252);

    /// <summary>Ensures legacy code pages such as Windows-1252 are available (runs the static constructor).</summary>
    public static void EnsureCodePages()
    {
    }

    public static Encoding Detect(string path, bool gzip)
    {
        var bom = new byte[4];
        int read;
        using (var s = SourceFile.OpenRead(path, gzip))
        {
            read = ReadFully(s, bom, 0, bom.Length);
        }

        var fromBom = FromByteOrderMark(bom, read);
        if (fromBom is not null)
        {
            return fromBom;
        }

        using var sample = SourceFile.OpenRead(path, gzip);
        var buffer = new byte[Utf8SampleSize];
        var len = ReadFully(sample, buffer, 0, buffer.Length);
        return LooksLikeUtf8(buffer, len) ? Encoding.UTF8 : Windows1252;
    }

    public static Encoding? FromByteOrderMark(byte[] bom, int length)
    {
        if (length >= 4 && bom[0] == 0xFF && bom[1] == 0xFE && bom[2] == 0 && bom[3] == 0)
        {
            return Encoding.UTF32;
        }

        if (length >= 4 && bom[0] == 0 && bom[1] == 0 && bom[2] == 0xFE && bom[3] == 0xFF)
        {
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        }

        if (length >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        if (length >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        if (length >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        return null;
    }

    /// <summary>
    /// Heuristic UTF-8 detection. The sample is treated as UTF-8 when it contains at least one valid
    /// multi-byte UTF-8 sequence and valid sequences are at least as common as invalid bytes (so a
    /// UTF-8 file with the odd corrupt byte is still read as UTF-8). Pure 7-bit ASCII is reported as
    /// not UTF-8 and read as Windows-1252, which is identical for ASCII.
    /// </summary>
    public static bool LooksLikeUtf8(byte[] buffer, int length)
    {
        var valid = 0;
        var invalid = 0;
        var i = 0;
        while (i < length)
        {
            var expected = LeadLength(buffer[i]);
            if (expected == 1)
            {
                i++;
                continue;
            }

            if (expected > 1 && i + expected > length)
            {
                break;
            }

            var n = SequenceLength(buffer, i, length);
            if (n > 1)
            {
                valid++;
                i += n;
            }
            else
            {
                invalid++;
                i++;
            }
        }

        return valid > 0 && valid >= invalid;
    }

    private static int LeadLength(byte lead)
    {
        if (lead < 0x80)
        {
            return 1;
        }

        if (lead >= 0xC2 && lead <= 0xDF)
        {
            return 2;
        }

        if (lead >= 0xE0 && lead <= 0xEF)
        {
            return 3;
        }

        if (lead >= 0xF0 && lead <= 0xF4)
        {
            return 4;
        }

        return 0;
    }

    private static int SequenceLength(byte[] b, int i, int length)
    {
        var n = LeadLength(b[i]);
        if (n <= 1)
        {
            return n;
        }

        if (i + n > length)
        {
            return 0;
        }

        for (var j = 1; j < n; j++)
        {
            if ((b[i + j] & 0xC0) != 0x80)
            {
                return 0;
            }
        }

        return n;
    }

    private static int ReadFully(Stream s, byte[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var r = s.Read(buffer, offset + total, count - total);
            if (r == 0)
            {
                break;
            }

            total += r;
        }

        return total;
    }
}
