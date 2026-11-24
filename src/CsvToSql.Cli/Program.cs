using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
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
        ImportLog log;
        try
        {
            log = new ImportLog(host.LogFilename, host.Verbose, stderr);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Failed file: {cl.Value("src.filename")}");
            stderr.WriteLine(ex.ToString());
            stderr.WriteLine(ExitMessage(ExitCodes.FileError));
            return ExitCodes.FileError;
        }

        using (log)
        using (OnTermination(log, cl.Value("src.filename")))
        {
            void OnWarning(string m) => log.Warn(m);
            FilterFactory.Warning += OnWarning;
            try
            {
                var options = OptionsMapper.Map(cl);
                foreach (var warning in OptionsMapper.Warnings(cl))
                {
                    log.Warn(warning);
                }

                using var slot = new RunGate { MaxConcurrentRuns = host.MaxConcurrentRuns, MinFreeMemoryMB = host.MinFreeMemoryMB }.Enter(log);
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

                if (host.ReturnRowCount && OperatingSystem.IsWindows())
                {
                    exitCode = (int)Math.Min(result.RowsWritten, int.MaxValue);
                }
            }
            catch (CommandLineException ex)
            {
                stderr.WriteLine(ex.Message);
                exitCode = ExitCodes.InvalidCommandLine;
                log.Error(ExitMessage(exitCode));
            }
#pragma warning disable CA1031 // top level handler: report and return a failure exit code
            catch (Exception ex)
#pragma warning restore CA1031
            {
                log.Error($"Failed file: {cl.Value("src.filename")}");
                log.Error(ex.ToString());
                exitCode = ExitCodes.For(ex);
                log.Error(ExitMessage(exitCode));
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

    private static string ExitMessage(int code) =>
        string.Create(CultureInfo.InvariantCulture, $"Exit code {code}: {ExitCodes.Describe(code)}");

    /// <summary>Logs a kill (e.g. a scheduler timeout) so the run doesn't vanish without trace; the process still stops.</summary>
    private static CompositeDisposable OnTermination(ImportLog log, string? file)
    {
        void Log(PosixSignalContext ctx) => log.Error($"Stopped by {ctx.Signal} before finishing file: {file}");
        return new CompositeDisposable(
            [.. new[] { PosixSignal.SIGTERM, PosixSignal.SIGINT, PosixSignal.SIGHUP, PosixSignal.SIGQUIT }
                .Where(s => !OperatingSystem.IsWindows() || s is PosixSignal.SIGINT or PosixSignal.SIGQUIT or PosixSignal.SIGHUP)
                .Select(s => PosixSignalRegistration.Create(s, Log))]);
    }

    private sealed class CompositeDisposable(IReadOnlyList<IDisposable> items) : IDisposable
    {
        public void Dispose()
        {
            foreach (var item in items)
            {
                item.Dispose();
            }
        }
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
