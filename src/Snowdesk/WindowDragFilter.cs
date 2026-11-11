namespace Snowdesk;

internal sealed class WindowDragFilter
{
    private const int PreviewReleaseDelayMs = 200;
    private readonly HashSet<nint> idleWindows = [];
    private nint draggedWindow;
    private long previewExpiresAt;

    public void Apply(List<DesktopWindow> windows, nint movingWindow, long now)
    {
        if (movingWindow != 0)
        {
            draggedWindow = movingWindow;
            previewExpiresAt = now + PreviewReleaseDelayMs;
        }
        else if (now >= previewExpiresAt)
        {
            draggedWindow = 0;
        }

        if (draggedWindow != 0 && idleWindows.Count != 0)
        {
            // Snap previews and other drag-only surfaces can report bounds
            // covering the whole display. Keep tracking the real window.
            windows.RemoveAll(window => window.Handle != draggedWindow && !window.IsTaskbar
                && !idleWindows.Contains(window.Handle));
            return;
        }

        if (draggedWindow == 0)
        {
            idleWindows.Clear();
            foreach (DesktopWindow window in windows)
            {
                idleWindows.Add(window.Handle);
            }
        }
    }
}
