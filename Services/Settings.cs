using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TokenNotchWin;

/// Which sprite the widget shows. Pokemon families can share the cumulative
/// growth stages while Clawd keeps its own visual behavior.
public enum PetCharacter
{
    Clawd,
    PixelCharmander,
    PixelPikachu,
    PixelBulbasaur,
    PixelSquirtle,
    Ditto,
    Snorlax,
}

/// <summary>
/// The service that drives the floating pet's mood, movement, and compact
/// percentage badge. Both services are still shown in the expanded panel.
/// </summary>
public enum AiProvider
{
    Claude,
    Codex,
}

/// <summary>
/// Remembers where the user parked Clawd, so a locked position survives a
/// restart instead of the crab wandering off from the middle again.
/// </summary>
public sealed class Settings
{
    public bool Locked { get; set; }
    public double? X { get; set; }
    public double? BottomY { get; set; }

    /// Lifetime sum of every rise in the 5-hour window's utilization we've
    /// ever observed — never decreases, even when the window itself resets.
    /// Purely cosmetic (drives the evolution stage); not a real usage figure.
    public double CumulativeUsagePoints { get; set; }

    /// Allows old 1%-equals-1-point saves to be upgraded once when the faster
    /// experience scale changes.
    public int ExperienceScaleVersion { get; set; }

    /// Which pet is on screen. Stored as a string so an unknown value from a
    /// newer build degrades to the default instead of throwing.
    public string Character { get; set; } = nameof(PetCharacter.Clawd);

    /// "Auto" follows cumulative usage. A stage name keeps an unlocked
    /// evolution family on that form until the user switches it again.
    public string EvolutionStage { get; set; } = "Auto";

    /// The provider whose live usage drives the always-visible widget.
    public string MainProvider { get; set; } = nameof(AiProvider.Claude);

    /// User-controlled order of the provider cards in the expanded panel.
    public List<string>? CardOrder { get; set; } =
        [nameof(AiProvider.Claude), nameof(AiProvider.Codex)];

    /// Derived from Character, so it must not round-trip into the file.
    [JsonIgnore]
    public PetCharacter Pet =>
        Enum.TryParse<PetCharacter>(Character, ignoreCase: true, out var pet)
            ? pet
            : PetCharacter.Clawd;

    [JsonIgnore]
    public Stage? PreferredEvolutionStage =>
        Enum.TryParse<Stage>(EvolutionStage, ignoreCase: true, out var stage)
            ? stage
            : null;

    [JsonIgnore]
    public AiProvider PrimaryProvider =>
        Enum.TryParse<AiProvider>(MainProvider, ignoreCase: true, out var provider)
            ? provider
            : AiProvider.Claude;

    /// Unknown, duplicate, or missing values are repaired in memory so older
    /// settings files and future provider additions remain usable.
    [JsonIgnore]
    public IReadOnlyList<AiProvider> OrderedProviders
    {
        get
        {
            var providers = new List<AiProvider>();
            foreach (var value in CardOrder ?? [])
            {
                if (Enum.TryParse<AiProvider>(value, ignoreCase: true, out var provider)
                    && !providers.Contains(provider))
                    providers.Add(provider);
            }

            foreach (var provider in Enum.GetValues<AiProvider>())
            {
                if (!providers.Contains(provider)) providers.Add(provider);
            }
            return providers;
        }
    }

    public void SetCardOrder(IEnumerable<AiProvider> providers) =>
        CardOrder = providers.Distinct().Select(provider => provider.ToString()).ToList();

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
