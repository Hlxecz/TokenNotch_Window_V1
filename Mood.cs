namespace TokenNotchWin;

public enum Mood
{
    Happy,
    Worried,
    Critical,
    Sleeping,
}

public static class MoodRules
{
    /// TOKENNOTCH_MOOD=happy|worried|critical|sleeping forces every character
    /// into that mood — for previewing animations without burning quota.
    private static readonly Mood? DemoOverride =
        Environment.GetEnvironmentVariable("TOKENNOTCH_MOOD")?.ToLowerInvariant() switch
        {
            "happy" => Mood.Happy,
            "worried" => Mood.Worried,
            "critical" => Mood.Critical,
            "sleeping" => Mood.Sleeping,
            _ => null,
        };

    public static Mood From(UsageWindow? window, bool hasError)
    {
        if (DemoOverride is { } forced) return forced;
        if (hasError) return Mood.Sleeping;
        if (window?.RemainingPercent is not { } remaining) return Mood.Sleeping;
        if (remaining > 50) return Mood.Happy;
        if (remaining > 20) return Mood.Worried;
        return Mood.Critical;
    }
}
