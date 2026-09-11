using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexBeacon;

/// <summary>One downloadable file attached to a GitHub release.</summary>
public sealed class UpdateAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}

/// <summary>Raw shape of <c>GET /repos/{owner}/{repo}/releases/latest</c>.</summary>
public sealed class UpdateRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("body")] public string Body { get; set; } = "";
    [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
    [JsonPropertyName("published_at")] public string PublishedAt { get; set; } = "";
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("assets")] public List<UpdateAsset> Assets { get; set; } = [];
}

/// <summary>Normalised result of a check, ready for the view layer.</summary>
public sealed class UpdateCheckResult
{
    public bool Succeeded { get; init; } = true;
    public string? ErrorDetail { get; init; }
    public bool HasUpdate { get; init; }
    public string CurrentVersion { get; init; } = "";
    public string LatestVersion { get; init; } = "";
    public string ReleaseName { get; init; } = "";
    public string ReleaseNotes { get; init; } = "";
    public string ReleaseUrl { get; init; } = "";
    public string PublishedAt { get; init; } = "";
    public string AssetName { get; init; } = "";
    public string AssetUrl { get; init; } = "";
    public long AssetSize { get; init; }
    public string ChecksumAssetUrl { get; init; } = "";

    /// <summary>A newer release exists but carries no package for this build.</summary>
    public bool AssetMissing => HasUpdate && string.IsNullOrWhiteSpace(AssetUrl);
}

/// <summary>Streamed download telemetry.</summary>
public sealed class UpdateProgress
{
    public long Received { get; init; }
    public long Total { get; init; }
    public double BytesPerSecond { get; init; }
    public bool Indeterminate => Total <= 0;
    public double Fraction => Total > 0 ? Math.Clamp((double)Received / Total, 0d, 1d) : 0d;
}

/// <summary>
/// Self-update against the project's official GitHub Releases channel.
///
/// The distributed artifacts are single-file launcher executables that expand
/// their embedded payload into <c>%LOCALAPPDATA%\CodexBeacon\app-portable</c> and
/// then exit. Because the launcher has already terminated by the time this
/// application is running, its file on disk is not locked and can be replaced in
/// place; the refreshed launcher re-expands its payload on the next start because
/// the extraction stamp no longer matches. The running executable itself is
/// always locked, so the swap is performed by a detached helper that waits for
/// this process to exit.
/// </summary>
public sealed class UpdateService
{
    private const string RepositoryOwner = "hurmuri";
    private const string RepositoryName = "codex-beacon";
    private const string PortableAssetName = "CodexBeacon-portable.exe";
    private const string SlimAssetName = "CodexBeacon-slim.exe";
    private const string ChecksumAssetName = "SHA256SUMS.txt";
    private const string LauncherArgument = "--launcher";

    /// <summary>
    /// Environment hook that redirects release metadata to another endpoint. It
    /// exists so the update pipeline can be exercised end to end without cutting a
    /// real release, and so an organisation can point the app at its own mirror.
    /// Only HTTPS or a loopback address is accepted: a plaintext remote feed would
    /// let anything on the network choose the binary this app downloads.
    /// </summary>
    private const string FeedOverrideVariable = "CODEXBEACON_UPDATE_FEED";

    private static readonly Uri LatestReleaseUri = ResolveLatestReleaseUri();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public UpdateService()
    {
        CurrentVersion = ReadCurrentVersion();
        IsSelfContained = File.Exists(Path.Combine(AppContext.BaseDirectory, "coreclr.dll"));
        LauncherPath = ReadLauncherPath();
        _http = CreateClient(CurrentVersion);
    }

    /// <summary>Marketing version of the running build, e.g. <c>0.3.4</c>.</summary>
    public string CurrentVersion { get; }

    /// <summary>
    /// Self-contained (portable) builds ship the .NET runtime next to the app;
    /// framework-dependent (slim) builds do not. Verified against the two publish
    /// profiles: only the portable output contains <c>coreclr.dll</c>.
    /// </summary>
    public bool IsSelfContained { get; }

    public string PackageAssetName => IsSelfContained ? PortableAssetName : SlimAssetName;

    /// <summary>Path of the single-file launcher that started this build, when known.</summary>
    public string? LauncherPath { get; }

    public string ReleasesPageUrl => $"https://github.com/{RepositoryOwner}/{RepositoryName}/releases/latest";

    /// <summary>True when the launcher can be replaced without elevation.</summary>
    public bool CanApplyInPlace =>
        !string.IsNullOrWhiteSpace(LauncherPath) && File.Exists(LauncherPath);

    public static string UpdateDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexBeacon", "updates");

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return Failure($"HTTP {(int)response.StatusCode}");

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var release = await JsonSerializer
                .DeserializeAsync<UpdateRelease>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return release is null ? Failure("empty response") : Describe(release);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
    }

    /// <summary>
    /// Downloads the package for this build into the updates directory, reporting
    /// progress, then verifies it against the release's <c>SHA256SUMS.txt</c> when
    /// that asset is published. Returns the path of the verified file.
    /// </summary>
    public async Task<string> DownloadAsync(
        UpdateCheckResult check,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(check.AssetUrl))
            throw new InvalidOperationException(Localization.Get("UpdateAssetMissing"));

        Directory.CreateDirectory(UpdateDirectory);
        var target = Path.Combine(UpdateDirectory, check.AssetName);
        var partial = target + ".download";

        var expectedHash = await TryGetExpectedHashAsync(check.AssetName, check.ChecksumAssetUrl, cancellationToken)
            .ConfigureAwait(false);

        using (var response = await _http
            .GetAsync(check.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? check.AssetSize;

            await using var source = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = File.Create(partial);

            var buffer = new byte[81920];
            long received = 0;
            var stopwatch = Stopwatch.StartNew();
            var lastReport = TimeSpan.Zero;

            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0) break;

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;

                if (progress is null || (stopwatch.Elapsed - lastReport).TotalMilliseconds < 100) continue;
                lastReport = stopwatch.Elapsed;
                progress.Report(Measure(received, total, stopwatch.Elapsed));
            }

            progress?.Report(Measure(received, total, stopwatch.Elapsed));
        }

        if (expectedHash is not null)
        {
            var actual = await ComputeHashAsync(partial, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(partial);
                throw new InvalidOperationException(
                    Localization.Format("UpdateVerifyFailed", check.AssetName));
            }
        }

        File.Move(partial, target, true);
        return target;
    }

    /// <summary>
    /// Launches a detached helper that waits for this process to exit, replaces the
    /// launcher executable, and starts it again. The caller must close the window
    /// once this returns.
    /// </summary>
    public void ApplyAndRestart(string downloadedPath)
    {
        if (!CanApplyInPlace)
            throw new InvalidOperationException(Localization.Get("UpdateNoLauncherHint"));

        Directory.CreateDirectory(UpdateDirectory);
        var script = Path.Combine(UpdateDirectory, "apply-update.ps1");
        var log = Path.Combine(UpdateDirectory, "apply-update.log");
        File.WriteAllText(script, HelperScript, new UTF8Encoding(true));

        var arguments = string.Join(' ',
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden",
            "-File", Quote(script),
            "-ProcessId", Environment.ProcessId.ToString(),
            "-Source", Quote(downloadedPath),
            "-Target", Quote(LauncherPath!),
            "-LogPath", Quote(log));

        var info = new ProcessStartInfo("powershell.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        Process.Start(info);
    }

    // ------------------------------------------------------------------- helpers

    private static UpdateProgress Measure(long received, long total, TimeSpan elapsed)
        => new()
        {
            Received = received,
            Total = total,
            BytesPerSecond = received / Math.Max(elapsed.TotalSeconds, 0.001)
        };

    private static UpdateCheckResult Failure(string detail) => new()
    {
        Succeeded = false,
        ErrorDetail = detail
    };

    private UpdateCheckResult Describe(UpdateRelease release)
    {
        var latest = NormalizeVersion(release.TagName);
        var asset = release.Assets.FirstOrDefault(
            candidate => string.Equals(candidate.Name, PackageAssetName, StringComparison.OrdinalIgnoreCase));
        var checksum = release.Assets.FirstOrDefault(
            candidate => string.Equals(candidate.Name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase));

        return new UpdateCheckResult
        {
            Succeeded = true,
            HasUpdate = CompareVersions(latest, CurrentVersion) > 0,
            CurrentVersion = CurrentVersion,
            LatestVersion = latest,
            ReleaseName = string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
            ReleaseNotes = release.Body,
            ReleaseUrl = release.HtmlUrl,
            PublishedAt = release.PublishedAt,
            AssetName = asset?.Name ?? PackageAssetName,
            AssetUrl = asset?.DownloadUrl ?? "",
            AssetSize = asset?.Size ?? 0,
            ChecksumAssetUrl = checksum?.DownloadUrl ?? ""
        };
    }

    private async Task<string?> TryGetExpectedHashAsync(
        string assetName, string checksumUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(checksumUrl)) return null;

        try
        {
            var text = await _http.GetStringAsync(checksumUrl, cancellationToken).ConfigureAwait(false);
            foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                // The release workflow writes this file with `Set-Content -Encoding
                // utf8`, which prefixes a BOM under Windows PowerShell 5.1. A BOM is
                // not whitespace, so Trim() leaves it on the first line and the hash
                // would come out one character too long. Strip it explicitly.
                var line = raw.Trim().TrimStart('\uFEFF');
                var separator = line.IndexOf(" *", StringComparison.Ordinal);
                if (separator < 0) separator = line.IndexOf("  ", StringComparison.Ordinal);
                if (separator <= 0) continue;

                var hash = line[..separator].Trim();
                var name = line[(separator + 1)..].Trim().TrimStart('*');
                if (!string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase)) continue;
                if (hash.Length == 64 && hash.All(Uri.IsHexDigit)) return hash;
            }
        }
        catch
        {
            // A missing or unreachable checksum file degrades verification rather
            // than blocking the update; the download already travelled over HTTPS.
        }

        return null;
    }

    private static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static HttpClient CreateClient(string version)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"CodexBeacon/{version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static Uri ResolveLatestReleaseUri()
    {
        var raw = Environment.GetEnvironmentVariable(FeedOverrideVariable);
        if (!string.IsNullOrWhiteSpace(raw)
            && Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var candidate)
            && (candidate.Scheme == Uri.UriSchemeHttps || candidate.IsLoopback))
        {
            return candidate;
        }

        return new Uri($"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest");
    }

    private static string ReadCurrentVersion()    {
        var assembly = typeof(UpdateService).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            var value = plus >= 0 ? informational[..plus] : informational;
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static string? ReadLauncherPath()
    {
        var arguments = Environment.GetCommandLineArgs();
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (!string.Equals(arguments[index], LauncherArgument, StringComparison.OrdinalIgnoreCase)) continue;
            var value = arguments[index + 1];
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    /// <summary>Strips a leading <c>v</c> and any pre-release suffix.</summary>
    internal static string NormalizeVersion(string? tag)
    {
        var value = (tag ?? string.Empty).Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        var dash = value.IndexOf('-');
        return dash >= 0 ? value[..dash] : value;
    }

    /// <summary>Numeric comparison on Major.Minor.Build; unparseable input ranks lowest.</summary>
    internal static int CompareVersions(string? left, string? right)
        => ParseVersion(left).CompareTo(ParseVersion(right));

    private static Version ParseVersion(string? value)
    {
        var parts = NormalizeVersion(value).Split('.');
        var numbers = new int[4];
        for (var index = 0; index < parts.Length && index < numbers.Length; index++)
        {
            if (!int.TryParse(parts[index], out var number)) return new Version(0, 0, 0, 0);
            numbers[index] = number;
        }

        return new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    /// <summary>
    /// The swap script. It must stay Windows PowerShell 5.1 compatible: the app
    /// hands it to <c>powershell.exe</c>, not <c>pwsh</c>.
    /// </summary>
    private const string HelperScript = """
        param(
            [Parameter(Mandatory = $true)][int]$ProcessId,
            [Parameter(Mandatory = $true)][string]$Source,
            [Parameter(Mandatory = $true)][string]$Target,
            [string]$LogPath
        )

        # Waits for the running manager to exit, swaps in the freshly downloaded
        # single-file launcher, and starts it again. The refreshed launcher expands
        # its embedded payload because its extraction stamp no longer matches.

        function Write-Note([string]$Message) {
            if (-not $LogPath) { return }
            try {
                Add-Content -LiteralPath $LogPath -Value ("{0}  {1}" -f (Get-Date -Format o), $Message) -Encoding UTF8
            } catch { }
        }

        Write-Note "helper started; waiting for pid $ProcessId"

        $deadline = (Get-Date).AddSeconds(180)
        while ((Get-Date) -lt $deadline) {
            $running = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
            if (-not $running) { break }
            Start-Sleep -Milliseconds 400
        }

        Write-Note "manager exited; applying update"

        $applied = $false
        for ($attempt = 1; $attempt -le 240; $attempt++) {
            try {
                Copy-Item -LiteralPath $Source -Destination $Target -Force -ErrorAction Stop
                $applied = $true
                Write-Note "replaced $Target on attempt $attempt"
                break
            } catch {
                Start-Sleep -Milliseconds 500
            }
        }

        if (-not $applied) {
            Write-Note "update failed: target stayed locked"
            exit 1
        }

        try {
            Start-Process -FilePath $Target
            Write-Note "relaunched $Target"
        } catch {
            Write-Note ("relaunch failed: " + $_.Exception.Message)
        }

        try { Remove-Item -LiteralPath $Source -Force -ErrorAction SilentlyContinue } catch { }
        """;
}
