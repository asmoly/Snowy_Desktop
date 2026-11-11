using System.Runtime.InteropServices;

namespace Snowdesk;

internal sealed class SnowOverlay : Form
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int DcBrush = 18;
    private const int NullPen = 8;
    private const int EmptyRegion = 1;
    private const uint PatCopy = 0x00F00021;
    private const int TransparentColorKey = 0xFF00FF;
    private static readonly uint[] FlakeColors = [0xC0C0C0, 0xDCDCDC, 0xF5F5F5, 0xFFFFFF];
    private SnowField? field;
    private Size fieldSize;
    private int fieldDpi;
    private IReadOnlyList<DesktopWindow>? coveredAreas;
    private SnowAccumulation? accumulation;

    public SnowOverlay(Rectangle bounds)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        TransparencyKey = ColorTranslator.FromWin32(TransparentColorKey);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
        Bounds = bounds;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // TransparencyKey makes the window layered. WS_EX_TRANSPARENT then
            // passes clicks through the whole window, including the visible flakes.
            parameters.ExStyle |= WsExTransparent | WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }

    public void Advance(float seconds, float time, float wind, int density, IReadOnlyList<DesktopWindow>? windows,
        bool desktopOnly, Point? cursor)
    {
        coveredAreas = desktopOnly ? windows : null;
        if (ClientSize != fieldSize || DeviceDpi != fieldDpi)
        {
            fieldSize = ClientSize;
            fieldDpi = DeviceDpi;
            field = new SnowField(fieldSize.Width, fieldSize.Height, fieldDpi, density);
            accumulation = null;
        }

        if (cursor is Point position && windows is not null)
        {
            accumulation ??= new SnowAccumulation(DeviceDpi);
            accumulation.Advance(Bounds, windows, position, seconds, wind, density, desktopOnly);
        }
        else
        {
            accumulation = null;
        }

        // Present the complete frame together so a bank never flashes clear
        // while the layered window is being repainted.
        DoubleBuffered = accumulation is not null;
        field?.SetDensity(density);
        field?.Advance(seconds, time, wind);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        nint dc = args.Graphics.GetHdc();
        nint oldBrush = SelectObject(dc, GetStockObject(DcBrush));
        nint oldPen = SelectObject(dc, GetStockObject(NullPen));
        int savedClip = 0;
        try
        {
            Rectangle clip = args.ClipRectangle;
            SetDCBrushColor(dc, TransparentColorKey);
            PatBlt(dc, clip.X, clip.Y, clip.Width, clip.Height, PatCopy);
            if (field is null)
            {
                return;
            }

            if (coveredAreas is not { Count: > 0 } areas)
            {
                DrawScene(dc, field);
                return;
            }

            savedClip = SaveDC(dc);
            if (savedClip != 0)
            {
                if (ClipToDesktop(dc, areas))
                {
                    DrawScene(dc, field);
                }
                RestoreDC(dc, savedClip);
                savedClip = 0;
            }

            // Keep a narrow ridge at the taskbar edge even when the desktop
            // mask covers the area immediately above it.
            if (accumulation is not null)
            {
                SetDCBrushColor(dc, FlakeColors[^1]);
                foreach (SnowAccumulation.SnowBank bank in accumulation.Banks)
                {
                    if (bank.HasSnow && !bank.TaskbarLip.IsEmpty)
                    {
                        DrawBank(dc, bank, bank.TaskbarLip);
                    }
                }
            }
        }
        finally
        {
            if (savedClip != 0)
            {
                RestoreDC(dc, savedClip);
            }
            SelectObject(dc, oldPen);
            SelectObject(dc, oldBrush);
            args.Graphics.ReleaseHdc(dc);
        }
    }

    private void DrawScene(nint dc, SnowField snow)
    {
        for (int shade = 0; shade < FlakeColors.Length; shade++)
        {
            SetDCBrushColor(dc, FlakeColors[shade]);
            foreach (ref readonly Snowflake flake in snow.Flakes)
            {
                if (flake.Shade == shade && flake.Y + flake.Diameter > 0)
                {
                    int x = (int)MathF.Floor(flake.X);
                    int y = (int)MathF.Floor(flake.Y);
                    int size = (int)MathF.Round(flake.Diameter);
                    Ellipse(dc, x, y, x + size, y + size);
                }
            }
        }

        if (accumulation is null)
        {
            return;
        }

        SetDCBrushColor(dc, FlakeColors[^1]);
        foreach (SnowAccumulation.SnowBank bank in accumulation.Banks)
        {
            if (bank.HasSnow && (coveredAreas is null || bank.TaskbarLip.IsEmpty))
            {
                DrawBank(dc, bank);
            }
        }
        foreach (ref readonly LooseSnow flake in accumulation.LooseFlakes)
        {
            int x = (int)MathF.Round(flake.Position.X);
            int y = (int)MathF.Round(flake.Position.Y);
            int size = (int)MathF.Round(flake.Diameter * Math.Min(1, flake.Life * 3));
            if (size > 0)
            {
                Ellipse(dc, x, y, x + (int)MathF.Round(size * flake.Aspect), y + size);
            }
        }
    }

    private void DrawBank(nint dc, SnowAccumulation.SnowBank bank, Rectangle? limit = null)
    {
        if (limit is null && (bank.Blockers.Count == 0 || coveredAreas is not null))
        {
            Polygon(dc, bank.Outline, bank.OutlineCount);
            return;
        }

        int savedClip = SaveDC(dc);
        if (savedClip == 0)
        {
            return;
        }

        try
        {
            if (limit is Rectangle clip && IntersectClipRect(dc, clip.Left, clip.Top, clip.Right, clip.Bottom) <= EmptyRegion)
            {
                return;
            }

            foreach (Rectangle blocker in bank.Blockers)
            {
                if (ExcludeClipRect(dc, blocker.Left, blocker.Top, blocker.Right, blocker.Bottom) <= EmptyRegion)
                {
                    return;
                }
            }
            Polygon(dc, bank.Outline, bank.OutlineCount);
        }
        finally
        {
            RestoreDC(dc, savedClip);
        }
    }

    private bool ClipToDesktop(nint dc, IReadOnlyList<DesktopWindow> areas)
    {
        Rectangle monitor = Bounds;
        foreach (DesktopWindow area in areas)
        {
            Rectangle window = area.Bounds;
            if (monitor.IntersectsWith(window)
                && ExcludeClipRect(dc, window.Left - monitor.Left, window.Top - monitor.Top,
                    window.Right - monitor.Left, window.Bottom - monitor.Top) <= EmptyRegion)
            {
                return false;
            }
        }
        return true;
    }

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int SaveDC(nint dc);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern bool RestoreDC(nint dc, int state);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int ExcludeClipRect(nint dc, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int IntersectClipRect(nint dc, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern nint GetStockObject(int index);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern nint SelectObject(nint dc, nint obj);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern uint SetDCBrushColor(nint dc, uint color);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern bool PatBlt(nint dc, int x, int y, int width, int height, uint operation);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern bool Ellipse(nint dc, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern bool Polygon(nint dc, [In] Point[] points, int count);
}
