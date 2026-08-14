using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TokenNotchWin;

public sealed record UsageWindow(double? Utilization, DateTime? ResetsAt)
{
    public double? RemainingPercent => Utilization is { } u ? Math.Max(0, 100 - u) : null;
}

public sealed record ClaudeUsage(
    UsageWindow? FiveHour,
    UsageWindow? SevenDay,
    UsageWindow? SevenDaySonnet,
    UsageWindow? SevenDayOpus);

public sealed record CodexUsage(
    UsageWindow? FiveHour,
    UsageWindow? SevenDay,
    string? PlanType);

public sealed class UsageException(string message) : Exception(message);

/// <summary>
/// Windows has no login keychain equivalent, so Claude Code stores its OAuth
/// blob as a plain file under the user profile instead.
/// </summary>
public static class Credentials
{
    public static string ClaudePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", ".credentials.json");

    public static string CodexPath =>
        Path.Combine(
            Environment.GetEnvironmentVariable("CODEX_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"),
            "auth.json");

    public static string ClaudeAccessToken()
    {
        if (!File.Exists(ClaudePath))
            throw new UsageException("Claude 자격증명을 찾을 수 없어요. `claude` 로그인 후 다시 시도하세요.");

        using var doc = JsonDocument.Parse(File.ReadAllText(ClaudePath));
        if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
            || !oauth.TryGetProperty("accessToken", out var tokenNode)
            || tokenNode.GetString() is not { Length: > 0 } token)
            throw new UsageException("Claude 자격증명 형식을 읽을 수 없어요.");

        if (oauth.TryGetProperty("expiresAt", out var expiresNode)
            && expiresNode.TryGetDouble(out var expiresMs)
            && DateTimeOffset.FromUnixTimeMilliseconds((long)expiresMs) < DateTimeOffset.UtcNow)
            throw new UsageException("Claude 토큰이 만료됐어요. `claude`를 한 번 실행하면 갱신됩니다.");

        return token;
    }

    public static (string Token, string AccountId) CodexAuth()
    {
        if (!File.Exists(CodexPath))
            throw new UsageException("Codex 자격증명을 찾을 수 없어요. `codex` 로그인 후 다시 시도하세요.");

        using var doc = JsonDocument.Parse(File.ReadAllText(CodexPath));
        if (!doc.RootElement.TryGetProperty("tokens", out var tokens)
            || tokens.GetPropertyOrNull("access_token")?.GetString() is not { Length: > 0 } token
            || tokens.GetPropertyOrNull("account_id")?.GetString() is not { Length: > 0 } accountId)
            throw new UsageException("Codex 자격증명 형식을 읽을 수 없어요.");

        return (token, accountId);
    }
}

public static class UsageApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<ClaudeUsage> FetchClaudeAsync()
    {
        var token = Credentials.ClaudeAccessToken();

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        // The endpoint gates on a CLI User-Agent — anything else lands in an
        // aggressively rate-limited bucket with a sticky (~10 min) 429.
        request.Headers.TryAddWithoutValidation("User-Agent", "claude-code/2.1.121");

        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new UsageException($"HTTP {(int)response.StatusCode}");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        return new ClaudeUsage(
            IsoWindow(root, "five_hour"),
            IsoWindow(root, "seven_day"),
            IsoWindow(root, "seven_day_sonnet"),
            IsoWindow(root, "seven_day_opus"));
    }

    public static async Task<CodexUsage> FetchCodexAsync()
    {
        var (token, accountId) = Credentials.CodexAuth();

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("ChatGPT-Account-Id", accountId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new UsageException(response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? "Codex 토큰이 만료됐어요. `codex`를 한 번 실행하면 갱신됩니다."
                : $"HTTP {(int)response.StatusCode}");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rateLimit = doc.RootElement.GetPropertyOrNull("rate_limit");

        // Which window is the session vs. weekly limit varies by plan (a Plus
        // account can report only a weekly primary_window), so classify by
        // window length instead of position.
        UsageWindow? session = null, weekly = null;
        foreach (var key in new[] { "primary_window", "secondary_window" })
        {
            if (rateLimit?.GetPropertyOrNull(key) is not { } dict) continue;
            if (EpochWindow(dict) is not { } parsed) continue;

            var seconds = dict.GetPropertyOrNull("limit_window_seconds")?.GetDoubleOrNull() ?? 0;
            if (seconds > 0 && seconds <= 6 * 3600) session ??= parsed;
            else weekly ??= parsed;
        }

        return new CodexUsage(session, weekly, doc.RootElement.GetPropertyOrNull("plan_type")?.GetString());
    }

    private static UsageWindow? IsoWindow(JsonElement parent, string name)
    {
        if (parent.GetPropertyOrNull(name) is not { } node) return null;
        var utilization = node.GetPropertyOrNull("utilization")?.GetDoubleOrNull();
        var resetsAt = node.GetPropertyOrNull("resets_at")?.GetString();
        DateTime? reset = DateTime.TryParse(resetsAt, out var parsed) ? parsed.ToLocalTime() : null;
        return utilization is null && reset is null ? null : new UsageWindow(utilization, reset);
    }

    /// Field names have drifted across Codex versions (reset_at vs resets_at),
    /// so parsing stays defensive.
    private static UsageWindow? EpochWindow(JsonElement dict)
    {
        var used = dict.GetPropertyOrNull("used_percent")?.GetDoubleOrNull();
        var epoch = (dict.GetPropertyOrNull("reset_at") ?? dict.GetPropertyOrNull("resets_at"))?.GetDoubleOrNull();
        if (used is null && epoch is null) return null;
        return new UsageWindow(used, epoch is { } e ? DateTimeOffset.FromUnixTimeSeconds((long)e).LocalDateTime : null);
    }
}

internal static class JsonExtensions
{
    public static JsonElement? GetPropertyOrNull(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value
            : null;

    public static double? GetDoubleOrNull(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value) ? value : null;
}
