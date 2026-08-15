using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace TokenNotchWin;

/// <summary>
/// Gets an expired Claude token renewed by running the CLI's own refresh path.
///
/// We deliberately do not POST to the OAuth endpoint and rewrite
/// .credentials.json ourselves: the refresh token rotates, so a request that
/// succeeds server-side but fails to land on disk would burn the only copy and
/// log the user out of Claude Code entirely. Letting the CLI do it keeps this
/// widget read-only over the credential file, exactly as the macOS original is.
///
/// The access token only lives ~8 hours, so without this the user has to open a
/// terminal and run `claude` by hand several times a day.
/// </summary>
public static class CredentialRefresher
{
    /// `mcp list` is the cheapest subcommand that still goes through the auth
    /// path — it renews the token without spending any of the usage this
    /// widget exists to report. It does not always exit on its own, so it is
    /// treated as fire-and-watch rather than run-to-completion.
    private const string RefreshArgs = "mcp list";

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// Long enough that a failing attempt can't turn into a process-spawn loop
    /// on every poll, short enough to recover well inside one token lifetime.
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    private static DateTime _lastAttempt = DateTime.MinValue;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static bool InCooldown => DateTime.UtcNow - _lastAttempt < Cooldown;

    /// Returns true only if the credential file came back with a token that is
    /// actually valid again — a changed file alone isn't proof of success.
    public static async Task<bool> TryRenewClaudeAsync(CancellationToken ct = default)
    {
        if (InCooldown) return false;
        if (!await Gate.WaitAsync(0, ct)) return false; // another attempt in flight

        try
        {
            _lastAttempt = DateTime.UtcNow;
            var exe = FindClaudeExecutable();
            if (exe is null) return false;

            var path = Credentials.ClaudePath;
            var before = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

            using var process = Start(exe);
            if (process is null) return false;

            try
            {
                var deadline = DateTime.UtcNow + Deadline;
                while (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(PollInterval, ct);

                    if (File.Exists(path) && File.GetLastWriteTimeUtc(path) != before)
                    {
                        // Give the CLI a moment to finish writing before we
                        // kill it — tearing down mid-write is the one way this
                        // could damage the login it's meant to repair.
                        await Task.Delay(500, ct);
                        return HasValidToken(path);
                    }

                    if (process.HasExited) return HasValidToken(path);
                }
                return false;
            }
            finally
            {
                TryKill(process);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A failed renewal is not worth surfacing on its own; the caller
            // still shows the underlying "token expired" message.
            return false;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static Process? Start(string exe)
    {
        var info = new ProcessStartInfo(exe, RefreshArgs)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        return Process.Start(info);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone, or we lost the race with its own exit.
        }
    }

    private static bool HasValidToken(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                || !oauth.TryGetProperty("expiresAt", out var expires)
                || !expires.TryGetDouble(out var ms))
                return false;

            return DateTimeOffset.FromUnixTimeMilliseconds((long)ms) > DateTimeOffset.UtcNow;
        }
        catch
        {
            return false;
        }
    }

    /// PATH first, then the two places the installers actually put it — a
    /// GUI app inherits the launching shell's PATH, which for a double-clicked
    /// exe may not include either location.
    private static string? FindClaudeExecutable()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>();

        if (Environment.GetEnvironmentVariable("PATH") is { } pathVar)
        {
            foreach (var dir in pathVar.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                candidates.Add(Path.Combine(dir, "claude.exe"));
            }
        }

        candidates.Add(Path.Combine(home, ".local", "bin", "claude.exe"));
        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd"));

        foreach (var candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // Malformed PATH entry — skip it.
            }
        }
        return null;
    }
}
