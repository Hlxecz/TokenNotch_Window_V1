using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TokenNotchWin;

internal sealed class PokemonSpriteAtlas
{
    private sealed record ActionFrames(BitmapSource[] Frames, int[] DurationsMs)
    {
        public int TotalDurationMs { get; } = DurationsMs.Sum();
    }

    private static readonly Dictionary<PokemonSpecies, PokemonSpriteAtlas> Cache = new();
    private static readonly HashSet<PokemonSpecies> Failed = [];
    private readonly Dictionary<string, ActionFrames> _actions;

    public int CellWidth { get; }
    public int CellHeight { get; }

    private PokemonSpriteAtlas(int cellWidth, int cellHeight,
        Dictionary<string, ActionFrames> actions)
    {
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        _actions = actions;
    }

    public static PokemonSpriteAtlas For(PokemonSpecies species)
    {
        if (Cache.TryGetValue(species, out var cached)) return cached;
        var loaded = Load(species);
        Cache[species] = loaded;
        return loaded;
    }

    public static bool IsAvailable(PetCharacter pet)
    {
        if (!PixelEvolution.IsPokemon(pet)) return true;

        return Enum.GetValues<Stage>()
            .Select(stage => PixelEvolution.SpeciesFor(pet, stage))
            .Distinct()
            .All(IsAvailable);
    }

    public static bool TryFor(PokemonSpecies species, out PokemonSpriteAtlas? atlas)
    {
        if (Failed.Contains(species))
        {
            atlas = null;
            return false;
        }

        try
        {
            atlas = For(species);
            return true;
        }
        catch (Exception error) when (error is IOException or JsonException
                                      or InvalidOperationException or ArgumentException)
        {
            Failed.Add(species);
            atlas = null;
            return false;
        }
    }

    public BitmapSource Frame(string action, double elapsedSeconds, bool holdLast = false)
    {
        if (!_actions.TryGetValue(action, out var animation))
            animation = _actions["idle"];

        if (holdLast && elapsedSeconds * 1000 >= animation.TotalDurationMs)
            return animation.Frames[^1];

        var elapsedMs = (int)Math.Max(0, elapsedSeconds * 1000);
        if (!holdLast && animation.TotalDurationMs > 0)
            elapsedMs %= animation.TotalDurationMs;

        var accumulated = 0;
        for (var i = 0; i < animation.Frames.Length; i++)
        {
            accumulated += animation.DurationsMs[i];
            if (elapsedMs < accumulated) return animation.Frames[i];
        }
        return animation.Frames[^1];
    }

    private static PokemonSpriteAtlas Load(PokemonSpecies species)
    {
        var (_, slug) = ResourceLocation(species);
        var root = ResourceRoot(species);
        using var manifestStream = OpenResource($"{root}/manifest.json");
        using var document = JsonDocument.Parse(manifestStream);
        var manifest = document.RootElement;
        var cellWidth = manifest.GetProperty("cell_width").GetInt32();
        var cellHeight = manifest.GetProperty("cell_height").GetInt32();
        var columns = manifest.GetProperty("columns").GetInt32();
        var rows = manifest.GetProperty("rows").GetInt32();

        using var atlasStream = OpenResource($"{root}/atlas.png");
        var atlas = BitmapFrame.Create(atlasStream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        atlas.Freeze();
        if (atlas.PixelWidth != cellWidth * columns || atlas.PixelHeight != cellHeight * rows)
            throw new InvalidOperationException($"Pokemon atlas dimensions do not match {slug} manifest.");

        var actions = new Dictionary<string, ActionFrames>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in manifest.GetProperty("actions").EnumerateArray())
        {
            var id = action.GetProperty("id").GetString()
                     ?? throw new InvalidOperationException($"Pokemon action id missing for {slug}.");
            var row = action.GetProperty("row").GetInt32();
            var frameCount = action.GetProperty("frame_count").GetInt32();
            var durations = action.GetProperty("durations_ms").EnumerateArray()
                .Select(value => Math.Max(1, value.GetInt32())).Take(frameCount).ToArray();
            if (durations.Length != frameCount || frameCount > columns || row >= rows)
                throw new InvalidOperationException($"Pokemon action metadata is invalid for {slug}/{id}.");

            var frames = new BitmapSource[frameCount];
            for (var column = 0; column < frameCount; column++)
            {
                var frame = new CroppedBitmap(atlas,
                    new Int32Rect(column * cellWidth, row * cellHeight, cellWidth, cellHeight));
                frame.Freeze();
                frames[column] = frame;
            }
            actions[id] = new ActionFrames(frames, durations);
        }

        if (!actions.ContainsKey("idle"))
            throw new InvalidOperationException($"Pokemon atlas {slug} has no idle action.");
        return new PokemonSpriteAtlas(cellWidth, cellHeight, actions);
    }

    private static Stream OpenResource(string path)
    {
        var uri = ResourceUri(path);
        return Application.GetResourceStream(uri)?.Stream
               ?? throw new InvalidOperationException($"Missing Pokemon resource: {uri}");
    }

    private static bool IsAvailable(PokemonSpecies species)
    {
        var root = ResourceRoot(species);
        return ResourceExists($"{root}/manifest.json") && ResourceExists($"{root}/atlas.png");
    }

    private static bool ResourceExists(string path)
    {
        try
        {
            using var stream = Application.GetResourceStream(ResourceUri(path))?.Stream;
            return stream is not null;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static Uri ResourceUri(string path) =>
        new($"/TokenNotchWin;component/{path}", UriKind.Relative);

    private static string ResourceRoot(PokemonSpecies species)
    {
        var (pack, slug) = ResourceLocation(species);
        return $"Resources/{pack}/{slug}";
    }

    private static (string Pack, string Slug) ResourceLocation(PokemonSpecies species) => species switch
    {
        PokemonSpecies.Charmander => ("pixel-evolution-actions", "charmander"),
        PokemonSpecies.Charmeleon => ("pixel-evolution-actions", "charmeleon"),
        PokemonSpecies.Charizard => ("pixel-evolution-actions", "charizard"),
        PokemonSpecies.Pikachu => ("pixel-pikachu-actions", "pikachu"),
        PokemonSpecies.Bulbasaur => ("pixel-bulbasaur-evolution-actions", "bulbasaur"),
        PokemonSpecies.Ivysaur => ("pixel-bulbasaur-evolution-actions", "ivysaur"),
        PokemonSpecies.Venusaur => ("pixel-bulbasaur-evolution-actions", "venusaur"),
        PokemonSpecies.Squirtle => ("pixel-squirtle-evolution-actions", "squirtle"),
        PokemonSpecies.Wartortle => ("pixel-squirtle-evolution-actions", "wartortle"),
        PokemonSpecies.Blastoise => ("pixel-squirtle-evolution-actions", "blastoise"),
        PokemonSpecies.Ditto => ("pixel-ditto-actions", "ditto"),
        PokemonSpecies.Snorlax => ("pixel-snorlax-actions", "snorlax"),
        PokemonSpecies.Arceus => ("pixel-legendary-actions", "arceus"),
        PokemonSpecies.Dialga => ("pixel-legendary-actions", "dialga"),
        PokemonSpecies.Palkia => ("pixel-legendary-actions", "palkia"),
        PokemonSpecies.Giratina => ("pixel-legendary-actions", "giratina"),
        PokemonSpecies.Mewtwo => ("pixel-legendary-actions", "mewtwo"),
        PokemonSpecies.Lugia => ("pixel-legendary-actions", "lugia"),
        PokemonSpecies.Kyogre => ("pixel-legendary-actions", "kyogre"),
        PokemonSpecies.Groudon => ("pixel-legendary-actions", "groudon"),
        PokemonSpecies.Rayquaza => ("pixel-legendary-actions", "rayquaza"),
        _ => throw new ArgumentOutOfRangeException(nameof(species), species, "Unknown Pokemon species."),
    };
}
