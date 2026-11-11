namespace Snowdesk;

internal sealed class SnowField
{
    private const int FlakesPerFullHd = 125;
    private Snowflake[] flakes = [];
    private readonly int defaultCount;
    private readonly int width;
    private readonly int height;
    private readonly float scale;
    private int density;

    public SnowField(int width, int height, int dpi, int density)
    {
        this.width = width;
        this.height = height;
        scale = dpi / 96f;
        defaultCount = Math.Clamp((int)(width * (double)height / (1920 * 1080 * scale * scale)
            * FlakesPerFullHd), 24, 420);
        SetDensity(density);
    }

    public void SetDensity(int value)
    {
        if (density == value)
        {
            return;
        }

        density = value;
        int count = Math.Max(1, (int)Math.Round(defaultCount * density / (double)SnowdeskSettings.DefaultDensity));
        int previousCount = flakes.Length;
        Array.Resize(ref flakes, count);
        for (int i = previousCount; i < count; i++)
        {
            flakes[i] = Spawn();
            // Stagger new arrivals so they don't fall in one horizontal band.
            flakes[i].Y -= Random.Shared.NextSingle() * height * 0.4f;
        }
    }

    public ReadOnlySpan<Snowflake> Flakes => flakes;

    public void Advance(float seconds, float time, float wind)
    {
        foreach (ref Snowflake flake in flakes.AsSpan())
        {
            float flutter = 12f * MathF.Sin(time * flake.FlutterRate + flake.Phase);
            flake.X += (wind * flake.WindResponse + flutter) * scale * seconds;
            flake.Y += flake.FallSpeed * seconds;

            if (flake.X < -flake.Diameter)
            {
                flake.X += width + flake.Diameter;
            }
            else if (flake.X >= width)
            {
                flake.X -= width + flake.Diameter;
            }

            if (flake.Y >= height)
            {
                flake = Spawn();
            }
        }
    }

    private Snowflake Spawn()
    {
        var random = Random.Shared;
        float depth = random.NextSingle();
        float diameter = (1.4f + 3.4f * depth * depth) * scale;
        return new Snowflake
        {
            X = random.NextSingle() * width,
            Y = -diameter - random.NextSingle() * 24 * scale,
            Diameter = diameter,
            FallSpeed = (45f + depth * 85f + random.NextSingle() * 20f) * scale,
            WindResponse = 0.55f + depth * 0.8f,
            Phase = random.NextSingle() * MathF.Tau,
            FlutterRate = 1.2f + random.NextSingle() * 2f,
            Shade = Math.Min((int)(depth * 4), 3)
        };
    }
}

internal struct Snowflake
{
    public float X;
    public float Y;
    public float Diameter;
    public float FallSpeed;
    public float WindResponse;
    public float Phase;
    public float FlutterRate;
    public int Shade;
}
