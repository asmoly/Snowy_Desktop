namespace Snowdesk;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            return args switch
            {
                ["start"] => OverlayProcess.Start(),
                ["stop"] => OverlayProcess.Stop(),
                ["status"] => OverlayProcess.Status(),
                ["background"] => OverlayProcess.Background(),
                ["background", "on"] => OverlayProcess.Background(true),
                ["background", "off"] => OverlayProcess.Background(false),
                ["accumulation"] => OverlayProcess.Accumulation(),
                ["accumulation", "on"] => OverlayProcess.Accumulation(true),
                ["accumulation", "off"] => OverlayProcess.Accumulation(false),
                ["density"] => OverlayProcess.Density(),
                ["density", var value] => SetDensity(value),
                ["--overlay", var readyName] => OverlayProcess.Run(readyName),
                [] or ["--help"] or ["-h"] => PrintUsage(Console.Out, 0),
                _ => PrintUsage(Console.Error, 2)
            };
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"snowdesk: {error.Message}");
            return 1;
        }
    }

    private static int SetDensity(string value)
    {
        if (SnowdeskSettings.TryParseDensity(value, out int density))
        {
            return OverlayProcess.Density(density);
        }

        Console.Error.WriteLine("snowdesk: density must be a whole number from 1 to 100.");
        return 2;
    }

    private static int PrintUsage(TextWriter output, int exitCode)
    {
        output.WriteLine("Usage: snowdesk <command> [value]");
        output.WriteLine("  start                  Start falling snow");
        output.WriteLine("  stop                   Remove the snow overlay");
        output.WriteLine("  status                 Show whether snowdesk is running");
        output.WriteLine("  background [on|off]    Show or set desktop-only snow");
        output.WriteLine("  density [1-100]        Show or set snowfall density (default 50)");
        output.WriteLine("  accumulation [on|off] Show or set interactive snow piles");
        return exitCode;
    }
}
