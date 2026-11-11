using System.Numerics;
using System.Runtime.InteropServices;

namespace Snowdesk;

internal sealed class SnowAccumulation
{
    private const int MaxLedges = 16;
    private const int MaxLooseFlakes = 96;
    private const int MaxSamplesPerBank = 2048;
    private const double BankRetentionSeconds = 2;
    private readonly List<LooseSnow> loose = [];
    private readonly Dictionary<nint, SnowBank> banks = new();
    private readonly List<SnowBank> visibleBanks = [];
    private readonly List<nint> expiredBanks = [];
    private readonly float scale;
    private readonly int sampleSpacing;
    private readonly int maxDepth;
    private Point? previousCursor;
    private Rectangle monitor;
    private double elapsed;

    public SnowAccumulation(int dpi)
    {
        scale = dpi / 96f;
        sampleSpacing = (int)MathF.Round(12 * scale);
        maxDepth = (int)MathF.Ceiling(44 * scale);
    }

    public ReadOnlySpan<LooseSnow> LooseFlakes => CollectionsMarshal.AsSpan(loose);
    public ReadOnlySpan<SnowBank> Banks => CollectionsMarshal.AsSpan(visibleBanks);

    public void Advance(Rectangle monitor, IReadOnlyList<DesktopWindow> windows, Point cursor, float seconds, float wind,
        int density, bool desktopOnly)
    {
        if (this.monitor.Location != monitor.Location)
        {
            loose.Clear();
        }
        this.monitor = monitor;
        elapsed += seconds;
        visibleBanks.Clear();

        AdvanceLooseSnow(seconds, wind);
        var brush = new CursorBrush(previousCursor ?? cursor, cursor, monitor.Location, 24 * scale);
        previousCursor = cursor;
        float growth = (0.55f + density * 0.04f) * scale * seconds;
        float targetDepth = (18f + density * 0.15f) * scale;

        int windowBanks = 0;
        for (int i = 0; i < windows.Count; i++)
        {
            DesktopWindow window = windows[i];
            Rectangle bounds = window.IsTaskbar ? Rectangle.Intersect(window.Bounds, monitor) : window.Bounds;
            if (!window.SupportsSnow || bounds.Width < (window.IsTaskbar ? 24 : 60) * scale
                || bounds.Height < (window.IsTaskbar ? 12 : 24) * scale
                || (!window.IsTaskbar && (windowBanks >= MaxLedges || bounds.Top <= monitor.Top + 2 * scale))
                || bounds.Top >= monitor.Bottom
                || !monitor.IntersectsWith(bounds))
            {
                continue;
            }

            bounds.Offset(-monitor.X, -monitor.Y);
            if (!banks.TryGetValue(window.Handle, out var bank))
            {
                bank = new SnowBank(bounds);
                banks.Add(window.Handle, bank);
            }

            var oldBounds = bank.Bounds;
            bank.Bounds = bounds;
            if (window.IsTaskbar)
            {
                int ridgeHeight = Math.Min(bounds.Height / 3, (int)(10 * scale));
                // A bar mounted at the top of the monitor has no room above it.
                int ridgeBottom = bounds.Top == 0 ? ridgeHeight : bounds.Top;
                int ridgeTop = Math.Max(0, ridgeBottom - ridgeHeight);
                bank.TaskbarLip = new Rectangle(bounds.Left, ridgeTop, bounds.Width, ridgeBottom - ridgeTop);
            }
            int samples = Math.Min(MaxSamplesPerBank, (bounds.Width + sampleSpacing - 1) / sampleSpacing);
            bank.Resize(samples);
            bank.Blockers.Clear();
            var band = new Rectangle(bounds.Left, bank.BaseY - maxDepth, bounds.Width, maxDepth);
            for (int j = 0; j < i; j++)
            {
                Rectangle blocker = windows[j].Bounds;
                blocker.Offset(-monitor.X, -monitor.Y);
                // A maximized window can meet the taskbar edge without covering it.
                if (blocker.IntersectsWith(band) && (!window.IsTaskbar || blocker.IntersectsWith(bounds)))
                {
                    bank.Blockers.Add(blocker);
                }
            }

            UpdateBank(bank, oldBounds, brush, window.IsMoving, seconds, growth, targetDepth, desktopOnly);
            bank.LastSeen = elapsed;
            visibleBanks.Add(bank);
            if (!window.IsTaskbar)
            {
                windowBanks++;
            }
        }

        // A window can briefly lose its exposed edge while being dragged.
        // Keep its snow profile available when that edge reappears.
        foreach (var entry in banks)
        {
            if (elapsed - entry.Value.LastSeen > BankRetentionSeconds)
            {
                expiredBanks.Add(entry.Key);
            }
        }
        foreach (nint handle in expiredBanks)
        {
            banks.Remove(handle);
        }
        expiredBanks.Clear();
    }

    private void UpdateBank(SnowBank bank, Rectangle previousBounds, CursorBrush brush, bool isMoving, float seconds,
        float growth, float targetDepth, bool desktopOnly)
    {
        Rectangle bounds = bank.Bounds;
        var motion = new Vector2(bounds.X - previousBounds.X, bounds.Y - previousBounds.Y);
        int baseline = bank.BaseY;
        float depthLimit = Math.Min(maxDepth, baseline);
        float displayScale = desktopOnly && !bank.TaskbarLip.IsEmpty ? bank.TaskbarLip.Height / depthLimit : 1;
        if (bank.TaskbarLip.IsEmpty)
        {
            UpdateSlide(bank, motion, bounds.Size != previousBounds.Size, seconds);
        }
        float shoulderWidth = Math.Min(36 * scale, bounds.Width * 0.45f);
        int brushReleases = 0;
        for (int i = 0; i < bank.SampleCount; i++)
        {
            float height = bank.Heights[i];
            int x = bank.SampleX(i);
            float edge = Math.Min(x - bounds.Left, bounds.Right - x);
            float shoulder = Math.Clamp(edge / shoulderWidth, 0, 1);
            shoulder = shoulder * shoulder * (3 - 2 * shoulder);
            bool exposed = !bank.IsCovered(x, baseline - 1);
            float goal = Math.Min(depthLimit, targetDepth * bank.Profile[i]) * shoulder;
            float step = growth * bank.Profile[i];
            if (exposed)
            {
                height += Math.Clamp(goal - height, -step * 0.25f, step);
            }

            var center = new Vector2(x, baseline - height * displayScale / 2);
            float brushed = exposed && !isMoving ? brush.StrengthAt(center) : 0;
            if (height >= 2 * scale && brushed > 0)
            {
                if (brushReleases < 5)
                {
                    ReleaseSnow(center, brush.Motion * (0.3f / Math.Max(seconds, 0.016f)), height, 2);
                    brushReleases++;
                }
                height *= 1 - 0.68f * brushed;
            }

            bank.Heights[i] = Math.Clamp(height, 0, depthLimit);
        }
        bank.BuildOutline(displayScale);
    }

    private void UpdateSlide(SnowBank bank, Vector2 motion, bool resized, float seconds)
    {
        float duration = Math.Max(seconds, 0.016f);
        float snapDistance = Math.Max(100, 2500 * duration) * scale;
        if (resized || motion.LengthSquared() > snapDistance * snapDistance)
        {
            bank.Velocity = Vector2.Zero;
            bank.SlideSpeed = 0;
            bank.LeftSpill = bank.RightSpill = 0;
            return;
        }

        if (motion == Vector2.Zero && bank.Velocity == Vector2.Zero && bank.SlideSpeed == 0)
        {
            return;
        }

        float oldSpeed = bank.Velocity.X;
        bank.Velocity = Vector2.Lerp(bank.Velocity, motion / duration, 1 - MathF.Exp(-seconds * 12));
        if (bank.Velocity.LengthSquared() < scale * scale)
        {
            bank.Velocity = Vector2.Zero;
        }

        // In the window's frame of reference, acceleration pulls the snow
        // the other way. Friction damps the slide once the window steadies.
        float change = bank.Velocity.X - oldSpeed;
        float slip = MathF.CopySign(Math.Max(0, MathF.Abs(change) - 280 * scale * seconds), change);
        float relativeSpeed = Math.Clamp(bank.SlideSpeed - slip, -310 * scale, 310 * scale);
        bank.SlideSpeed = relativeSpeed * MathF.Exp(-4.6f * seconds);
        if (MathF.Abs(bank.SlideSpeed) < 2 * scale)
        {
            bank.SlideSpeed = 0;
        }

        float distance = (relativeSpeed + bank.SlideSpeed) * (seconds * 0.5f);
        if (distance != 0)
        {
            SlideSnow(bank, distance);
        }
    }

    private void SlideSnow(SnowBank bank, float distance)
    {
        int direction = Math.Sign(distance);
        int edge = direction > 0 ? bank.SampleCount - 1 : 0;
        int inlet = direction > 0 ? 0 : bank.SampleCount - 1;
        float cells = MathF.Abs(distance) * bank.SampleCount / bank.Bounds.Width;
        float spilled = 0;
        float edgeHeight = bank.Heights[edge];

        // Move snow between adjacent samples, preserving the amount that
        // remains on the ledge and collecting whatever crosses the edge.
        while (cells > 0)
        {
            float fraction = Math.Min(1, cells);
            spilled += bank.Heights[edge] * fraction;
            for (int i = edge; i != inlet; i -= direction)
            {
                bank.Heights[i] += (bank.Heights[i - direction] - bank.Heights[i]) * fraction;
            }
            bank.Heights[inlet] *= 1 - fraction;
            cells -= fraction;
        }

        float clumpAmount = 6.5f * scale;
        ref float carry = ref (direction > 0 ? ref bank.RightSpill : ref bank.LeftSpill);
        carry += spilled;
        int count = Math.Min((int)(carry / clumpAmount), Math.Min(4, MaxLooseFlakes - loose.Count));
        carry %= clumpAmount;

        int edgeX = direction > 0 ? bank.Bounds.Right : bank.Bounds.Left;
        int y = bank.BaseY - (int)Math.Clamp(edgeHeight * 0.5f, 2 * scale, 12 * scale);
        if (count == 0 || bank.IsCovered(bank.SampleX(edge), y))
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            float diameter = (4.5f + Random.Shared.NextSingle() * 3) * scale;
            float x = direction > 0 ? edgeX : edgeX - diameter;
            float outward = (22 + Random.Shared.NextSingle() * 30) * scale;
            var velocity = new Vector2(
                Math.Clamp(bank.Velocity.X + bank.SlideSpeed + direction * outward, -320 * scale, 320 * scale),
                Math.Clamp(bank.Velocity.Y * 0.5f + outward, -95 * scale, 100 * scale));
            loose.Add(new LooseSnow(new Vector2(x, y), velocity, diameter, 1.4f, 1.4f));
        }
    }

    private void ReleaseSnow(Vector2 origin, Vector2 impulse, float height, int fragments)
    {
        fragments = Math.Min(MaxLooseFlakes - loose.Count, fragments);
        for (int i = 0; i < fragments; i++)
        {
            float spread = (Random.Shared.NextSingle() - 0.5f) * 56 * scale;
            var velocity = new Vector2(Math.Clamp(impulse.X + spread, -240 * scale, 240 * scale),
                Math.Clamp(impulse.Y - 38 * scale - MathF.Abs(spread), -120 * scale, 75 * scale));
            float diameter = Math.Clamp(height * 0.28f, 2 * scale, 5.5f * scale);
            loose.Add(new LooseSnow(origin, velocity, diameter * (1 - i * 0.23f), 1.4f));
        }
    }

    private void AdvanceLooseSnow(float seconds, float wind)
    {
        for (int i = loose.Count - 1; i >= 0; i--)
        {
            LooseSnow flake = loose[i];
            Vector2 velocity = flake.Velocity * (1 - 0.35f * seconds)
                + new Vector2(wind * 0.85f, 280) * (seconds * scale);
            Vector2 position = flake.Position + (flake.Velocity + velocity) * (seconds * 0.5f);
            float life = flake.Life - seconds;
            if (life <= 0 || position.Y >= monitor.Height || position.X < -30 * scale
                || position.X >= monitor.Width + 30 * scale)
            {
                loose[i] = loose[^1];
                loose.RemoveAt(loose.Count - 1);
            }
            else
            {
                loose[i] = flake with { Position = position, Velocity = velocity, Life = life };
            }
        }
    }

    internal sealed class SnowBank(Rectangle bounds)
    {
        private const int SegmentsPerInterval = 3;
        private readonly float phase = Random.Shared.NextSingle() * MathF.Tau;
        public Rectangle Bounds = bounds;
        public Rectangle TaskbarLip;
        public int BaseY => TaskbarLip.IsEmpty ? Bounds.Top : TaskbarLip.Bottom;
        public Vector2 Velocity;
        public float SlideSpeed;
        public float LeftSpill;
        public float RightSpill;
        public float[] Heights = [];
        public float[] Profile = [];
        public Point[] Outline = [];
        public int SampleCount;
        public int OutlineCount => (SampleCount + 1) * SegmentsPerInterval + 1;
        public bool HasSnow;
        public double LastSeen;
        public List<Rectangle> Blockers { get; } = [];

        public int SampleX(int index) => Bounds.Left + (int)((index + 0.5f) * Bounds.Width / SampleCount);

        public void Resize(int count)
        {
            if (count > Heights.Length)
            {
                int capacity = Math.Max(count, Heights.Length * 2);
                int oldCapacity = Heights.Length;
                Array.Resize(ref Heights, capacity);
                Array.Resize(ref Profile, capacity);
                Array.Resize(ref Outline, (capacity + 1) * SegmentsPerInterval + 1);
                for (int i = oldCapacity; i < capacity; i++)
                {
                    Profile[i] = 1 + 0.22f * MathF.Sin(i * 0.27f + phase)
                        + 0.10f * MathF.Sin(i * 0.69f + phase * 2);
                }
            }
            if (count < SampleCount)
            {
                Array.Clear(Heights, count, SampleCount - count);
            }
            SampleCount = count;
        }

        public void BuildOutline(float displayScale)
        {
            int baseline = BaseY;
            int leftX = Bounds.Left;
            float leftDepth = 0;
            float leftSlope = 0;
            HasSnow = false;
            Outline[0] = new Point(leftX, baseline);
            for (int i = 0; i <= SampleCount; i++)
            {
                int rightX = i == SampleCount ? Bounds.Right : SampleX(i);
                float rightDepth = i == SampleCount ? 0 : Heights[i] * displayScale;
                int width = rightX - leftX;
                float rightSlope = 0;
                if (i < SampleCount && width > 0)
                {
                    int nextX = i + 1 == SampleCount ? Bounds.Right : SampleX(i + 1);
                    float nextDepth = i + 1 == SampleCount ? 0 : Heights[i + 1] * displayScale;
                    if (nextX > rightX)
                    {
                        float rise = (rightDepth - leftDepth) / width;
                        float nextRise = (nextDepth - rightDepth) / (nextX - rightX);
                        // A flat tangent at peaks and troughs keeps curves
                        // from overshooting the settled height or a groove.
                        if (rise * nextRise > 0)
                        {
                            rightSlope = 2 * rise * nextRise / (rise + nextRise);
                        }
                    }
                }

                for (int segment = 1; segment <= SegmentsPerInterval; segment++)
                {
                    float t = (float)segment / SegmentsPerInterval;
                    float blend = t * t * (3 - 2 * t);
                    float curve = t * (1 - t) * width * ((1 - t) * leftSlope - t * rightSlope);
                    int depth = (int)MathF.Round(leftDepth + (rightDepth - leftDepth) * blend + curve);
                    int x = leftX + (int)MathF.Round(width * t);
                    Outline[i * SegmentsPerInterval + segment] = new Point(x, baseline - depth);
                    HasSnow |= depth > 0;
                }

                leftX = rightX;
                leftDepth = rightDepth;
                leftSlope = rightSlope;
            }
        }

        public bool IsCovered(int x, int y)
        {
            foreach (Rectangle blocker in Blockers)
            {
                if (blocker.Contains(x, y))
                {
                    return true;
                }
            }
            return false;
        }
    }

    private readonly struct CursorBrush(Point previous, Point current, Point origin, float radius)
    {
        private readonly Vector2 start = new(previous.X - origin.X, previous.Y - origin.Y);
        public readonly Vector2 Motion = new(current.X - previous.X, current.Y - previous.Y);

        public float StrengthAt(Vector2 position)
        {
            float length = Motion.LengthSquared();
            if (length == 0)
            {
                return 0;
            }

            float along = Math.Clamp(Vector2.Dot(position - start, Motion) / length, 0, 1);
            float distance = Vector2.DistanceSquared(position, start + Motion * along);
            return Math.Max(0, 1 - distance / (radius * radius));
        }
    }
}

internal readonly record struct LooseSnow(Vector2 Position, Vector2 Velocity, float Diameter, float Life, float Aspect = 1);
