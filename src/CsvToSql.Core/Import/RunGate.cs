using System.Globalization;
using CsvToSql.Core.Logging;

namespace CsvToSql.Core.Import;

/// <summary>
/// Holds a run back until the machine has room for it, instead of letting concurrent runs exhaust memory and be
/// killed without trace: at most <see cref="MaxConcurrentRuns"/> CsvToDb processes at once, and at least
/// <see cref="MinFreeMemoryMB"/> of available memory before starting. Waits as long as it takes, logging while it waits.
/// </summary>
public sealed class RunGate
{
    /// <summary>Most runs on this machine at once; 0 for no limit.</summary>
    public int MaxConcurrentRuns { get; init; }

    /// <summary>Available memory (MB) needed before starting; 0 to not wait for memory.</summary>
    public long MinFreeMemoryMB { get; init; }

    /// <summary>Holds one lock file per run slot. The OS releases a slot's lock when its process exits, even if killed.</summary>
    public string LockDirectory { get; init; } = Path.Combine(Path.GetTempPath(), "CsvToDb-runs");

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan LogInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Available and total memory in MB, or null if unknown; replaceable for tests.</summary>
    public Func<(long Available, long Total)?> Memory { get; init; } = ReadMemoryMB;

    public Action<TimeSpan> Sleep { get; init; } = Thread.Sleep;

    /// <summary>Waits for a run slot and then for memory; dispose the result when the run ends to free the slot.</summary>
    public IDisposable Enter(ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var slot = MaxConcurrentRuns > 0 ? WaitForSlot(log) : null;
        try
        {
            WaitForMemory(log);
        }
        catch
        {
            slot?.Dispose();
            throw;
        }

        return slot ?? (IDisposable)new MemoryStream(0);
    }

    private FileStream WaitForSlot(ImportLog log)
    {
        CreateSharedDirectory(LockDirectory);
        var waited = TimeSpan.Zero;
        var lastLog = TimeSpan.Zero;
        while (true)
        {
            for (var i = 0; i < MaxConcurrentRuns; i++)
            {
                if (TryLock(Path.Combine(LockDirectory, string.Create(CultureInfo.InvariantCulture, $"slot-{i}.lock"))) is { } slot)
                {
                    if (waited > TimeSpan.Zero)
                    {
                        log.Info(string.Create(CultureInfo.InvariantCulture, $"Starting after waiting {Format(waited)} for a free run slot"));
                    }

                    return slot;
                }
            }

            if (waited == TimeSpan.Zero || waited - lastLog >= LogInterval)
            {
                lastLog = waited;
                log.Info(string.Create(CultureInfo.InvariantCulture,
                    $"Waiting for a free run slot: {MaxConcurrentRuns} CsvToDb runs are already active (--ui.wait.maxConcurrent){Waited(waited)}"));
            }

            Sleep(PollInterval);
            waited += PollInterval;
        }
    }

    private void WaitForMemory(ImportLog log)
    {
        if (MinFreeMemoryMB <= 0)
        {
            return;
        }

        var waited = TimeSpan.Zero;
        var lastLog = TimeSpan.Zero;
        while (Memory() is var (available, total) && available < MinFreeMemoryMB)
        {
            if (total < MinFreeMemoryMB)
            {
                log.Warn(string.Create(CultureInfo.InvariantCulture,
                    $"--ui.wait.minFreeMemoryMB {MinFreeMemoryMB} is more than this machine's {total} MB, so it is ignored"));
                return;
            }

            if (waited == TimeSpan.Zero || waited - lastLog >= LogInterval)
            {
                lastLog = waited;
                log.Info(string.Create(CultureInfo.InvariantCulture,
                    $"Waiting for memory: {available} MB available, {MinFreeMemoryMB} MB needed (--ui.wait.minFreeMemoryMB){Waited(waited)}"));
            }

            Sleep(PollInterval);
            waited += PollInterval;
        }

        if (waited > TimeSpan.Zero)
        {
            log.Info(string.Create(CultureInfo.InvariantCulture, $"Starting after waiting {Format(waited)} for memory"));
        }
    }

    private static FileStream? TryLock(string path)
    {
        try
        {
            // FileShare.None takes an exclusive lock (flock on Unix) that other processes' attempts fail on.
            var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.Read, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = (UnixFileMode)0x1B6; // 0666, less umask
            }

            return new FileStream(path, options);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void CreateSharedDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                // Runs under other accounts can then create slot files too.
                File.SetUnixFileMode(path, (UnixFileMode)0x3FF); // 1777
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string Waited(TimeSpan waited) => waited > TimeSpan.Zero ? $"; waited {Format(waited)} so far" : string.Empty;

    private static string Format(TimeSpan t) => t.TotalMinutes >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalMinutes}m {t.Seconds}s")
        : string.Create(CultureInfo.InvariantCulture, $"{t.TotalSeconds:0}s");

    /// <summary>
    /// Reads MemAvailable from /proc/meminfo, lowered to the cgroup v2 limit if the process runs under one
    /// (e.g. in a container or a systemd service with MemoryMax). Null elsewhere.
    /// </summary>
    public static (long Available, long Total)? ReadMemoryMB()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        try
        {
            long? available = null, total = null;
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                {
                    available = KbToMb(line);
                }
                else if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    total = KbToMb(line);
                }
            }

            if (available is null || total is null)
            {
                return null;
            }

            if (CgroupLimitMB() is var (used, limit))
            {
                return (Math.Min(available.Value, Math.Max(0, limit - used)), Math.Min(total.Value, limit));
            }

            return (available.Value, total.Value);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long KbToMb(string line) =>
        long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture) / 1024;

    private static (long Used, long Limit)? CgroupLimitMB()
    {
        var entry = File.ReadLines("/proc/self/cgroup").FirstOrDefault(l => l.StartsWith("0::", StringComparison.Ordinal));
        if (entry is null)
        {
            return null;
        }

        var dir = Path.Join("/sys/fs/cgroup", entry[3..]);
        var max = Path.Join(dir, "memory.max");
        var current = Path.Join(dir, "memory.current");
        if (!File.Exists(max) || !File.Exists(current)
            || !long.TryParse(File.ReadAllText(max).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var limit))
        {
            return null; // "max": no limit
        }

        var used = long.Parse(File.ReadAllText(current).Trim(), CultureInfo.InvariantCulture);
        return (used / 1024 / 1024, limit / 1024 / 1024);
    }
}
