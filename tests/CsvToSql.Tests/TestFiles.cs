using System.Text;

namespace CsvToSql.Tests;

internal static class TestFiles
{
    public static string DataPath(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    public static string Fixture => DataPath("SEM_999_CCMDS.csv");

    public static string SampleProfile => DataPath("SEM_999_CCMDS_DataProfile.xml");

    public static string WriteTemp(string content, Encoding? encoding = null, string extension = ".csv")
    {
        var path = Path.Combine(Path.GetTempPath(), "csvtosql-" + Guid.NewGuid().ToString("N") + extension);
        File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false));
        return path;
    }
}
