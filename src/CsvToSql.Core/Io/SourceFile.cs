using System.IO.Compression;

namespace CsvToSql.Core.Io;

/// <summary>
/// Opens source files, transparently decompressing gzip input.
/// </summary>
public static class SourceFile
{
    private const int BufferSize = 1 << 16;

    /// <summary>
    /// Determines whether a source should be treated as gzip, using the explicit file type
    /// override (e.g. ".gz") if supplied, otherwise the file extension.
    /// </summary>
    public static bool IsGzip(string path, string? fileTypeOverride = null)
    {
        var ext = string.IsNullOrWhiteSpace(fileTypeOverride)
            ? Path.GetExtension(path)
            : fileTypeOverride.Trim();
        ext = ext.TrimStart('.');
        return string.Equals(ext, "gz", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, "gzip", StringComparison.OrdinalIgnoreCase);
    }

    public static Stream OpenRead(string path, bool gzip)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
        if (!gzip)
        {
            return fs;
        }

        return new BufferedStream(new GZipStream(fs, CompressionMode.Decompress, leaveOpen: false), BufferSize);
    }

    public static StreamReader OpenText(string path, bool gzip, System.Text.Encoding encoding)
    {
        return new StreamReader(OpenRead(path, gzip), encoding, detectEncodingFromByteOrderMarks: true, BufferSize);
    }
}
