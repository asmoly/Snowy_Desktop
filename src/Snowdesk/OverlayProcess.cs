namespace Snowdesk;

internal static class OverlayProcess
{
    private const string ControlName = @"Local\Snowdesk.Control";
    private const string LifetimeName = @"Local\Snowdesk.Lifetime";
    private const string StopName = @"Local\Snowdesk.Stop";
    private const string BackgroundName = @"Local\Snowdesk.Background";
    private const string AccumulationName = @"Local\Snowdesk.Accumulation";
    private const string DensityChangedName = @"Local\Snowdesk.DensityChanged";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    public static int Start() => WithControlLock(() =>
    {
        using var lifetime = new Mutex(false, LifetimeName);
        if (IsRunning(lifetime))
        {
            Console.WriteLine("Snowdesk is already running.");
            return 0;
        }

        string readyName = $@"Local\Snowdesk.Ready.{Guid.NewGuid():N}";
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, readyName);
        string host = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not locate the snowdesk executable.");
        string commandLine = $"\"{host}\"";
        if (string.Equals(Path.GetFileNameWithoutExtension(host), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            commandLine += $" \"{typeof(Program).Assembly.Location}\"";
        }
        commandLine += $" --overlay {readyName}";

        using var child = DetachedProcess.Start(host, commandLine);
        switch (WaitHandle.WaitAny([child, ready], CommandTimeout))
        {
            case 0:
                throw new InvalidOperationException($"The overlay exited during startup (exit code {child.ExitCode}).");
            case 1:
                Console.WriteLine("Snowdesk started.");
                return 0;
            default:
                child.Terminate();
                child.WaitOne(CommandTimeout);
                throw new TimeoutException("The overlay did not start in time.");
        }
    });

    public static int Stop() => WithControlLock(() =>
    {
        using var lifetime = new Mutex(false, LifetimeName);
        if (!IsRunning(lifetime))
        {
            Console.WriteLine("Snowdesk is already stopped.");
            return 0;
        }

        if (EventWaitHandle.TryOpenExisting(StopName, out var stop))
        {
            using (stop)
            {
                stop.Set();
            }
        }

        if (!Acquire(lifetime, CommandTimeout))
        {
            throw new TimeoutException("The overlay did not stop in time.");
        }
        lifetime.ReleaseMutex();
        Console.WriteLine("Snowdesk stopped.");
        return 0;
    });

    public static int Status() => WithControlLock(() =>
    {
        using var lifetime = new Mutex(false, LifetimeName);
        Console.WriteLine(IsRunning(lifetime) ? "Snowdesk is running." : "Snowdesk is stopped.");
        return 0;
    });

    public static int Background(bool? enabled = null) => WithControlLock(() =>
    {
        if (enabled is bool value)
        {
            SnowdeskSettings.SaveBackground(value);
            UpdateSettingEvent(BackgroundName, value);
        }

        Console.WriteLine((enabled ?? SnowdeskSettings.IsBackgroundEnabled()) ? "Background mode is on." : "Background mode is off.");
        return 0;
    });

    public static int Density(int? density = null) => WithControlLock(() =>
    {
        if (density is int value)
        {
            SnowdeskSettings.SaveDensity(value);
            UpdateSettingEvent(DensityChangedName, signaled: true);
        }

        Console.WriteLine($"Snow density is {density ?? SnowdeskSettings.ReadDensity()}.");
        return 0;
    });

    public static int Accumulation(bool? enabled = null) => WithControlLock(() =>
    {
        if (enabled is bool value)
        {
            SnowdeskSettings.SaveAccumulation(value);
            UpdateSettingEvent(AccumulationName, value);
        }

        Console.WriteLine((enabled ?? SnowdeskSettings.IsAccumulationEnabled())
            ? "Snow accumulation is on." : "Snow accumulation is off.");
        return 0;
    });

    private static void UpdateSettingEvent(string name, bool signaled)
    {
        if (EventWaitHandle.TryOpenExisting(name, out var setting))
        {
            using (setting)
            {
                if (signaled)
                {
                    setting.Set();
                }
                else
                {
                    setting.Reset();
                }
            }
            return;
        }

        using var lifetime = new Mutex(false, LifetimeName);
        if (IsRunning(lifetime))
        {
            Console.WriteLine("Restart Snowdesk to apply the saved setting.");
        }
    }

    public static int Run(string readyName)
    {
        using var ready = EventWaitHandle.OpenExisting(readyName);
        using var lifetime = new Mutex(false, LifetimeName);
        if (!Acquire(lifetime, TimeSpan.Zero))
        {
            throw new InvalidOperationException("Another overlay is already running.");
        }

        try
        {
            using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopName);
            using var background = new EventWaitHandle(SnowdeskSettings.IsBackgroundEnabled(), EventResetMode.ManualReset, BackgroundName);
            using var accumulation = new EventWaitHandle(SnowdeskSettings.IsAccumulationEnabled(), EventResetMode.ManualReset, AccumulationName);
            using var densityChanged = new EventWaitHandle(false, EventResetMode.AutoReset, DensityChangedName);
            // Keep the main UI thread under the same exception handling as startup.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            using var overlay = new OverlayContext(stop, background, densityChanged, accumulation);
            ready.Set();
            Application.Run(overlay);
            return 0;
        }
        finally
        {
            lifetime.ReleaseMutex();
        }
    }

    private static int WithControlLock(Func<int> command)
    {
        using var control = new Mutex(false, ControlName);
        if (!Acquire(control, CommandTimeout))
        {
            throw new TimeoutException("Another snowdesk command is still in progress.");
        }

        try
        {
            return command();
        }
        finally
        {
            control.ReleaseMutex();
        }
    }

    private static bool IsRunning(Mutex lifetime)
    {
        if (!Acquire(lifetime, TimeSpan.Zero))
        {
            return true;
        }

        lifetime.ReleaseMutex();
        return false;
    }

    private static bool Acquire(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }
}
