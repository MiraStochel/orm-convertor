using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.Foreign;

/// <summary>
/// The settings of the channel <c>code</c> (decision 205), read from <c>code.json</c> in the
/// directory of the run: the executable of Claude Code, the empty folder outside any repository it
/// runs in, the model and the version the record states, the effort and the output limit the
/// request carries, the frontend as the record names it, and the utilization of the two windows of
/// the subscription at which the driver stops before the next attempt.
/// </summary>
internal sealed record CodeSettings(
    string Executable,
    string WorkingDirectory,
    string Model,
    string Version,
    string Effort,
    int MaxTokens,
    string Frontend,
    decimal StopAtFiveHourUtilization,
    decimal StopAtSevenDayUtilization)
{
    public const string FileName = "code.json";

    private static readonly string[] Efforts = ["low", "medium", "high", "xhigh", "max"];

    /// <summary>The settings of the run, or null where the run has no <c>code.json</c> - a run of another channel.</summary>
    public static CodeSettings? ReadIfPresent(string run)
    {
        var file = Path.Combine(run, FileName);
        return File.Exists(file) ? Read(file) : null;
    }

    public static CodeSettings Read(string file)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? throw new InvalidOperationException($"{file} is not a JSON object.");
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"{file} is not JSON: {e.Message}");
        }

        string Text(string name, string? fallback = null)
            => root[name] is JsonValue value && value.TryGetValue<string>(out var text) && text.Trim().Length > 0
                ? text.Trim()
                : fallback ?? throw new InvalidOperationException($"{file}: \"{name}\" is missing or empty (decision 205).");

        decimal Number(string name, decimal? fallback = null)
            => root[name] is JsonValue value && value.TryGetValue<decimal>(out var number)
                ? number
                : root[name] is null && fallback is decimal given
                    ? given
                    : throw new InvalidOperationException($"{file}: \"{name}\" is missing or not a number (decision 205).");

        var effort = Text("effort");
        if (!Efforts.Contains(effort, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"{file}: \"effort\" is {effort}, not one of {string.Join(", ", Efforts)} (decision 205).");
        }

        var maxTokens = Number("maxTokens");
        if (maxTokens <= 0 || maxTokens != decimal.Truncate(maxTokens))
        {
            throw new InvalidOperationException($"{file}: \"maxTokens\" is no positive whole number.");
        }

        var fiveHour = Number("stopAtFiveHourUtilization", 0.8m);
        var sevenDay = Number("stopAtSevenDayUtilization", 0.9m);
        if (fiveHour is <= 0 or > 1 || sevenDay is <= 0 or > 1)
        {
            throw new InvalidOperationException($"{file}: the utilizations to stop at are shares of a window, above 0 and at most 1.");
        }

        return new CodeSettings(
            Text("executable", "claude"),
            Text("workingDirectory"),
            Text("model"),
            Text("version"),
            effort,
            (int)maxTokens,
            Text("frontend"),
            fiveHour,
            sevenDay);
    }

    /// <summary>
    /// The folder Claude Code runs in: it exists, and neither it nor a folder above it holds a
    /// <c>CLAUDE.md</c> or a <c>.git</c> - so no instructions are read and the environment it
    /// reports says "not a git repository" (decision 205).
    /// </summary>
    public void CheckWorkingDirectory()
    {
        if (!Directory.Exists(WorkingDirectory))
        {
            throw new InvalidOperationException($"The working directory of {FileName}, {WorkingDirectory}, does not exist: Claude Code runs in an empty folder outside any repository (decision 205).");
        }

        for (var directory = new DirectoryInfo(WorkingDirectory); directory is not null; directory = directory.Parent)
        {
            foreach (var name in (string[])["CLAUDE.md", ".git"])
            {
                if (File.Exists(Path.Combine(directory.FullName, name)) || Directory.Exists(Path.Combine(directory.FullName, name)))
                {
                    throw new InvalidOperationException($"{Path.Combine(directory.FullName, name)} stands at or above the working directory of {FileName}: Claude Code runs outside any repository and reads no instructions (decision 205).");
                }
            }
        }
    }
}

/// <summary>One request the capture saw: the method, the path with its query, and the body as text.</summary>
internal sealed record CapturedRequest(string Method, string Target, string Body)
{
    public string Path => Target.Split('?')[0];
}

/// <summary>One call of Claude Code: its exit code and output, the driver's clock around it, and the requests the capture saw.</summary>
internal sealed record CodeCall(int ExitCode, string Stdout, string Stderr, TimeSpan Elapsed, IReadOnlyList<CapturedRequest> Requests);

/// <summary>Runs Claude Code once on a prompt; the real one is <see cref="ClaudeCodeRunner"/>, the tests stand one in.</summary>
internal interface ICodeRunner
{
    CodeCall Run(CodeSettings settings, string prompt);
}

/// <summary>
/// The events of one call in the output <c>stream-json</c>: the opening <c>system</c>/<c>init</c>,
/// the closing <c>result</c>, and the last <c>rate_limit_event</c> with the utilization of the
/// windows of the subscription.
/// </summary>
internal sealed record CodeOutput(JsonObject? Init, JsonObject? Result, JsonObject? RateLimit)
{
    public static CodeOutput Parse(string stdout)
    {
        JsonObject? init = null, result = null, rateLimit = null;
        foreach (var line in stdout.Split('\n'))
        {
            if (line.Trim().Length == 0 || WebTrace.TryParse(line) is not JsonObject item)
            {
                continue;
            }

            switch (Text(item, "type"))
            {
                case "system" when Text(item, "subtype") == "init":
                    init = item;
                    break;
                case "result":
                    result = item;
                    break;
                case "rate_limit_event":
                    rateLimit = item;
                    break;
            }
        }

        return new CodeOutput(init, result, rateLimit);
    }

    /// <summary>The utilization of a window of the subscription (<c>five_hour</c>, <c>seven_day</c>) as a share, or null.</summary>
    public decimal? Utilization(string window) => Window(window)?["utilization"] is JsonValue value && value.TryGetValue<decimal>(out var share) ? share : null;

    /// <summary>When a window of the subscription is renewed, or null.</summary>
    public DateTimeOffset? ResetsAt(string window) => Window(window)?["resetsAt"] is JsonValue value && value.TryGetValue<long>(out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;

    public string? ResultText => Result?["result"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public long? Usage(string field) => Result?["usage"]?[field] is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;

    public long? ThinkingTokens => Result?["usage"]?["output_tokens_details"]?["thinking_tokens"] is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;

    public decimal? CostUsd => Result?["total_cost_usd"] is JsonValue value && value.TryGetValue<decimal>(out var cost) ? cost : null;

    public static string? Text(JsonNode? node, string field) => node?[field] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private JsonObject? Window(string window) => RateLimit?["rate_limit_info"]?["unifiedWindows"]?[window] as JsonObject;
}

/// <summary>
/// What a call of the channel <c>code</c> may carry beyond the prompt (decision 205), as measured on
/// Claude Code 2.1.317: the two blocks of the system prompt, a reminder with the section
/// <c>userEmail</c> beside the prompt, and the section <c>Environment</c> as a system message in the
/// conversation. Everything else stops the run.
/// </summary>
internal static class CodeRequest
{
    public const string BillingPrefix = "x-anthropic-billing-header:";

    public const string Identity = "You are a Claude agent, built on Anthropic's Claude Agent SDK.";

    public const string Reminder = "<system-reminder>";

    public static readonly IReadOnlyList<string> ReminderSections = ["userEmail"];

    public const string Environment = "# Environment";

    /// <summary>What is wrong with the call: the events of the output and the request the capture saw. Empty where nothing is.</summary>
    public static List<string> Problems(CodeCall call, CodeOutput output, string prompt, CodeSettings settings)
    {
        if (call.ExitCode != 0)
        {
            return [$"Claude Code ended with exit code {call.ExitCode}: {Tail(call.Stderr.Length > 0 ? call.Stderr : call.Stdout)}"];
        }

        if (output.Result is null)
        {
            return [$"Claude Code gave no result event: {Tail(call.Stderr.Length > 0 ? call.Stderr : call.Stdout)}"];
        }

        if (output.Result["is_error"] is JsonValue error && error.TryGetValue<bool>(out var isError) && isError)
        {
            return [$"Claude Code reported an error ({CodeOutput.Text(output.Result, "subtype")}, status {output.Result["api_error_status"]?.ToString() ?? "-"}): {output.ResultText}"];
        }

        var problems = new List<string>();
        problems.AddRange(InitProblems(output.Init, settings));

        if (output.Result["num_turns"] is JsonValue turns && turns.TryGetValue<int>(out var count) && count != 1)
        {
            problems.Add($"the call took {count} turns, not one");
        }

        var messages = call.Requests.Where(request => request.Method == "POST" && request.Path == "/v1/messages").ToList();
        if (messages.Count != 1)
        {
            problems.Add($"the capture saw {messages.Count} requests to /v1/messages, not one");
            return problems;
        }

        if (WebTrace.TryParse(messages[0].Body) is not JsonObject body)
        {
            problems.Add("the captured request is not a JSON object");
            return problems;
        }

        problems.AddRange(BodyProblems(body, prompt, settings));
        return problems;
    }

    private static IEnumerable<string> InitProblems(JsonObject? init, CodeSettings settings)
    {
        if (init is null)
        {
            yield return "Claude Code gave no init event";
            yield break;
        }

        foreach (var list in (string[])["tools", "mcp_servers", "skills", "slash_commands"])
        {
            if (init[list] is JsonArray { Count: > 0 } items)
            {
                yield return $"the init event lists {list}: {string.Join(", ", items.Select(item => item is JsonObject named ? CodeOutput.Text(named, "name") : item?.ToString()))}";
            }
        }

        if (CodeOutput.Text(init, "model") is { } model && model != settings.Model)
        {
            yield return $"Claude Code runs the model {model}, not {settings.Model}";
        }

        if (CodeOutput.Text(init, "apiKeySource") is { } source && source != "none")
        {
            yield return $"Claude Code takes its credentials from {source}, not from the subscription";
        }
    }

    private static IEnumerable<string> BodyProblems(JsonObject body, string prompt, CodeSettings settings)
    {
        if (CodeOutput.Text(body, "model") != settings.Model)
        {
            yield return $"the request names the model {CodeOutput.Text(body, "model")}, not {settings.Model}";
        }

        if (body["tools"] is JsonArray { Count: > 0 } tools)
        {
            yield return $"the request carries {tools.Count} tools";
        }

        if (body["max_tokens"] is not JsonValue maxTokens || !maxTokens.TryGetValue<int>(out var cap) || cap != settings.MaxTokens)
        {
            yield return $"the request sets max_tokens {body["max_tokens"]?.ToString() ?? "-"}, not {settings.MaxTokens}";
        }

        if (CodeOutput.Text(body["output_config"], "effort") != settings.Effort)
        {
            yield return $"the request sets the effort {CodeOutput.Text(body["output_config"], "effort") ?? "-"}, not {settings.Effort}";
        }

        foreach (var block in Blocks(body["system"]))
        {
            if (!block.StartsWith(BillingPrefix, StringComparison.Ordinal) && block != Identity)
            {
                yield return $"the system prompt carries \"{Short(block)}\"";
            }
        }

        var messages = body["messages"] as JsonArray ?? [];
        var expected = Trim(prompt);
        var promptSeen = false;
        foreach (var message in messages.OfType<JsonObject>())
        {
            var role = CodeOutput.Text(message, "role");
            if (message["content"] is JsonArray parts && parts.OfType<JsonObject>().FirstOrDefault(part => CodeOutput.Text(part, "type") != "text") is { } other)
            {
                yield return $"a {role} message carries a block of type {CodeOutput.Text(other, "type")}";
            }

            var blocks = Blocks(message["content"]);
            switch (role)
            {
                case "user":
                    foreach (var block in blocks)
                    {
                        if (!promptSeen && Trim(block) == expected)
                        {
                            promptSeen = true;
                        }
                        else if (block.StartsWith(Reminder, StringComparison.Ordinal))
                        {
                            foreach (var section in Sections(block).Where(section => !ReminderSections.Contains(section, StringComparer.Ordinal)))
                            {
                                yield return $"the reminder beside the prompt carries the section {section}";
                            }
                        }
                        else
                        {
                            yield return $"the user message carries \"{Short(block)}\" beside the prompt";
                        }
                    }

                    break;
                case "system":
                    foreach (var block in blocks)
                    {
                        if (!block.StartsWith(Environment, StringComparison.Ordinal) || Sections(block).Any(section => section != Environment[2..]))
                        {
                            yield return $"a system message in the conversation carries \"{Short(block)}\"";
                        }
                    }

                    break;
                default:
                    yield return $"the request carries a message of the role {role}";
                    break;
            }

            foreach (var name in WebPrompt.TraceNames.Where(name => blocks.Where(block => Trim(block) != expected).Any(block => block.Contains(name, StringComparison.OrdinalIgnoreCase))))
            {
                yield return $"the context beside the prompt names \"{name}\"";
            }
        }

        if (!promptSeen)
        {
            yield return "no user message carries the prompt unchanged";
        }
    }

    /// <summary>The body of the request as the attempt keeps it: everything but <c>metadata</c>, which names the device and the account.</summary>
    public static string Kept(string body)
    {
        if (WebTrace.TryParse(body) is not JsonObject root)
        {
            return body;
        }

        root.Remove("metadata");
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static List<string> Blocks(JsonNode? content) => content switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => [text],
        JsonArray parts => [.. parts.OfType<JsonObject>().Select(part => CodeOutput.Text(part, "text")).OfType<string>()],
        _ => [],
    };

    /// <summary>The headings of a block - lines that start with "# " - without the hash.</summary>
    private static IEnumerable<string> Sections(string block)
        => block.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(line => line.StartsWith("# ", StringComparison.Ordinal)).Select(line => line[2..].Trim());

    private static string Trim(string text) => WebPrompt.Lf(text).Trim();

    private static string Short(string text) => text.Length > 80 ? text[..80].Replace('\n', ' ') + "…" : text.Replace('\n', ' ');

    private static string Tail(string text) => text.Length > 400 ? "…" + text[^400..] : text;
}

/// <summary>
/// The capture of decision 205: a forwarding proxy on 127.0.0.1 inside the driver's process. It
/// keeps every request Claude Code sends, passes it on unchanged to the upstream - the vendor's
/// API - and streams the answer back as it arrives.
/// </summary>
internal sealed class CaptureProxy : IDisposable
{
    private static readonly HashSet<string> Hop = new(["Host", "Content-Length", "Transfer-Encoding", "Connection", "Keep-Alive", "Accept-Encoding"], StringComparer.OrdinalIgnoreCase);

    private readonly WebApplication app;
    private readonly HttpClient client;
    private readonly Uri upstream;
    private readonly List<CapturedRequest> requests = [];

    private CaptureProxy(WebApplication app, HttpClient client, Uri upstream)
    {
        this.app = app;
        this.client = client;
        this.upstream = upstream;
    }

    /// <summary>The address to give Claude Code as <c>ANTHROPIC_BASE_URL</c>.</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    public IReadOnlyList<CapturedRequest> Requests
    {
        get
        {
            lock (requests)
            {
                return [.. requests];
            }
        }
    }

    public static CaptureProxy Start(Uri upstream)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

        var client = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.None, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        var proxy = new CaptureProxy(app, client, upstream);
        app.Run(proxy.Forward);
        app.StartAsync().GetAwaiter().GetResult();
        proxy.BaseUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return proxy;
    }

    private async Task Forward(HttpContext context)
    {
        var request = context.Request;
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, context.RequestAborted);
        var bytes = buffer.ToArray();
        var target = request.Path.ToString() + request.QueryString.ToString();
        lock (requests)
        {
            requests.Add(new CapturedRequest(request.Method, target, Encoding.UTF8.GetString(bytes)));
        }

        using var message = new HttpRequestMessage(new HttpMethod(request.Method), new Uri(upstream, target));
        if (bytes.Length > 0)
        {
            message.Content = new ByteArrayContent(bytes);
        }

        foreach (var (name, values) in request.Headers)
        {
            if (Hop.Contains(name))
            {
                continue;
            }

            if (!message.Headers.TryAddWithoutValidation(name, (IEnumerable<string?>)values))
            {
                message.Content?.Headers.TryAddWithoutValidation(name, (IEnumerable<string?>)values);
            }
        }

        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var (name, values) in response.Headers.Concat(response.Content.Headers))
        {
            if (!Hop.Contains(name))
            {
                context.Response.Headers[name] = values.ToArray();
            }
        }

        await using var stream = await response.Content.ReadAsStreamAsync(context.RequestAborted);
        await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    public void Dispose()
    {
        app.StopAsync().GetAwaiter().GetResult();
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        client.Dispose();
    }
}

/// <summary>Claude Code as a process: one call per attempt, behind its own capture, with the prompt on the standard input.</summary>
internal sealed class ClaudeCodeRunner(Uri? upstream = null) : ICodeRunner
{
    public static readonly Uri Vendor = new("https://api.anthropic.com");

    private static readonly TimeSpan Limit = TimeSpan.FromHours(2);

    public CodeCall Run(CodeSettings settings, string prompt)
    {
        using var capture = CaptureProxy.Start(upstream ?? Vendor);
        var utf8 = new UTF8Encoding(false);
        var start = new ProcessStartInfo(settings.Executable)
        {
            WorkingDirectory = settings.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };

        foreach (var argument in CodeAttempts.Arguments(settings))
        {
            start.ArgumentList.Add(argument);
        }

        CodeAttempts.Prepare(start.Environment, settings, capture.BaseUrl);

        var watch = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{settings.Executable} did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(prompt);
        process.StandardInput.Close();

        if (!process.WaitForExit(Limit))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"Claude Code did not finish within {Limit.TotalHours} hours and was stopped.");
        }

        watch.Stop();
        return new CodeCall(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult(), watch.Elapsed, capture.Requests);
    }
}

/// <summary>
/// The driver of the channel <c>code</c> (decision 205): makes the attempts of the run that have no
/// reply yet by calling Claude Code without tools and without customization, logged in with the
/// subscription, in passes - attempt 1 of every cell in <c>zero-shot</c>, then in
/// <c>few-shot</c>, then attempt 2, then 3 -, at most a batch per call, and stops before the next
/// attempt once a window of the subscription is used up to its bound. Every call goes through the
/// capture; an attempt passes only when its request carries the prompt and the measured context,
/// and the request is kept beside the reply. The table is written after every attempt, so a run
/// stopped from outside continues where it was.
/// </summary>
internal static class CodeAttempts
{
    public const string Channel = "code";

    public const string BatchVariable = "ORMCONVERTOR_LLM_BATCH";

    public const string RequestFileName = "request.json";

    public const string CreditsUnit = "% of the five-hour window";

    internal sealed class Report
    {
        public List<string> Made { get; } = [];

        public List<string> Done { get; } = [];

        /// <summary>Why the driver stopped before the end of the run - a batch made, a window used up - or null.</summary>
        public string? Stopped { get; set; }
    }

    /// <summary>The arguments of every call: print mode with events, the model and the effort, no tool, no customization, no MCP server, no command, no saved session, an empty system prompt.</summary>
    public static IReadOnlyList<string> Arguments(CodeSettings settings) =>
    [
        "-p", "--output-format", "stream-json", "--verbose",
        "--model", settings.Model, "--effort", settings.Effort,
        "--tools", string.Empty, "--safe-mode", "--strict-mcp-config", "--disable-slash-commands",
        "--no-session-persistence", "--system-prompt", string.Empty,
    ];

    /// <summary>
    /// The environment of the process: without the variables of a Claude Code session the driver may
    /// run in and without any <c>ANTHROPIC_*</c> - so the call goes by the subscription, not a key -,
    /// with the capture as the base URL, the output limit of the settings and no update mid-run.
    /// </summary>
    public static void Prepare(IDictionary<string, string?> environment, CodeSettings settings, string baseUrl)
    {
        foreach (var name in environment.Keys.ToList())
        {
            if (name.Equals("CLAUDECODE", StringComparison.OrdinalIgnoreCase)
                || name.Equals("CLAUDE_PID", StringComparison.OrdinalIgnoreCase)
                || name.Equals("CLAUDE_EFFORT", StringComparison.OrdinalIgnoreCase)
                || (name.StartsWith("CLAUDE_CODE_", StringComparison.OrdinalIgnoreCase) && !name.Equals("CLAUDE_CODE_GIT_BASH_PATH", StringComparison.OrdinalIgnoreCase))
                || name.StartsWith("ANTHROPIC_", StringComparison.OrdinalIgnoreCase))
            {
                environment.Remove(name);
            }
        }

        environment["ANTHROPIC_BASE_URL"] = baseUrl;
        environment["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] = settings.MaxTokens.ToString(CultureInfo.InvariantCulture);
        environment["DISABLE_AUTOUPDATER"] = "1";
    }

    /// <summary>The batch of this call from <c>ORMCONVERTOR_LLM_BATCH</c>, or null for every attempt left.</summary>
    public static int? Batch
    {
        get
        {
            var text = Environment.GetEnvironmentVariable(BatchVariable);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var batch) && batch > 0
                ? batch
                : throw new InvalidOperationException($"{BatchVariable} is \"{text}\", not a positive whole number of attempts.");
        }
    }

    public static Report Make(string run, CodeSettings settings, ICodeRunner runner, int? batch = null, Action<string>? log = null)
    {
        var table = Path.Combine(run, WebAttempts.AttemptsFileName);
        if (!File.Exists(table))
        {
            throw new InvalidOperationException($"{table} does not exist: the attempts are prepared first ({nameof(WebAttempts.Prepare)}).");
        }

        var rows = WebAttempts.ReadTable(table);
        var report = new Report();
        CodeOutput? last = null;

        foreach (var row in Passes(rows))
        {
            var id = row["id"];
            var directory = WebAttempts.AttemptDirectory(run, id);
            var replyFile = Path.Combine(directory, WebAttempts.ReplyFileName);
            var promptFile = Path.Combine(directory, WebAttempts.PromptFileName);

            if (File.Exists(replyFile))
            {
                report.Done.Add(id);
                continue;
            }

            if (report.Stopped is not null)
            {
                continue;
            }

            if (batch is int size && report.Made.Count >= size)
            {
                report.Stopped = $"the batch of {size} attempts is made";
                continue;
            }

            if (last is not null && Full(last, settings) is { } full)
            {
                report.Stopped = full;
                continue;
            }

            if (!File.Exists(promptFile))
            {
                throw new InvalidOperationException($"{id} has no {WebAttempts.PromptFileName}: the attempts are prepared first ({nameof(WebAttempts.Prepare)}).");
            }

            var prompt = File.ReadAllText(promptFile);
            var call = runner.Run(settings, prompt);
            var output = CodeOutput.Parse(call.Stdout);

            if (call.Requests.FirstOrDefault(request => request.Method == "POST" && request.Path == "/v1/messages") is { } captured)
            {
                File.WriteAllText(Path.Combine(directory, RequestFileName), CodeRequest.Kept(captured.Body), new UTF8Encoding(false));
            }

            var problems = CodeRequest.Problems(call, output, prompt, settings);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException($"{id}: {string.Join("; ", problems)}. No reply was written; the attempt is made again on the next run (decision 205).");
            }

            File.WriteAllText(replyFile, WebPrompt.Lf(output.ResultText ?? string.Empty), new UTF8Encoding(false));
            Fill(row, settings, output, call.Elapsed, last);
            WebAttempts.WriteTable(table, rows);
            report.Made.Add(id);
            log?.Invoke($"{id}: {row["seconds"]} s, {row["tokensIn"]} in, {row["tokensOut"]} out; {row["note"]}");
            last = output;
        }

        return report;
    }

    /// <summary>The attempts in passes: by attempt, then <c>zero-shot</c> before <c>few-shot</c> before the rest, then as the table has them.</summary>
    internal static IEnumerable<Dictionary<string, string>> Passes(List<Dictionary<string, string>> rows)
        => rows
            .Select((row, index) => (row, index))
            .OrderBy(item => int.TryParse(item.row.GetValueOrDefault("attempt"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var attempt) ? attempt : int.MaxValue)
            .ThenBy(item => item.row.GetValueOrDefault("variant") switch { "zero-shot" => 0, "few-shot" => 1, _ => 2 })
            .ThenBy(item => item.row.GetValueOrDefault("variant"), StringComparer.Ordinal)
            .ThenBy(item => item.index)
            .Select(item => item.row);

    /// <summary>Why the windows of the subscription stop the next attempt, or null.</summary>
    internal static string? Full(CodeOutput last, CodeSettings settings)
    {
        foreach (var (window, bound, name) in (IEnumerable<(string, decimal, string)>)[("five_hour", settings.StopAtFiveHourUtilization, "five-hour"), ("seven_day", settings.StopAtSevenDayUtilization, "seven-day")])
        {
            if (last.Utilization(window) is decimal used && used >= bound)
            {
                var resets = last.ResetsAt(window) is DateTimeOffset at ? $"; it renews at {at.ToLocalTime():yyyy-MM-dd HH:mm}" : string.Empty;
                return $"the {name} window of the subscription is used to {Percent(used)} %, the bound is {Percent(bound)} %{resets}";
            }
        }

        return null;
    }

    /// <summary>The row of the attempt from the settings, the events of the call and the driver's clock.</summary>
    internal static void Fill(Dictionary<string, string> row, CodeSettings settings, CodeOutput output, TimeSpan elapsed, CodeOutput? previous)
    {
        string Number(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        var version = CodeOutput.Text(output.Init, "claude_code_version");
        row["model"] = settings.Model;
        row["version"] = settings.Version;
        row["channel"] = Channel;
        row["frontend"] = version is null ? settings.Frontend : $"{settings.Frontend}, Claude Code {version}";
        row["temperature"] = string.Empty;
        row["seconds"] = elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);

        var input = output.Usage("input_tokens");
        var written = output.Usage("cache_creation_input_tokens");
        var read = output.Usage("cache_read_input_tokens");
        row["tokensIn"] = input is null ? string.Empty : Number(input + (written ?? 0) + (read ?? 0));
        row["tokensOut"] = Number(output.Usage("output_tokens"));
        row["costUsd"] = string.Empty;

        var now = output.Utilization("five_hour");
        var before = previous?.Utilization("five_hour");
        row["credits"] = now is decimal after && before is decimal earlier ? Percent(after - earlier) : string.Empty;
        row["creditsUnit"] = row["credits"].Length > 0 ? CreditsUnit : string.Empty;

        var notes = new List<string> { $"finish={CodeOutput.Text(output.Result, "stop_reason") ?? "?"}" };
        if (output.ThinkingTokens is long thinking)
        {
            notes.Add($"thinking {thinking} tokens");
        }

        notes.Add($"cache write {Number(written)}, read {Number(read)}");
        if (output.CostUsd is decimal cost)
        {
            notes.Add($"API list price {cost.ToString("0.####", CultureInfo.InvariantCulture)} USD, not charged");
        }

        if (now is decimal fiveHour)
        {
            notes.Add($"five-hour window {Percent(fiveHour)} %" + (output.Utilization("seven_day") is decimal sevenDay ? $", seven-day {Percent(sevenDay)} %" : string.Empty));
        }

        if (output.Result?["modelUsage"] is JsonObject usage && usage.Select(pair => pair.Key).Where(model => model != settings.Model).ToList() is { Count: > 0 } others)
        {
            notes.Add($"the call also used {string.Join(", ", others)}");
        }

        row["note"] = string.Join("; ", notes);
    }

    private static string Percent(decimal share) => (share * 100).ToString("0.##", CultureInfo.InvariantCulture);
}
