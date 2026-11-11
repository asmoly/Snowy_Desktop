using System.Globalization;

namespace Snowdesk;

internal static class SnowdeskSettings
{
    public const int DefaultDensity = 50;
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetEnvironmentVariable("LOCALAPPDATA")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Snowdesk");
    private static readonly string BackgroundMarker = Path.Combine(SettingsDirectory, "background-on");
    private static readonly string AccumulationMarker = Path.Combine(SettingsDirectory, "accumulation-on");
    private static readonly string DensityPath = Path.Combine(SettingsDirectory, "density");

    public static bool IsBackgroundEnabled() => File.Exists(BackgroundMarker);

    public static bool IsAccumulationEnabled() => File.Exists(AccumulationMarker);

    public static void SaveBackground(bool enabled) => SaveToggle(BackgroundMarker, enabled);

    public static void SaveAccumulation(bool enabled) => SaveToggle(AccumulationMarker, enabled);

    private static void SaveToggle(string path, bool enabled)
    {
        if (enabled)
        {
            Directory.CreateDirectory(SettingsDirectory);
            using var marker = File.Open(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static bool TryParseDensity(string value, out int density) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out density)
        && density is >= 1 and <= 100;

    public static int ReadDensity()
    {
        try
        {
            using var file = File.Open(DensityPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var reader = new StreamReader(file);
            return TryParseDensity(reader.ReadToEnd().Trim(), out int density) ? density : DefaultDensity;
        }
        catch (FileNotFoundException)
        {
            return DefaultDensity;
        }
        catch (DirectoryNotFoundException)
        {
            return DefaultDensity;
        }
    }

    public static void SaveDensity(int density)
    {
        Directory.CreateDirectory(SettingsDirectory);
        string temporaryPath = Path.Combine(SettingsDirectory, $"density.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, density.ToString(CultureInfo.InvariantCulture));
            File.Move(temporaryPath, DensityPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
