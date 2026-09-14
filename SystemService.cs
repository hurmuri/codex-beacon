using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexBeacon;

public sealed class SystemService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>Marks the stdout line that carries the machine-readable result.</summary>
    private const string ResultMarker = "##RESULT##";
    private const string ProgressMarker = "##PROGRESS##";

    private static readonly TimeSpan CollectTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan QuickHttpTimeout = TimeSpan.FromSeconds(6);

    private static HttpClient CreateConfiguredHttpClient(string? proxyAddress, TimeSpan timeout)
    {
        var handler = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(proxyAddress) && Uri.TryCreate(proxyAddress, UriKind.Absolute, out var proxyUri))
        {
            handler.Proxy = new WebProxy(proxyUri);
            handler.UseProxy = true;
        }
        var client = new HttpClient(handler) { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Codex-Beacon");
        return client;
    }

    public static async Task<List<NetworkProbe>> ProbeNetworkAsync(
        string? proxyAddress = null, CancellationToken cancellationToken = default)
    {
        var targets = new (string Id, string Name, string Url)[]
        {
            ("chatgpt", "ChatGPT (Web / API)", "https://chatgpt.com/"),
            ("codex", "Codex CLI (API)", "https://api.openai.com/v1/models")
        };

        var tasks = targets.Select(async target =>
        {
            var sw = Stopwatch.StartNew();
            var probe = new NetworkProbe
            {
                Id = target.Id,
                Name = target.Name,
                Address = target.Url,
                State = "Unknown",
                LatencyMs = -1
            };

            try
            {
                using var client = CreateConfiguredHttpClient(proxyAddress, QuickHttpTimeout);
                using var req = new HttpRequestMessage(HttpMethod.Head, target.Url);
                using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                sw.Stop();
                probe.LatencyMs = sw.ElapsedMilliseconds;
                probe.State = "Connected";
                probe.Detail = ((int)res.StatusCode).ToString();
            }
            catch (OperationCanceledException)
            {
                probe.State = "Unreachable";
                probe.Detail = "Timeout";
            }
            catch (Exception ex)
            {
                sw.Stop();
                probe.State = "Disconnected";
                probe.Detail = SafeError(ex.Message);
            }
            return probe;
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.ToList();
    }

    public static async Task<List<PublicEgress>> QueryPublicEgressAsync(
        string? proxyAddress = null, CancellationToken cancellationToken = default)
    {
        string? egressIp = null;
        string sourceKey = "EgressSourceGeneric";

        try
        {
            using var client = CreateConfiguredHttpClient(proxyAddress, QuickHttpTimeout);
            var trace = await client.GetStringAsync("https://chatgpt.com/cdn-cgi/trace", cancellationToken).ConfigureAwait(false);
            var match = Regex.Match(trace, @"ip=([0-9a-fA-F.:]+)");
            if (match.Success)
            {
                egressIp = match.Groups[1].Value.Trim();
                sourceKey = "EgressSourceChatGpt";
            }
        }
        catch { }

        if (string.IsNullOrWhiteSpace(egressIp))
        {
            try
            {
                using var client = CreateConfiguredHttpClient(proxyAddress, QuickHttpTimeout);
                egressIp = (await client.GetStringAsync("https://api.ipify.org", cancellationToken).ConfigureAwait(false)).Trim();
                sourceKey = "EgressSourceIpEcho";
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(egressIp)) return [];

        try
        {
            using var client = CreateConfiguredHttpClient(proxyAddress, QuickHttpTimeout);
            var riskJson = await client.GetStringAsync($"https://ip.net.coffee/api/iprisk/{egressIp}", cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(riskJson);
            var root = doc.RootElement;

            var country = root.TryGetProperty("country", out var pCountry) ? pCountry.GetString() ?? "" : "";
            var countryCode = root.TryGetProperty("countryCode", out var pCode) ? pCode.GetString() ?? "" : "";
            var region = root.TryGetProperty("region", out var pRegion) ? pRegion.GetString() ?? "" : "";
            var city = root.TryGetProperty("city", out var pCity) ? pCity.GetString() ?? "" : "";
            var isp = root.TryGetProperty("asOrganization", out var pIsp) ? pIsp.GetString() ?? "" : "";
            var asn = root.TryGetProperty("asn", out var pAsn) ? $"AS{pAsn}" : "";
            var isHosting = root.TryGetProperty("is_datacenter", out var pDc) && pDc.GetBoolean();
            var trust = root.TryGetProperty("trust_score", out var pTrust) && pTrust.TryGetInt32(out var tVal) ? tVal : -1;

            return new List<PublicEgress>
            {
                new()
                {
                    Address = egressIp,
                    SourceKey = sourceKey,
                    CheckedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Country = country,
                    CountryCode = countryCode,
                    FlagEmoji = GetCountryFlagEmoji(countryCode),
                    Region = region,
                    City = city,
                    Isp = isp,
                    AsNumber = asn,
                    IsHosting = isHosting,
                    TrustScore = trust,
                    Resolved = true
                }
            };
        }
        catch
        {
            return new List<PublicEgress>
            {
                new()
                {
                    Address = egressIp,
                    SourceKey = sourceKey,
                    CheckedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Resolved = false
                }
            };
        }
    }

    private static string GetCountryFlagEmoji(string countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2) return "";
        var upper = countryCode.ToUpperInvariant();
        int first = char.ConvertToUtf32(upper, 0) - 'A' + 0x1F1E6;
        int second = char.ConvertToUtf32(upper, 1) - 'A' + 0x1F1E6;
        return char.ConvertFromUtf32(first) + char.ConvertFromUtf32(second);
    }

    public static async Task<Dictionary<string, string>> QueryLatestVersionsAsync(string? proxyAddress = null, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, string>();
        using var client = CreateConfiguredHttpClient(proxyAddress, QuickHttpTimeout);

        var desktopTask = Task.Run(async () =>
        {
            try
            {
                var json = await client.GetStringAsync("https://codexapp.agentsmirror.com/latest/manifest", cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("sources", out var sources)
                    && sources.TryGetProperty("windows", out var win)
                    && win.TryGetProperty("version", out var ver))
                {
                    var v = ver.GetString();
                    if (!string.IsNullOrEmpty(v)) lock (results) results["desktop"] = v;
                }
            }
            catch { }
        }, cancellationToken);

        var codexTask = Task.Run(async () =>
        {
            try
            {
                var json = await client.GetStringAsync("https://registry.npmjs.org/@openai/codex/latest", cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("version", out var ver))
                {
                    var v = ver.GetString();
                    if (!string.IsNullOrEmpty(v)) lock (results) results["codex"] = v;
                }
            }
            catch { }
        }, cancellationToken);

        var openCodexTask = Task.Run(async () =>
        {
            try
            {
                var json = await client.GetStringAsync("https://registry.npmjs.org/@bitkyc08/opencodex/latest", cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("version", out var ver))
                {
                    var v = ver.GetString();
                    if (!string.IsNullOrEmpty(v)) lock (results) results["opencodex"] = v;
                }
            }
            catch { }
        }, cancellationToken);

        var relayTask = Task.Run(async () =>
        {
            try
            {
                var json = await client.GetStringAsync("https://registry.npmjs.org/codex-relay/latest", cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("version", out var ver))
                {
                    var v = ver.GetString();
                    if (!string.IsNullOrEmpty(v)) lock (results) results["relay"] = v;
                }
            }
            catch { }
        }, cancellationToken);

        var tailscaleTask = Task.Run(async () =>
        {
            try
            {
                var json = await client.GetStringAsync("https://api.github.com/repos/tailscale/tailscale/releases/latest", cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("tag_name", out var tag))
                {
                    var v = tag.GetString()?.TrimStart('v');
                    if (!string.IsNullOrEmpty(v)) lock (results) results["tailscale"] = v;
                }
            }
            catch { }
        }, cancellationToken);

        var nvmTask = Task.Run(async () =>
        {
            try
            {
                var json = await client.GetStringAsync("https://api.github.com/repos/coreybutler/nvm-windows/releases/latest", cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var version = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v');
                if (!string.IsNullOrWhiteSpace(version)) lock (results) results["nvm"] = version;
            }
            catch { }
        }, cancellationToken);

        var nodeTask = Task.Run(async () =>
        {
            try
            {
                var json = await client.GetStringAsync("https://nodejs.org/dist/index.json", cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var version = doc.RootElement[0].GetProperty("version").GetString()?.TrimStart('v');
                if (!string.IsNullOrWhiteSpace(version)) lock (results) results["node"] = version;
            }
            catch { }
        }, cancellationToken);

        var npmTask = Task.Run(async () =>
        {
            try
            {
                var json = await client.GetStringAsync("https://registry.npmjs.org/npm/latest", cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var version = doc.RootElement.GetProperty("version").GetString();
                if (!string.IsNullOrWhiteSpace(version)) lock (results) results["npm"] = version;
            }
            catch { }
        }, cancellationToken);

        await Task.WhenAll(desktopTask, codexTask, openCodexTask, relayTask, tailscaleTask, nvmTask, nodeTask, npmTask).ConfigureAwait(false);
        return results;
    }

    public static async Task<List<NodeRuntime>> QueryNodeVersionCatalogAsync(
        string nodeMirror, string? proxyAddress = null, CancellationToken cancellationToken = default)
    {
        var baseUri = Uri.TryCreate(nodeMirror, UriKind.Absolute, out var parsed)
            ? parsed.ToString().TrimEnd('/')
            : "https://nodejs.org/dist";
        using var client = CreateConfiguredHttpClient(proxyAddress, TimeSpan.FromSeconds(12));
        var json = await client.GetStringAsync($"{baseUri}/index.json", cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var versions = new List<NodeRuntime>();
        foreach (var item in doc.RootElement.EnumerateArray().Take(80))
        {
            if (!item.TryGetProperty("version", out var versionProperty)) continue;
            var version = versionProperty.GetString()?.TrimStart('v');
            if (string.IsNullOrWhiteSpace(version)) continue;
            var lts = item.TryGetProperty("lts", out var ltsProperty) && ltsProperty.ValueKind == JsonValueKind.String
                ? ltsProperty.GetString() ?? ""
                : "";
            versions.Add(new NodeRuntime { Version = version, IsInstalled = false, Lts = lts });
        }
        return versions;
    }

    public static async Task<List<OpenCodexModel>> FetchOpenCodexModelsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<OpenCodexModel>();
        try
        {
            var outcome = await RunAsync("opencodex", new[] { "models", "list", "--json" }, QuickHttpTimeout, null, null, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(outcome.Output))
            {
                using var doc = JsonDocument.Parse(outcome.Output);
                if (doc.RootElement.TryGetProperty("models", out var arr))
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        var model = item.TryGetProperty("model", out var m) ? m.GetString() : null;
                        var provider = item.TryGetProperty("provider", out var p) ? p.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(model))
                        {
                            list.Add(new OpenCodexModel { Id = model, Provider = provider ?? "" });
                        }
                    }
                }
            }
        }
        catch { }
        return list;
    }

    private readonly string _scriptDirectory = Path.Combine(AppContext.BaseDirectory, "Scripts");
    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexBeacon", "settings.json");
    public static string ProviderDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".CodexBeacon");
    public static string ProvidersPath { get; } = Path.Combine(ProviderDirectory, "providers.json");

    private readonly string _settingsPath = SettingsPath;

    public AppSettings LoadSettings()
    {
        try
        {
            return File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), JsonOptions) ?? new()
                : new();
        }
        catch { return new(); }
    }

    public void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public void ApplyNetworkSettings(AppSettings previous, AppSettings next)
    {
        if (next.NetworkMode == "custom" && !string.IsNullOrWhiteSpace(next.CustomHttpProxy))
        {
            if (!previous.ManagesUserProxyEnvironment)
            {
                next.PreviousHttpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY", EnvironmentVariableTarget.User);
                next.PreviousHttpsProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY", EnvironmentVariableTarget.User);
            }
            else
            {
                next.PreviousHttpProxy = previous.PreviousHttpProxy;
                next.PreviousHttpsProxy = previous.PreviousHttpsProxy;
            }
            Environment.SetEnvironmentVariable("HTTP_PROXY", next.CustomHttpProxy, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable("HTTPS_PROXY", next.CustomHttpProxy, EnvironmentVariableTarget.User);
            next.ManagesUserProxyEnvironment = true;
            return;
        }

        if (!previous.ManagesUserProxyEnvironment) return;
        Environment.SetEnvironmentVariable("HTTP_PROXY", previous.PreviousHttpProxy, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("HTTPS_PROXY", previous.PreviousHttpsProxy, EnvironmentVariableTarget.User);
        next.ManagesUserProxyEnvironment = false;
        next.PreviousHttpProxy = null;
        next.PreviousHttpsProxy = null;
    }

    public IReadOnlyList<ProviderProfile> LoadProviders()
    {
        try
        {
            if (!File.Exists(ProvidersPath)) return [];
            return JsonSerializer.Deserialize<List<ProviderProfile>>(File.ReadAllText(ProvidersPath), JsonOptions) ?? [];
        }
        catch
        {
            // A malformed or inaccessible secret store is reported as empty. Never
            // include its contents in an exception message or diagnostic payload.
            return [];
        }
    }

    public void SaveProviders(IEnumerable<ProviderProfile> providers)
    {
        EnsurePrivateProviderDirectory();
        var temporaryPath = Path.Combine(ProviderDirectory, $"providers.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(providers, JsonOptions), new UTF8Encoding(false));
            ApplyCurrentUserFileAcl(temporaryPath);
            File.Move(temporaryPath, ProvidersPath, true);
            ApplyCurrentUserFileAcl(ProvidersPath);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
        }
    }

    public static async Task<ActionResult> TestProviderAsync(
        ProviderProfile provider, string model, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
            return new() { Success = false, MessageKey = "ProviderKeyMissing" };

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
            var baseUrl = provider.BaseUrl.Trim().TrimEnd('/');
            var usesChat = provider.WireApi.Equals("chat", StringComparison.OrdinalIgnoreCase)
                || provider.WireApi.Contains("chat.completions", StringComparison.OrdinalIgnoreCase);
            var endpoint = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? $"{baseUrl}/{(usesChat ? "chat/completions" : "responses")}"
                : $"{baseUrl}/v1/{(usesChat ? "chat/completions" : "responses")}";
            var payload = usesChat
                ? JsonSerializer.Serialize(new { model, messages = new[] { new { role = "user", content = "Reply with OK." } }, max_tokens = 16 })
                : JsonSerializer.Serialize(new { model, input = "Reply with OK.", max_output_tokens = 16 });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new() { Success = true, MessageKey = "ProviderTestSucceeded", MessageArgs = model }
                : new() { Success = false, MessageKey = "ProviderTestFailed", MessageArgs = $"HTTP {(int)response.StatusCode}" };
        }
        catch (OperationCanceledException)
        {
            return new() { Success = false, MessageKey = "ActionCanceled" };
        }
        catch (Exception ex)
        {
            return new() { Success = false, MessageKey = "ProviderTestFailed", MessageArgs = SafeError(ex.Message) };
        }
    }

    private static void EnsurePrivateProviderDirectory()
    {
        Directory.CreateDirectory(ProviderDirectory);
        var directory = new DirectoryInfo(ProviderDirectory);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The provider directory cannot be a symbolic link or junction.");
        directory.Attributes |= FileAttributes.Hidden;

        var identity = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Unable to resolve the current Windows user.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            identity,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    private static void ApplyCurrentUserFileAcl(string path)
    {
        var identity = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Unable to resolve the current Windows user.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    public async Task<SystemSnapshot> CollectAsync(bool includeLatest, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string>
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", Path.Combine(_scriptDirectory, "Collect-CodexStatus.ps1"),
            "-SettingsPath", _settingsPath
        };
        if (includeLatest) arguments.Add("-IncludeLatest");

        var outcome = await RunAsync("powershell.exe", arguments, CollectTimeout, null, null, cancellationToken).ConfigureAwait(false);
        if (outcome.Canceled)
            return new() { OverallState = "Warning", OverallKey = "OverallCollectCanceled", Error = Localization.Get("RefreshCanceled") };
        if (outcome.TimedOut)
            return new() { OverallState = "Warning", OverallKey = "OverallCollectFailed", Error = Localization.Get("CollectionTimedOut") };

        // The collector always emits a complete payload, so a non-zero exit code
        // alone is not a reason to discard a parseable snapshot.
        if (string.IsNullOrWhiteSpace(outcome.Output))
            return new() { OverallState = "Warning", OverallKey = "OverallCollectNoData", Error = SafeError(outcome.Error) };

        try
        {
            return JsonSerializer.Deserialize<SystemSnapshot>(outcome.Output, JsonOptions)
                ?? new() { OverallState = "Warning", OverallKey = "OverallCollectNoData", Error = SafeError(outcome.Error) };
        }
        catch (Exception ex)
        {
            return new()
            {
                OverallState = "Warning",
                OverallKey = outcome.ExitCode == 0 ? "OverallCollectParseFailed" : "OverallCollectFailed",
                Error = SafeError(string.IsNullOrWhiteSpace(outcome.Error) ? ex.Message : outcome.Error)
            };
        }
    }

    public async Task<ActionResult> InvokeActionAsync(
        string component, string action, string? version, IProgress<string>? progress,
        CancellationToken cancellationToken = default, IProgress<OperationProgress>? operationProgress = null)
    {
        var arguments = new List<string>
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", Path.Combine(_scriptDirectory, "Invoke-CodexAction.ps1"),
            "-Component", component, "-Action", action, "-SettingsPath", _settingsPath
        };
        if (!string.IsNullOrWhiteSpace(version)) { arguments.Add("-Version"); arguments.Add(version); }

        var outcome = await RunAsync("powershell.exe", arguments, ActionTimeout, ResultMarker, progress, cancellationToken, operationProgress).ConfigureAwait(false);
        if (outcome.Canceled) return new() { Success = false, MessageKey = "ActionCanceled" };
        if (outcome.TimedOut) return new() { Success = false, MessageKey = "ActionTimedOut" };

        var payload = outcome.ResultLine ?? outcome.Output;
        if (string.IsNullOrWhiteSpace(payload))
            return new() { Success = false, MessageKey = "ActionNoResult", Details = SafeError(outcome.Error) };

        try
        {
            return JsonSerializer.Deserialize<ActionResult>(payload, JsonOptions)
                ?? new() { Success = false, MessageKey = "ActionNoResult" };
        }
        catch
        {
            return new() { Success = false, MessageKey = "ActionFailed", Details = SafeError(outcome.Error + " " + payload) };
        }
    }

    private sealed record ProcessOutcome(int ExitCode, string Output, string? ResultLine, string Error, bool TimedOut, bool Canceled);

    private static async Task<ProcessOutcome> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        string? resultMarker,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? operationProgress = null)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException(Localization.Format("UnableToStart", fileName));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        var output = new StringBuilder();
        var error = new StringBuilder();
        string? resultLine = null;

        var stderrTask = Task.Run(async () =>
        {
            while (true)
            {
                var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                if (line is null) break;
                error.AppendLine(line);
                if (!string.IsNullOrWhiteSpace(line)) progress?.Report(line);
            }
        }, CancellationToken.None);

        var timedOut = false;
        var canceled = false;

        try
        {
            if (resultMarker is null)
            {
                output.Append(await process.StandardOutput.ReadToEndAsync(linked.Token).ConfigureAwait(false));
            }
            else
            {
                while (true)
                {
                    var line = await process.StandardOutput.ReadLineAsync(linked.Token).ConfigureAwait(false);
                    if (line is null) break;
                    if (line.StartsWith(resultMarker, StringComparison.Ordinal))
                        resultLine = line[resultMarker.Length..];
                    else if (line.StartsWith(ProgressMarker, StringComparison.Ordinal))
                    {
                        try
                        {
                            var parsed = JsonSerializer.Deserialize<OperationProgress>(line[ProgressMarker.Length..], JsonOptions);
                            if (parsed is not null) operationProgress?.Report(parsed);
                        }
                        catch { }
                    }
                    else if (!string.IsNullOrWhiteSpace(line))
                        progress?.Report(line);
                    output.AppendLine(line);
                }
            }
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            canceled = cancellationToken.IsCancellationRequested;
            timedOut = !canceled;
            TryKill(process);
            await stderrTask.ConfigureAwait(false);
            return new ProcessOutcome(-1, output.ToString(), resultLine, error.ToString(), timedOut, canceled);
        }

        await stderrTask.ConfigureAwait(false);
        return new ProcessOutcome(process.ExitCode, output.ToString(), resultLine, error.ToString(), false, false);
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
    }

    private static string SafeError(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Localization.Get("NoMoreDiagnostics");
        var lines = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.Contains("token", StringComparison.OrdinalIgnoreCase)
                        && !line.Contains("authorization", StringComparison.OrdinalIgnoreCase)
                        && !line.Contains("api_key", StringComparison.OrdinalIgnoreCase))
            .Take(12);
        return string.Join(" ", lines).Trim();
    }
}
