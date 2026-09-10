using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace TokenNotchWin;

public sealed class UsageViewModel : INotifyPropertyChanged
{
    private ClaudeUsage? _claude;
    private CodexUsage? _codex;
    private string? _claudeError;
    private string? _codexError;
    private DateTime? _lastUpdated;

    /// Once the usage endpoint's limiter trips it stays tripped for ~10 min,
    /// so retrying on the normal cadence just re-arms the penalty.
    private DateTime? _claudeCooldownUntil;

    /// Last utilization we saw for each service's primary window, used to turn
    /// the endpoint's absolute reading into an increment. Tracked separately
    /// so Claude and Codex windows can reset independently without one's
    /// rollover being mistaken for the other's.
    private double? _lastClaudeUtilization;
    private double? _lastCodexUtilization;

    /// Lifetime total of those increments — the pet's experience bar. Seeded
    /// from settings on construction and reported through UsagePointsChanged
    /// so the host can persist it.
    public double CumulativeUsagePoints { get; private set; }

    /// Raised when the total moves, so the host can save without polling.
    public event Action<double>? UsagePointsChanged;

    public UsageViewModel(double startingPoints = 0) => CumulativeUsagePoints = startingPoints;

    public Stage Stage => PixelEvolution.StageFor(CumulativeUsagePoints);

    /// Folds a fresh utilization reading into the lifetime total. Only rises
    /// count: when a window resets, utilization drops back toward zero, and
    /// treating that as negative progress would un-evolve the pet. Shared by
    /// both services — Claude and Codex usage both feed the same pet.
    private void AccumulateUsage(double? utilization, ref double? lastUtilization)
    {
        if (utilization is not { } now) return;

        if (lastUtilization is { } previous)
        {
            // A drop means the window rolled over; the usage since that reset
            // is whatever the new reading already shows.
            var gained = now >= previous ? now - previous : now;
            if (gained > 0)
            {
                CumulativeUsagePoints += gained;
                UsagePointsChanged?.Invoke(CumulativeUsagePoints);
            }
        }
        lastUtilization = now;
    }

    public UsageWindow? FiveHour => _claude?.FiveHour;
    public UsageWindow? SevenDay => _claude?.SevenDay;
    public UsageWindow? SevenDayOpus => _claude?.SevenDayOpus;
    public UsageWindow? SevenDaySonnet => _claude?.SevenDaySonnet;

    /// A Plus plan may only report a weekly window — show whichever exists.
    public UsageWindow? CodexWindow => _codex?.FiveHour ?? _codex?.SevenDay;
    public UsageWindow? CodexFiveHour => _codex?.FiveHour;
    public UsageWindow? CodexSevenDay => _codex?.SevenDay;

    public string? ClaudeError => _claudeError;
    public string? CodexError => _codexError;

    public Mood ClaudeMood => MoodRules.From(FiveHour, _claudeError is not null);
    public Mood CodexMood => MoodRules.From(CodexWindow, _codexError is not null);

    public string ClaudePercentText => Format.Percent(FiveHour?.RemainingPercent);
    public string CodexPercentText => Format.Percent(CodexWindow?.RemainingPercent);
    public Brush ClaudePercentBrush => Format.StatusBrush(FiveHour?.RemainingPercent);
    public Brush CodexPercentBrush => Format.StatusBrush(CodexWindow?.RemainingPercent);

    public string ClaudePhrase => Format.Phrase(ClaudeMood);
    public string CodexPhrase => Format.Phrase(CodexMood);

    public Mood MoodFor(AiProvider provider) => provider switch
    {
        AiProvider.Codex => CodexMood,
        _ => ClaudeMood,
    };

    public string PercentTextFor(AiProvider provider) => provider switch
    {
        AiProvider.Codex => CodexPercentText,
        _ => ClaudePercentText,
    };

    public Brush PercentBrushFor(AiProvider provider) => provider switch
    {
        AiProvider.Codex => CodexPercentBrush,
        _ => ClaudePercentBrush,
    };

    public string CodexTitle => _codex?.PlanType is { Length: > 0 } plan
        ? $"Codex ({char.ToUpper(plan[0])}{plan[1..]})"
        : "Codex";

    public string LastUpdatedText => _lastUpdated is { } d ? d.ToString("HH:mm") : "—";

    public async Task RefreshAsync()
    {
        await Task.WhenAll(RefreshClaudeAsync(), RefreshCodexAsync());
        RaiseAll();
    }

    private async Task RefreshClaudeAsync()
    {
        if (_claudeCooldownUntil is { } until && DateTime.UtcNow < until) return;

        try
        {
            _claude = await UsageApi.FetchClaudeAsync();
            AccumulateUsage(_claude.FiveHour?.Utilization, ref _lastClaudeUtilization);
            _claudeError = null;
            _lastUpdated = DateTime.Now;
        }
        catch (TokenExpiredException ex)
        {
            // The token only lasts ~8 hours, so rather than telling the user to
            // go open a terminal, have the CLI renew it and try again.
            _claudeError = ex.Message;
            RaiseAll();

            if (await CredentialRefresher.TryRenewClaudeAsync())
            {
                try
                {
                    _claude = await UsageApi.FetchClaudeAsync();
                    AccumulateUsage(_claude.FiveHour?.Utilization, ref _lastClaudeUtilization);
                    _claudeError = null;
                    _lastUpdated = DateTime.Now;
                    return;
                }
                catch (Exception retry)
                {
                    _claudeError = retry.Message;
                    return;
                }
            }

            _claudeError = CredentialRefresher.InCooldown
                ? "Claude 토큰이 만료됐어요. `claude`를 한 번 실행해 주세요."
                : "Claude 자동 갱신에 실패했어요. `claude`를 한 번 실행해 주세요.";
        }
        catch (UsageException ex) when (ex.Message.Contains("429"))
        {
            _claudeCooldownUntil = DateTime.UtcNow.AddMinutes(15);
            // Transient: keep showing the last data if we have any.
            if (_claude is null) _claudeError = "잠시 후 다시 시도할게요 (요청 제한)";
        }
        catch (Exception ex)
        {
            _claudeError = ex.Message;
        }
    }

    private async Task RefreshCodexAsync()
    {
        try
        {
            _codex = await UsageApi.FetchCodexAsync();
            // A Plus plan may only ever report the weekly window, so fall back
            // the same way CodexWindow does — otherwise those accounts would
            // never accumulate anything.
            AccumulateUsage(_codex.FiveHour?.Utilization ?? _codex.SevenDay?.Utilization, ref _lastCodexUtilization);
            _codexError = null;
            _lastUpdated = DateTime.Now;
        }
        catch (Exception ex)
        {
            _codexError = ex.Message;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseAll()
    {
        foreach (var name in new[]
                 {
                     nameof(FiveHour), nameof(SevenDay), nameof(SevenDayOpus), nameof(SevenDaySonnet),
                     nameof(CodexWindow), nameof(CodexFiveHour), nameof(CodexSevenDay),
                     nameof(ClaudeError), nameof(CodexError), nameof(ClaudeMood), nameof(CodexMood),
                     nameof(ClaudePercentText), nameof(CodexPercentText),
                     nameof(ClaudePercentBrush), nameof(CodexPercentBrush),
                     nameof(ClaudePhrase), nameof(CodexPhrase),
                     nameof(CodexTitle), nameof(LastUpdatedText),
                 })
        {
            Raise(name);
        }
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class Format
{
    public static string Percent(double? value) =>
        value is { } v ? $"{Math.Round(v)}%" : "—";

    public static string ClockTime(DateTime? date) =>
        date is { } d ? d.ToString("HH:mm") : "—";

    public static string DayTime(DateTime? date) =>
        date is { } d ? d.ToString("M/d HH:mm") : "—";

    public static string Countdown(DateTime? date)
    {
        if (date is not { } d) return "—";
        var span = d - DateTime.Now;
        if (span <= TimeSpan.Zero) return "곧 리셋";
        if (span.TotalHours >= 24) return $"{(int)span.TotalDays}일 {span.Hours}시간 후";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}시간 {span.Minutes}분 후";
        return $"{span.Minutes}분 후";
    }

    public static Brush StatusBrush(double? remaining)
    {
        var color = remaining switch
        {
            null => Color.FromRgb(150, 150, 150),
            > 50 => Color.FromRgb(140, 217, 115),
            > 20 => Color.FromRgb(255, 204, 89),
            _ => Color.FromRgb(255, 115, 115),
        };
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static string Phrase(Mood mood) => mood switch
    {
        Mood.Happy => "아직 든든해요!",
        Mood.Worried => "아껴 써야 해요…",
        Mood.Critical => "거의 다 썼어요!!",
        _ => "쉬는 중… zzz",
    };
}
