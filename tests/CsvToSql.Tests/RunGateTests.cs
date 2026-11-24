using CsvToSql.Core.Import;
using CsvToSql.Core.Logging;

namespace CsvToSql.Tests;

public class RunGateTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "csvtosql-gate-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void WaitsForAFreeSlotWhenAllAreTaken()
    {
        var dir = TempDir();
        try
        {
            var logFile = dir + ".log";
            var log = new ImportLog(logFile, console: TextWriter.Null);
            var first = new RunGate { MaxConcurrentRuns = 2, LockDirectory = dir }.Enter(log);
            using var second = new RunGate { MaxConcurrentRuns = 2, LockDirectory = dir }.Enter(log);
            var sleeps = 0;
            var gate = new RunGate
            {
                MaxConcurrentRuns = 2,
                LockDirectory = dir,
                PollInterval = TimeSpan.FromSeconds(5),
                LogInterval = TimeSpan.FromSeconds(10),
                Sleep = _ =>
                {
                    if (++sleeps == 3)
                    {
                        first.Dispose();
                    }
                },
            };

            using (gate.Enter(log))
            {
                Assert.Equal(3, sleeps);
            }

            log.Dispose();
            var output = File.ReadAllText(logFile);
            File.Delete(logFile);
            Assert.Contains("Waiting for a free run slot: 2 CsvToDb runs are already active", output, StringComparison.Ordinal);
            Assert.Contains("waited 10s so far", output, StringComparison.Ordinal);
            Assert.Contains("Starting after waiting 15s for a free run slot", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void NoLimitTakesNoSlot()
    {
        var dir = TempDir();
        using (new RunGate { MaxConcurrentRuns = 0, LockDirectory = dir }.Enter(ImportLog.Null))
        {
            Assert.False(Directory.Exists(dir));
        }
    }

    [Fact]
    public void WaitsForMemory()
    {
        var readings = new Queue<(long, long)>([(300, 8000), (500, 8000), (2000, 8000)]);
        var logFile = TempDir() + ".log";
        var sleeps = 0;
        using (var log = new ImportLog(logFile, console: TextWriter.Null))
        using (new RunGate { MinFreeMemoryMB = 1024, Memory = () => readings.Dequeue(), Sleep = _ => sleeps++ }.Enter(log))
        {
        }

        var output = File.ReadAllText(logFile);
        File.Delete(logFile);
        Assert.Equal(2, sleeps);
        Assert.Contains("Waiting for memory: 300 MB available, 1024 MB needed", output, StringComparison.Ordinal);
        Assert.Contains("Starting after waiting 10s for memory", output, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoresAMemoryThresholdAboveTheMachineTotal()
    {
        var console = new StringWriter();
        using var log = new ImportLog(console: console);
        using (new RunGate { MinFreeMemoryMB = 4096, Memory = () => (1000, 2000), Sleep = _ => throw new InvalidOperationException("waited") }.Enter(log))
        {
        }

        Assert.Contains("is more than this machine's 2000 MB, so it is ignored", console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsMemoryOnLinux()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");
        var memory = RunGate.ReadMemoryMB();
        Assert.NotNull(memory);
        Assert.InRange(memory.Value.Available, 1, memory.Value.Total);
    }
}
