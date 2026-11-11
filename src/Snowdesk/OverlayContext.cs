using System.Diagnostics;

namespace Snowdesk;

internal sealed class OverlayContext : ApplicationContext
{
    private const double DisplayCheckInterval = 2;
    private readonly Dictionary<string, SnowOverlay> overlays = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly EventWaitHandle stop;
    private readonly EventWaitHandle background;
    private readonly EventWaitHandle densityChanged;
    private readonly EventWaitHandle accumulation;
    private readonly DesktopOcclusion occlusion = new();
    private int density;
    private bool wasAccumulating;
    private double previousFrame;
    private double nextDisplayCheck = DisplayCheckInterval;

    public OverlayContext(EventWaitHandle stop, EventWaitHandle background, EventWaitHandle densityChanged,
        EventWaitHandle accumulation)
    {
        this.stop = stop;
        this.background = background;
        this.densityChanged = densityChanged;
        this.accumulation = accumulation;
        try
        {
            density = SnowdeskSettings.ReadDensity();
            UpdateDisplays();
            timer.Tick += OnTick;
            timer.Start();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (stop.WaitOne(0))
        {
            timer.Stop();
            ExitThread();
            return;
        }

        if (densityChanged.WaitOne(0))
        {
            density = SnowdeskSettings.ReadDensity();
        }

        double time = clock.Elapsed.TotalSeconds;
        float seconds = (float)Math.Min(time - previousFrame, 0.1);
        previousFrame = time;
        if (time >= nextDisplayCheck)
        {
            UpdateDisplays();
            nextDisplayCheck = time + DisplayCheckInterval;
        }

        // Slow changes in direction with shorter gusts shared by all displays.
        float wind = 30f * MathF.Sin((float)(time * 0.31 + 0.8))
            + 16f * MathF.Sin((float)(time * 0.81 + 0.4));
        bool desktopOnly = background.WaitOne(0);
        Point? cursor = accumulation.WaitOne(0) ? Cursor.Position : null;
        if (wasAccumulating && !cursor.HasValue)
        {
            BufferedGraphicsManager.Current.Invalidate();
        }
        wasAccumulating = cursor.HasValue;
        var windows = desktopOnly || cursor.HasValue ? occlusion.Refresh(includeLedges: cursor.HasValue) : null;
        foreach (SnowOverlay overlay in overlays.Values)
        {
            overlay.Advance(seconds, (float)time, wind, density, windows, desktopOnly, cursor);
        }
    }

    private void UpdateDisplays()
    {
        Screen[] screens = Screen.AllScreens;
        if (screens.MaxBy(screen => (long)screen.Bounds.Width * screen.Bounds.Height) is Screen largest)
        {
            // WinForms otherwise creates and discards a temporary buffer for
            // frames larger than its default control-sized buffer.
            BufferedGraphicsManager.Current.MaximumBuffer = largest.Bounds.Size;
        }

        foreach (string name in overlays.Keys.Except(screens.Select(screen => screen.DeviceName)).ToArray())
        {
            overlays[name].Dispose();
            overlays.Remove(name);
        }

        foreach (Screen screen in screens)
        {
            if (!overlays.TryGetValue(screen.DeviceName, out var overlay))
            {
                overlay = new SnowOverlay(screen.Bounds);
                overlays.Add(screen.DeviceName, overlay);
                overlay.Show();
            }

            // Use the monitor bounds, including the taskbar and negative desktop coordinates.
            if (overlay.Bounds != screen.Bounds)
            {
                overlay.Bounds = screen.Bounds;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            foreach (SnowOverlay overlay in overlays.Values)
            {
                overlay.Dispose();
            }
            overlays.Clear();
        }

        base.Dispose(disposing);
    }
}
