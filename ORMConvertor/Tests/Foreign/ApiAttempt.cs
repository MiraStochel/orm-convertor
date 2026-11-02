using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tests.Foreign;

/// <summary>
/// The settings of the channel <c>api</c> (decision 202), read from <c>api.json</c> in the
/// directory of the run: the base URL of an OpenAI-compatible endpoint (ending in <c>/v1</c>),
/// the model as the endpoint names it, the version the record states (the SHA-256 of the model
/// file for a local model, the snapshot for a hosted one), the frontend - the server and its
/// build -, the temperature - absent for a model whose endpoint takes none, which then runs at
/// its default and is the second setting of decision 175 (decision 203) -, the output limit, the
/// price per million tokens in and out (0 for a local model), optionally the name of the
/// environment variable that holds the API key (the key itself never stands in the file), the
/// seed, and <c>extra</c> - fields merged into every request as they are, such as
/// <c>cache_prompt: false</c> for llama.cpp.
/// </summary>
internal sealed record ApiSettings(
    string Url,
    string Model,
    string Version,
    string Frontend,
    decimal? Temperature,
    int MaxTokens,
    decimal UsdPerMillionTokensIn,
    decimal UsdPerMillionTokensOut,
    string? ApiKeyVariable,
    int? Seed,
    JsonObject Extra)
{
    public const string FileName = "api.json";

    /// <summary>The settings of the run, or null where the run has no <c>api.json</c> - a run of the web frontend.</summary>
    public static ApiSettings? ReadIfPresent(string run)
    {
        var file = Path.Combine(run, FileName);
        return File.Exists(file) ? Read(file) : null;
    }

    public static ApiSettings Read(string file)
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

        string Text(string name)
            => root[name] is JsonValue value && value.TryGetValue<string>(out var text) && text.Trim().Length > 0
                ? text.Trim()
                : throw new InvalidOperationException($"{file}: \"{name}\" is missing or empty (decision 202).");

        decimal Number(string name)
            => root[name] is JsonValue value && value.TryGetValue<decimal>(out var number)
                ? number
                : throw new InvalidOperationException($"{file}: \"{name}\" is missing or not a number (decision 202).");

        string? OptionalText(string name)
            => root[name] is JsonValue value && value.TryGetValue<string>(out var text) && text.Trim().Length > 0 ? text.Trim() : null;

        // Absent is a setting: the model takes no temperature and runs at its default (decision 203).
        decimal? OptionalNumber(string name)
            => root[name] is null
                ? null
                : root[name] is JsonValue value && value.TryGetValue<decimal>(out var number)
                    ? number
                    : throw new InvalidOperationException($"{file}: \"{name}\" is not a number (decision 203).");

        var maxTokens = Number("maxTokens");
        if (maxTokens <= 0 || maxTokens != decimal.Truncate(maxTokens))
        {
            throw new InvalidOperationException($"{file}: \"maxTokens\" is no positive whole number.");
        }

        int? seed = root["seed"] is JsonValue seedValue && seedValue.TryGetValue<int>(out var seedNumber) ? seedNumber : null;
        var extra = root["extra"] is JsonObject given ? (JsonObject)given.DeepClone() : [];

        return new ApiSettings(
            Text("url").TrimEnd('/'),
            Text("model"),
            Text("version"),
            Text("frontend"),
            OptionalNumber("temperature"),
            (int)maxTokens,
            Number("usdPerMillionTokensIn"),
            Number("usdPerMillionTokensOut"),
            OptionalText("apiKeyVariable"),
            seed,
            extra);
    }

    public string Endpoint => Url + "/chat/completions";
}

/// <summary>What one answer of the endpoint carried, read from the JSON of a chat completion.</summary>
internal sealed record ApiReply(string Content, string? FinishReason, int? TokensIn, int? TokensOut, int ReasoningChars, string? Model, string? Timings)
{
    public static ApiReply Parse(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("the answer is not a JSON object");
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"the answer is not JSON: {e.Message}");
        }

        var choice = root["choices"] is JsonArray { Count: > 0 } choices ? choices[0] as JsonObject : null;
        var message = choice?["message"] as JsonObject
            ?? throw new InvalidOperationException("the answer carries no choices[0].message");

        var content = message["content"] switch
        {
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            JsonArray parts => string.Concat(parts.Select(part => part?["text"]?.GetValue<string>() ?? string.Empty)),
            _ => string.Empty,
        };

        var reasoning = message["reasoning_content"] is JsonValue reasoningValue && reasoningValue.TryGetValue<string>(out var reasoningText) ? reasoningText.Length : 0;
        var usage = root["usage"] as JsonObject;
        var timings = root["timings"] as JsonObject;

        return new ApiReply(
            content,
            choice?["finish_reason"]?.GetValue<string>(),
            Int(usage?["prompt_tokens"]),
            Int(usage?["completion_tokens"]),
            reasoning,
            root["model"]?.GetValue<string>(),
            timings is null ? null : Describe(timings));
    }

    private static int? Int(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    /// <summary>The server-side timings of llama.cpp in one phrase: tokens, seconds and tokens per second of the prompt and of the generation.</summary>
    private static string Describe(JsonObject timings)
    {
        string? Phase(string name, string tokens, string ms, string perSecond)
        {
            if (timings[tokens] is not JsonValue count || timings[ms] is not JsonValue milliseconds)
            {
                return null;
            }

            var rate = timings[perSecond] is JsonValue speed ? $" ({speed.GetValue<double>().ToString("0.0", CultureInfo.InvariantCulture)} tok/s)" : string.Empty;
            return $"{name} {count.GetValue<double>().ToString("0", CultureInfo.InvariantCulture)} tok {(milliseconds.GetValue<double>() / 1000).ToString("0.0", CultureInfo.InvariantCulture)} s{rate}";
        }

        return string.Join("; ", new[] { Phase("prompt", "prompt_n", "prompt_ms", "prompt_per_second"), Phase("generation", "predicted_n", "predicted_ms", "predicted_per_second") }.Where(phase => phase is not null));
    }
}

/// <summary>
/// The driver of the channel <c>api</c> (decision 202): makes every attempt of the run that has no
/// reply yet, one at a time, by sending its prompt as the one user message of a chat completion -
/// no system prompt, no tool - to the endpoint of <c>api.json</c>, writes the content of the answer
/// as <c>reply.md</c> and the measurements into the row of the attempt: the model, version and
/// frontend of the settings, <c>channel</c> <c>api</c>, the temperature (blank where the settings
/// have none, which the import reads as <c>default</c>), the seconds of the call by
/// its own clock, the tokens the endpoint counted and the cost from them by the prices of the
/// settings. The table is written after every attempt, so a run stopped from outside continues
/// where it was; an attempt with a reply is not made again, and a reply the import refused - moved
/// to <c>refused-&lt;n&gt;.md</c> - is made again on the next run. The import reads the rows as it
/// reads the operator's.
/// </summary>
internal static class ApiAttempts
{
    public const string Channel = "api";

    internal sealed record Report(List<string> Made, List<string> Done);

    public static Report Make(string run, ApiSettings settings, HttpMessageHandler? handler = null, Action<string>? log = null)
    {
        var table = Path.Combine(run, WebAttempts.AttemptsFileName);
        if (!File.Exists(table))
        {
            throw new InvalidOperationException($"{table} does not exist: the attempts are prepared first ({nameof(WebAttempts.Prepare)}).");
        }

        using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromHours(2);
        if (settings.ApiKeyVariable is not null)
        {
            var key = Environment.GetEnvironmentVariable(settings.ApiKeyVariable);
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException($"{ApiSettings.FileName} names the variable {settings.ApiKeyVariable} for the API key, and it is not set.");
            }

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        }

        var rows = WebAttempts.ReadTable(table);
        var report = new Report([], []);

        foreach (var row in rows.OrderBy(row => row["id"], StringComparer.Ordinal))
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

            if (!File.Exists(promptFile))
            {
                throw new InvalidOperationException($"{id} has no {WebAttempts.PromptFileName}: the attempts are prepared first ({nameof(WebAttempts.Prepare)}).");
            }

            var prompt = File.ReadAllText(promptFile);
            using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint)
            {
                Content = new StringContent(Request(settings, prompt), Encoding.UTF8, "application/json"),
            };

            var watch = Stopwatch.StartNew();
            using var response = client.Send(request, HttpCompletionOption.ResponseContentRead);
            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            watch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"{id}: {settings.Endpoint} answered {(int)response.StatusCode}: {(json.Length > 400 ? json[..400] + "…" : json)}");
            }

            ApiReply reply;
            try
            {
                reply = ApiReply.Parse(json);
            }
            catch (InvalidOperationException e)
            {
                throw new InvalidOperationException($"{id}: {e.Message}", e);
            }

            File.WriteAllText(replyFile, WebPrompt.Lf(reply.Content), new UTF8Encoding(false));
            Fill(row, settings, reply, watch.Elapsed);
            WebAttempts.WriteTable(table, rows);
            report.Made.Add(id);
            log?.Invoke($"{id}: {row["seconds"]} s, {row["tokensIn"]} in, {row["tokensOut"]} out; {row["note"]}");
        }

        return report;
    }

    /// <summary>The body of the chat completion: one user message with the prompt, the settings, and the extra fields as they are.</summary>
    internal static string Request(ApiSettings settings, string prompt)
    {
        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = prompt }),
            ["max_tokens"] = settings.MaxTokens,
        };

        if (settings.Temperature is decimal temperature)
        {
            body["temperature"] = temperature;
        }

        if (settings.Seed is int seed)
        {
            body["seed"] = seed;
        }

        foreach (var (name, value) in settings.Extra)
        {
            body[name] = value?.DeepClone();
        }

        return body.ToJsonString();
    }

    /// <summary>The row of the attempt as the operator would fill it, from the settings, the answer and the clock.</summary>
    internal static void Fill(Dictionary<string, string> row, ApiSettings settings, ApiReply reply, TimeSpan elapsed)
    {
        row["model"] = settings.Model;
        row["version"] = settings.Version;
        row["channel"] = Channel;
        row["frontend"] = settings.Frontend;
        row["temperature"] = settings.Temperature?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        row["seconds"] = elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        row["tokensIn"] = reply.TokensIn?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        row["tokensOut"] = reply.TokensOut?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        row["costUsd"] = reply.TokensIn is int tokensIn && reply.TokensOut is int tokensOut
            ? ((tokensIn * settings.UsdPerMillionTokensIn + tokensOut * settings.UsdPerMillionTokensOut) / 1_000_000m).ToString("0.######", CultureInfo.InvariantCulture)
            : string.Empty;
        row["credits"] = string.Empty;
        row["creditsUnit"] = string.Empty;

        var notes = new List<string> { $"finish={reply.FinishReason ?? "?"}" };
        if (reply.Timings is not null)
        {
            notes.Add(reply.Timings);
        }

        if (reply.ReasoningChars > 0)
        {
            notes.Add($"reasoning {reply.ReasoningChars} chars (not in reply.md)");
        }

        if (reply.Model is not null && reply.Model != settings.Model)
        {
            notes.Add($"endpoint names the model {reply.Model}");
        }

        row["note"] = string.Join("; ", notes);
    }
}
