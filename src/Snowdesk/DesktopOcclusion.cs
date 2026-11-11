using System.Runtime.InteropServices;
using System.Text;

namespace Snowdesk;

internal sealed class DesktopOcclusion
{
    private const int ExtendedFrameBounds = 9;
    private const int Cloaked = 14;
    private const int ExtendedStyle = -20;
    private const int Transparent = 0x20;
    private const int ToolWindow = 0x80;
    private const int Layered = 0x80000;
    private const int InMoveSize = 0x02;
    private readonly List<DesktopWindow> windows = [];
    private readonly WindowDragFilter dragFilter = new();
    private readonly StringBuilder className = new(64);
    private readonly WindowCallback collectWindow;
    private nint shellWindow;
    private nint movingWindow;
    private uint shellProcess;
    private bool includeLedges;

    public DesktopOcclusion()
    {
        collectWindow = CollectWindow;
    }

    public IReadOnlyList<DesktopWindow> Refresh(bool includeLedges)
    {
        this.includeLedges = includeLedges;
        windows.Clear();
        shellWindow = GetShellWindow();
        GetWindowThreadProcessId(shellWindow, out shellProcess);
        var gui = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        movingWindow = GetGUIThreadInfo(0, ref gui) && (gui.Flags & InMoveSize) != 0 ? gui.MoveSize : 0;
        EnumWindows(collectWindow, 0);
        dragFilter.Apply(windows, movingWindow, Environment.TickCount64);
        return windows;
    }

    private bool CollectWindow(nint window, nint parameter)
    {
        if (window == shellWindow || !IsWindowVisible(window) || IsIconic(window))
        {
            return true;
        }

        GetWindowThreadProcessId(window, out uint process);
        if (process == Environment.ProcessId)
        {
            return true;
        }

        string windowClass = process == shellProcess || includeLedges ? ReadClassName(window) : "";
        // Explorer can host the wallpaper and icons in a separate desktop window.
        if (process == shellProcess && windowClass is ("WorkerW" or "Progman"))
        {
            return true;
        }

        if (GetCloakState(window, Cloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return true;
        }

        bool taskbar = windowClass is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
        if (((!taskbar && GetFrameBounds(window, ExtendedFrameBounds, out var rect, Marshal.SizeOf<WindowRect>()) == 0)
                || GetWindowRect(window, out rect))
            && rect.Right > rect.Left && rect.Bottom > rect.Top)
        {
            int style = GetWindowLongW(window, ExtendedStyle);
            if (!taskbar && (style & (Layered | Transparent)) == (Layered | Transparent))
            {
                return true;
            }

            bool supportsSnow = includeLedges && (taskbar || (style & ToolWindow) == 0);
            windows.Add(new DesktopWindow(window, Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom),
                supportsSnow, taskbar, window == movingWindow));
        }

        return true;
    }

    private string ReadClassName(nint window) =>
        GetClassNameW(window, className, className.Capacity) > 0 ? className.ToString() : "";

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public int Flags;
        public nint Active;
        public nint Focus;
        public nint Capture;
        public nint MenuOwner;
        public nint MoveSize;
        public nint Caret;
        public WindowRect CaretBounds;
    }

    private delegate bool WindowCallback(nint window, nint parameter);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool EnumWindows(WindowCallback callback, nint parameter);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint process);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder name, int capacity);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetWindowRect(nint window, out WindowRect rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetWindowLongW(nint window, int index);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute", ExactSpelling = true)]
    private static extern int GetFrameBounds(nint window, int attribute, out WindowRect rect, int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute", ExactSpelling = true)]
    private static extern int GetCloakState(nint window, int attribute, out int cloaked, int size);
}

internal readonly record struct DesktopWindow(nint Handle, Rectangle Bounds, bool SupportsSnow, bool IsTaskbar,
    bool IsMoving = false);
