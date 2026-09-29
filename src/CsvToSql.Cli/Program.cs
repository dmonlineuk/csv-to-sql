using System.Globalization;
using System.Reflection;
using CsvToSql.Cli;
using CsvToSql.Core.Filters;
using CsvToSql.Core.Import;
using CsvToSql.Core.Logging;

return CliApp.Run(args, Console.Out, Console.Error);

internal static class CliApp
{
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        ParsedCommandLine cl;
        try
        {
            cl = LegacyCommandLine.Parse(args);
        }
        catch (CommandLineException ex)
        {
            stderr.WriteLine(ex.Message);
            stderr.WriteLine(Usage());
            return 2;
        }

        if (cl.Version)
        {
            stdout.WriteLine(typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
            return 0;
        }

        if (cl.Help || !cl.Has("src.filename"))
        {
            stdout.WriteLine(Usage());
            return cl.Help ? 0 : 2;
        }

        var host = OptionsMapper.MapHost(cl);
        var exitCode = 0;
        using (var log = new ImportLog(host.LogFilename, host.Verbose, stderr))
        {
            void OnWarning(string m) => log.Warn(m);
            FilterFactory.Warning += OnWarning;
            try
            {
                var options = OptionsMapper.Map(cl);
                log.Info($"Processing file {options.SourceFile}");
                var result = new CsvImporter(options, log).Run();
                if (options.DestNone && !options.ProfileOnly)
                {
                    stdout.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Validated {result.RowsWritten} records"));
                }
                else if (options.Sql is not null)
                {
                    log.Info(string.Create(CultureInfo.InvariantCulture, $"Imported {result.RowsWritten} rows"));
                }

                if (host.ReturnRowCount)
                {
                    exitCode = (int)Math.Min(result.RowsWritten, int.MaxValue);
                }
            }
            catch (CommandLineException ex)
            {
                stderr.WriteLine(ex.Message);
                exitCode = 2;
            }
#pragma warning disable CA1031 // top level handler: report and return a failure exit code
            catch (Exception ex)
#pragma warning restore CA1031
            {
                log.Error($"Failed file: {cl.Value("src.filename")}");
                log.Error(ex.ToString());
                exitCode = 1;
            }
            finally
            {
                FilterFactory.Warning -= OnWarning;
            }
        }

        if (host.WaitForKeyPress && !Console.IsInputRedirected)
        {
            stdout.WriteLine();
            stdout.WriteLine("Press any key");
            Console.ReadKey();
        }

        return exitCode;
    }

    public static string Usage()
    {
        var lines = new List<string> { "Usage: CsvToDb -i <file.csv> -s <server> -d <database> -t <table> [options]", string.Empty };
        foreach (var o in LegacyCommandLine.Options)
        {
            var name = (o.ShortName is { } c ? $"-{c}, " : "    ") + "--" + o.Name;
            var arg = o.Kind switch
            {
                OptionKind.Value => " <value>",
                OptionKind.List => " <values...>",
                _ => string.Empty,
            };
            var help = o.Supported ? o.Help ?? string.Empty : "(not supported yet)";
            lines.Add($"  {name + arg,-62} {help}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
