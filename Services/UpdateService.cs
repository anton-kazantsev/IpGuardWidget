using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IpGuard;

public sealed record UpdateInfo(
    string Tag,
    Version Version,
    string ReleaseName,
    DateTime PublishedAt,
    long SizeBytes,
    string DownloadUrl,
    string ReleaseUrl,
    IReadOnlyList<string> Changes);

public enum UpdateCheckStatus
{
    UpdateAvailable,
    UpToDate,
    Failed
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Info, string? Error);

/// <summary>
/// One download report: the percent for the bar, the byte counters and the time since the
/// download started — the banner needs the last two to estimate what is left.
/// </summary>
public sealed record DownloadProgress(double Percent, long ReceivedBytes, long TotalBytes, TimeSpan Elapsed);

/// <summary>
/// Looks for a newer build in GitHub Releases and downloads its exe. The banner is driven only by
/// <see cref="UpdateCheckStatus.UpdateAvailable"/>: a failed request never surfaces in the window.
/// </summary>
public sealed class UpdateService
{
    public const string Owner = "anton-kazantsev";
    public const string Repo = "IpGuardWidget";

    private const int MaxChanges = 12;
    private const int MaxChangeLength = 200;
    private const int ProgressReportIntervalMs = 100;

    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly Regex TagPattern = new(@"^[vV]?(\d+(?:\.\d+){1,3})", RegexOptions.Compiled);
    private static readonly Regex LinkPattern = new(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex EscapePattern = new(@"\\([\\`*_{}\[\]()#+\-.!>~|])", RegexOptions.Compiled);
    private static readonly Regex SpacePattern = new(@"[ \t]{2,}", RegexOptions.Compiled);

    private sealed record ReleaseCandidate(
        string Tag,
        Version Version,
        string Name,
        DateTime PublishedAt,
        long SizeBytes,
        string DownloadUrl,
        string ReleaseUrl,
        string Body);

    /// <summary>The version of the running exe (from the assembly, never hard-coded).</summary>
    public static Version CurrentVersion
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly;
            return Normalize(assembly.GetName().Version ?? new Version(0, 0, 0, 0));
        }
    }

    /// <summary>Fills missing components with 0 so that 1.2.0 and 1.2.0.0 compare equal.</summary>
    public static Version Normalize(Version version)
        => new(version.Major, Math.Max(version.Minor, 0), Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    /// <summary>1.2.0 — the form used in the banner title.</summary>
    public static string FormatVersion(Version version)
        => $"{version.Major}.{Math.Max(version.Minor, 0)}.{Math.Max(version.Build, 0)}";

    /// <summary>1.2 (or 1.2.1) — the short form used in the card footer.</summary>
    public static string ShortVersion(Version version)
        => version.Build > 0 ? FormatVersion(version) : $"{version.Major}.{Math.Max(version.Minor, 0)}";

    public static string FormatSize(long bytes)
        => (bytes / 1024d / 1024d).ToString("0.0", Ru) + " МБ";

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
    {
        var current = CurrentVersion;

        try
        {
            using var client = CreateClient(current);
            using var response = await client
                .GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases?per_page=10", ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Fail($"GitHub API вернул {(int)response.StatusCode} {response.ReasonPhrase}".Trim(), current);
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Fail("GitHub API вернул не список релизов", current);
            }

            var candidate = FindCandidate(doc.RootElement);
            if (candidate is null)
            {
                Log.Info($"Update: no usable release for current {current}, nothing to show");
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, null, null);
            }

            if (candidate.Version <= current)
            {
                Log.Info($"Update: current {current}, latest release {candidate.Tag} — up to date");
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, null, null);
            }

            var changes = await LoadChangesAsync(client, candidate.Tag, candidate.Body, ct).ConfigureAwait(false);
            var info = new UpdateInfo(
                candidate.Tag,
                candidate.Version,
                candidate.Name,
                candidate.PublishedAt,
                candidate.SizeBytes,
                candidate.DownloadUrl,
                candidate.ReleaseUrl,
                changes);

            Log.Info($"Update: {candidate.Tag} is newer than {current} " +
                     $"({FormatSize(candidate.SizeBytes)}, {changes.Count} bullets)");
            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, info, null);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message, current);
        }
    }

    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var directory = Path.Combine(AppPaths.DataDirectory, "update");
        Directory.CreateDirectory(directory);

        var target = Path.Combine(directory, AssetFileName(info));
        var partial = target + ".download";

        try
        {
            using var client = CreateClient(CurrentVersion);
            using var response = await client
                .GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? info.SizeBytes;
            long received = 0;
            long lastReport = -ProgressReportIntervalMs;
            var clock = Stopwatch.StartNew();

            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    received += read;

                    if (total > 0 && clock.ElapsedMilliseconds - lastReport >= ProgressReportIntervalMs)
                    {
                        lastReport = clock.ElapsedMilliseconds;
                        progress?.Report(new DownloadProgress(
                            Math.Clamp(received * 100.0 / total, 0, 100), received, total, clock.Elapsed));
                    }
                }
            }

            progress?.Report(new DownloadProgress(100, received, total, clock.Elapsed));
            File.Move(partial, target, overwrite: true);
            Log.Info($"Update: downloaded {info.Tag} to {target} ({FormatSize(received)})");
            return target;
        }
        catch (Exception ex)
        {
            // Never leave a half-written file behind: the next attempt would "resume" from it.
            TryDelete(partial);
            if (ex is OperationCanceledException)
            {
                Log.Info($"Update: download of {info.Tag} cancelled");
            }
            else
            {
                Log.Warn($"Update: download of {info.Tag} failed: {ex.Message}");
            }

            throw;
        }
    }

    /// <summary>
    /// Linear scan for the "## Что нового" section: bullets "- ..." up to the next "## " heading.
    /// </summary>
    public static IReadOnlyList<string> ParseChanges(string? readme)
    {
        if (string.IsNullOrWhiteSpace(readme))
        {
            return Array.Empty<string>();
        }

        var changes = new List<string>();
        var inside = false;

        foreach (var raw in SplitLines(readme))
        {
            var line = raw.Trim();

            if (IsHeading(line))
            {
                if (inside)
                {
                    break;
                }

                var title = line.TrimStart('#').Trim();
                if (title.StartsWith("Что нового", StringComparison.OrdinalIgnoreCase))
                {
                    inside = true;
                }

                continue;
            }

            if (!inside || !line.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            var text = CleanMarkdown(line[2..]);
            if (text.Length == 0)
            {
                continue;
            }

            changes.Add(text);
            if (changes.Count >= MaxChanges)
            {
                break;
            }
        }

        return changes;
    }

    /// <summary>Flat "- " list, used for the release body when README has no section.</summary>
    public static IReadOnlyList<string> ParseBulletList(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return Array.Empty<string>();
        }

        var changes = new List<string>();
        foreach (var raw in SplitLines(markdown))
        {
            var line = raw.Trim();
            if (!line.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            var text = CleanMarkdown(line[2..]);
            if (text.Length == 0)
            {
                continue;
            }

            changes.Add(text);
            if (changes.Count >= MaxChanges)
            {
                break;
            }
        }

        return changes;
    }

    public static string CleanMarkdown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var value = LinkPattern.Replace(text, "$1");
        value = value.Replace("**", "").Replace("__", "").Replace("`", "");
        value = EscapePattern.Replace(value, "$1");
        value = value
            .Replace("&quot;", "\"")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&amp;", "&");
        value = SpacePattern.Replace(value.Trim(), " ");

        if (value.Length > MaxChangeLength)
        {
            value = value[..(MaxChangeLength - 1)].TrimEnd() + "…";
        }

        return value;
    }

    private async Task<IReadOnlyList<string>> LoadChangesAsync(
        HttpClient client, string tag, string body, CancellationToken ct)
    {
        try
        {
            var url = $"https://raw.githubusercontent.com/{Owner}/{Repo}/{tag}/README.md";
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var markdown = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var changes = ParseChanges(markdown);
                if (changes.Count > 0)
                {
                    Log.Info($"Update: {changes.Count} bullets from README of {tag}");
                    return changes;
                }

                Log.Warn($"Update: README of {tag} has no «Что нового» section, using the release body");
            }
            else
            {
                Log.Warn($"Update: README of {tag} — HTTP {(int)response.StatusCode}, using the release body");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.Warn($"Update: README of {tag} timed out, using the release body");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn($"Update: cannot load README of {tag} ({ex.Message}), using the release body");
        }

        var fallback = ParseBulletList(body);
        if (fallback.Count == 0)
        {
            Log.Warn($"Update: release {tag} has no readable notes");
        }

        return fallback;
    }

    private static ReleaseCandidate? FindCandidate(JsonElement releases)
    {
        foreach (var release in releases.EnumerateArray())
        {
            if (GetBool(release, "draft") || GetBool(release, "prerelease"))
            {
                continue;
            }

            var tag = GetString(release, "tag_name");
            if (!TryParseTag(tag, out var version))
            {
                continue;
            }

            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            (string Url, long Size)? exe = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = GetString(asset, "name");
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var url = GetString(asset, "browser_download_url");
                if (!string.IsNullOrWhiteSpace(url))
                {
                    exe = (url, GetLong(asset, "size"));
                    break;
                }
            }

            if (exe is not { } assetFile)
            {
                continue;
            }

            var name2 = GetString(release, "name");
            return new ReleaseCandidate(
                tag,
                version,
                string.IsNullOrWhiteSpace(name2) ? tag : name2,
                ParseDate(GetString(release, "published_at")),
                assetFile.Size,
                assetFile.Url,
                GetString(release, "html_url"),
                GetString(release, "body"));
        }

        return null;
    }

    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var match = TagPattern.Match(tag.Trim());
        if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var parsed))
        {
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    private static HttpClient CreateClient(Version currentVersion)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.Zero,
            PooledConnectionIdleTimeout = TimeSpan.Zero,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"IpGuardWidget/{currentVersion} (Windows 10/11)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.ConnectionClose = true;
        return client;
    }

    private static UpdateCheckResult Fail(string message, Version current)
    {
        Log.Warn($"Update: check failed for current {current}: {message}");
        return new UpdateCheckResult(UpdateCheckStatus.Failed, null, message);
    }

    private static string AssetFileName(UpdateInfo info)
    {
        try
        {
            var name = Path.GetFileName(new Uri(info.DownloadUrl).AbsolutePath);
            if (!string.IsNullOrWhiteSpace(name) &&
                name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }
        catch (UriFormatException)
        {
            // fall through to a generated name
        }

        var tag = new string(info.Tag.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray());
        return $"IpGuardWidget-{tag}.exe";
    }

    private static IEnumerable<string> SplitLines(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static bool IsHeading(string line)
        => line.StartsWith("## ", StringComparison.Ordinal) || line == "##";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Update: cannot delete " + path + ": " + ex.Message);
        }
    }

    private static DateTime ParseDate(string value)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
        {
            return date.ToLocalTime();
        }

        return DateTime.Now;
    }

    private static string GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static bool GetBool(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static long GetLong(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : 0;
}
