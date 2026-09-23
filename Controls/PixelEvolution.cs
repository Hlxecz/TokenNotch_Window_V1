namespace TokenNotchWin;

public enum Stage
{
    Charmander = 0,
    Charmeleon = 1,
    Charizard = 2,
}

public enum PokemonSpecies
{
    Charmander,
    Charmeleon,
    Charizard,
    Pikachu,
    Bulbasaur,
    Ivysaur,
    Venusaur,
    Squirtle,
    Wartortle,
    Blastoise,
    Ditto,
    Snorlax,
    Arceus,
    Dialga,
    Palkia,
    Giratina,
    Mewtwo,
    Lugia,
    Kyogre,
    Groudon,
    Rayquaza,
}

public static class PixelEvolution
{
    public const double CharmeleonAt = 100;
    public const double CharizardAt = 300;

    public static Stage StageFor(double points) => points switch
    {
        >= CharizardAt => Stage.Charizard,
        >= CharmeleonAt => Stage.Charmeleon,
        _ => Stage.Charmander,
    };

    public static string Name(Stage stage) => stage switch
    {
        Stage.Charmander => "파이리",
        Stage.Charmeleon => "리자드",
        _ => "리자몽",
    };

    public static bool IsPokemon(PetCharacter pet) => pet is
        PetCharacter.PixelCharmander or
        PetCharacter.PixelPikachu or
        PetCharacter.PixelBulbasaur or
        PetCharacter.PixelSquirtle or
        PetCharacter.Ditto or
        PetCharacter.Snorlax or
        PetCharacter.Arceus or
        PetCharacter.Dialga or
        PetCharacter.Palkia or
        PetCharacter.Giratina or
        PetCharacter.Mewtwo or
        PetCharacter.Lugia or
        PetCharacter.Kyogre or
        PetCharacter.Groudon or
        PetCharacter.Rayquaza;

    public static bool Evolves(PetCharacter pet) => pet is
        PetCharacter.PixelCharmander or
        PetCharacter.PixelBulbasaur or
        PetCharacter.PixelSquirtle;

    public static PokemonSpecies SpeciesFor(PetCharacter pet, Stage stage) => pet switch
    {
        PetCharacter.PixelCharmander => stage switch
        {
            Stage.Charmander => PokemonSpecies.Charmander,
            Stage.Charmeleon => PokemonSpecies.Charmeleon,
            _ => PokemonSpecies.Charizard,
        },
        PetCharacter.PixelPikachu => PokemonSpecies.Pikachu,
        PetCharacter.PixelBulbasaur => stage switch
        {
            Stage.Charmander => PokemonSpecies.Bulbasaur,
            Stage.Charmeleon => PokemonSpecies.Ivysaur,
            _ => PokemonSpecies.Venusaur,
        },
        PetCharacter.PixelSquirtle => stage switch
        {
            Stage.Charmander => PokemonSpecies.Squirtle,
            Stage.Charmeleon => PokemonSpecies.Wartortle,
            _ => PokemonSpecies.Blastoise,
        },
        PetCharacter.Ditto => PokemonSpecies.Ditto,
        PetCharacter.Snorlax => PokemonSpecies.Snorlax,
        PetCharacter.Arceus => PokemonSpecies.Arceus,
        PetCharacter.Dialga => PokemonSpecies.Dialga,
        PetCharacter.Palkia => PokemonSpecies.Palkia,
        PetCharacter.Giratina => PokemonSpecies.Giratina,
        PetCharacter.Mewtwo => PokemonSpecies.Mewtwo,
        PetCharacter.Lugia => PokemonSpecies.Lugia,
        PetCharacter.Kyogre => PokemonSpecies.Kyogre,
        PetCharacter.Groudon => PokemonSpecies.Groudon,
        PetCharacter.Rayquaza => PokemonSpecies.Rayquaza,
        _ => throw new ArgumentOutOfRangeException(nameof(pet), pet, "Not a Pokemon pet."),
    };

    public static string Name(PetCharacter pet, Stage stage) => SpeciesFor(pet, stage) switch
    {
        PokemonSpecies.Charmander => "파이리",
        PokemonSpecies.Charmeleon => "리자드",
        PokemonSpecies.Charizard => "리자몽",
        PokemonSpecies.Pikachu => "피카츄",
        PokemonSpecies.Bulbasaur => "이상해씨",
        PokemonSpecies.Ivysaur => "이상해풀",
        PokemonSpecies.Venusaur => "이상해꽃",
        PokemonSpecies.Squirtle => "꼬부기",
        PokemonSpecies.Wartortle => "어니부기",
        PokemonSpecies.Blastoise => "거북왕",
        PokemonSpecies.Ditto => "메타몽",
        PokemonSpecies.Snorlax => "잠만보",
        PokemonSpecies.Arceus => "아르세우스",
        PokemonSpecies.Dialga => "디아루가",
        PokemonSpecies.Palkia => "펄기아",
        PokemonSpecies.Giratina => "기라티나",
        PokemonSpecies.Mewtwo => "뮤츠",
        PokemonSpecies.Lugia => "루기아",
        PokemonSpecies.Kyogre => "가이오가",
        PokemonSpecies.Groudon => "그란돈",
        PokemonSpecies.Rayquaza => "레쿠쟈",
        _ => throw new ArgumentOutOfRangeException(nameof(pet), pet, "Unknown Pokemon species."),
    };

    public static (double Fraction, double Remaining) Progress(double points)
    {
        var (from, to) = StageFor(points) switch
        {
            Stage.Charmander => (0.0, CharmeleonAt),
            Stage.Charmeleon => (CharmeleonAt, CharizardAt),
            _ => (CharizardAt, CharizardAt),
        };
        if (to <= from) return (1.0, 0);
        return (Math.Clamp((points - from) / (to - from), 0, 1), Math.Max(0, to - points));
    }
}
