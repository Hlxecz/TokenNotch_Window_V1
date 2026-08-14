using System.IO;
using System.Text.Json;

namespace TokenNotchWin;

/// <summary>
/// Remembers where the user parked Clawd, so a locked position survives a
/// restart instead of the crab wandering off from the middle again.
/// </summary>
public sealed class Settings
{
    public bool Locked { get; set; }
    public double? X { get; set; }
    public double? BottomY { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TokenNotch", "settings.json");

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (IOException)
        {
            // A dropped preference isn't worth interrupting the widget for.
        }
    }
}
