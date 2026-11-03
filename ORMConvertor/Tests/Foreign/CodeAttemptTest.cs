using System.Net.Http.Headers;
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
using Model;

namespace Tests.Foreign;

/// <summary>
/// The channel <c>code</c> of decision 205: the driver that makes the attempts of a run through
/// Claude Code without tools, captures every request and lets an attempt pass only with the prompt
/// and the measured context. Two cases are runs on demand that skip without
/// <c>ORMCONVERTOR_LLM_RUN</c> or without <c>code.json</c> in the run - the run itself, and the
/// measurement of what reaches the model, one call with a one-word prompt -; the rest stands in a
/// process for Claude Code, or a server for the vendor, and needs neither.
/// </summary>
public class CodeAttemptTest(ITestOutputHelper output)
{
    private static readonly ForeignKey TrialKey = new(ForeignKey.Matrix, "AggregationGroupingAndHaving");

    private const string Model = "claude-fable-5-1";

    [Fact]
    public void TheAttemptsAreMadeWhereAsked()
    {
        var (run, settings) = Configured();
        settings.CheckWorkingDirectory();

        var report = CodeAttempts.Make(run, settings, new ClaudeCodeRunner(), CodeAttempts.Batch, output.WriteLine);

        output.WriteLine($"made: {report.Made.Count}; already with a reply: {report.Done.Count}; {(report.Stopped is null ? "every attempt has a reply" : "stopped: " + report.Stopped)}. The measurements are in {Path.Combine(run, WebAttempts.AttemptsFileName)}.");
    }

    /// <summary>
    /// The measurement of decision 205 kept as a test: one call of Claude Code with the settings of
    /// the run and a one-word prompt, through the capture. The request carries the prompt and
    /// nothing but the measured context - or the test names what else it carries.
    /// </summary>
    [Fact]
    public void WhatReachesTheModelIsThePromptAndTheMeasuredContext()
    {
        var (_, settings) = Configured();
        settings.CheckWorkingDirectory();
        const string Prompt = "Reply with the single word OK.";

        var call = new ClaudeCodeRunner().Run(settings, Prompt);
        var events = CodeOutput.Parse(call.Stdout);
        foreach (var request in call.Requests)
        {
            output.WriteLine($"{request.Method} {request.Target}");
        }

        Assert.Empty(CodeRequest.Problems(call, events, Prompt, settings));
        Assert.Contains("OK", events.ResultText ?? string.Empty, StringComparison.Ordinal);
        output.WriteLine($"Claude Code {CodeOutput.Text(events.Init, "claude_code_version")}; five-hour window {events.Utilization("five_hour")}, seven-day {events.Utilization("seven_day")}.");
    }

    /// <summary>
    /// A run of two attempts through a stand-in for Claude Code: the prompt goes in unchanged, the
    /// reply, the kept request and the measurements land where the import reads them, the second
    /// attempt counts the window it used, and the import takes the rows - with the context the
    /// report declares - as the channel's.
    /// </summary>
    [Fact]
    public void AnAttemptThroughClaudeCodeWritesTheReplyTheRequestAndItsMeasurements()
    {
        using var temp = new TempDirectory();
        var run = temp.Path;
        var ids = Prepare(run, "zero-shot", 2);
        var settings = Settings(run);

        var windows = new Queue<decimal>([0.10m, 0.12m]);
        var runner = new StandIn((given, prompt) => Call(given, prompt, Reply, fiveHour: windows.Dequeue()));

        var report = CodeAttempts.Make(run, settings, runner, log: output.WriteLine);

        Assert.Equal(ids, report.Made);
        Assert.Empty(report.Done);
        Assert.Null(report.Stopped);
        Assert.Equal(File.ReadAllText(Path.Combine(WebAttempts.AttemptDirectory(run, ids[0]), WebAttempts.PromptFileName)), runner.Prompts[0]);

        var rows = WebAttempts.ReadTable(Path.Combine(run, WebAttempts.AttemptsFileName)).ToDictionary(row => row["id"]);
        Assert.All(rows.Values, row =>
        {
            Assert.Equal(Model, row["model"]);
            Assert.Equal(Model, row["version"]);
            Assert.Equal("code", row["channel"]);
            Assert.Equal("Claude Code headless, subscription, Claude Code 2.1.317", row["frontend"]);
            Assert.Equal(string.Empty, row["temperature"]);
            Assert.Equal("42.0", row["seconds"]);
            Assert.Equal("2010", row["tokensIn"]);
            Assert.Equal("5000", row["tokensOut"]);
            Assert.Equal(string.Empty, row["costUsd"]);
            Assert.Contains("finish=end_turn", row["note"]);
            Assert.Contains("thinking 4000 tokens", row["note"]);
            Assert.Contains("cache write 2000, read 0", row["note"]);
            Assert.Contains("API list price 0.2701 USD, not charged", row["note"]);
        });
        Assert.Equal((string.Empty, string.Empty), (rows[ids[0]]["credits"], rows[ids[0]]["creditsUnit"]));
        Assert.Equal(("2", CodeAttempts.CreditsUnit), (rows[ids[1]]["credits"], rows[ids[1]]["creditsUnit"]));
        Assert.Contains("five-hour window 12 %, seven-day 30 %", rows[ids[1]]["note"]);

        var directory = WebAttempts.AttemptDirectory(run, ids[0]);
        Assert.Equal(WebPrompt.Lf(Reply), File.ReadAllText(Path.Combine(directory, WebAttempts.ReplyFileName)));
        var kept = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(directory, CodeAttempts.RequestFileName)))!;
        Assert.False(kept.ContainsKey("metadata"));
        Assert.Equal(32768, kept["max_tokens"]!.GetValue<int>());

        // Made again: nothing is called, every attempt already has its reply.
        var again = CodeAttempts.Make(run, settings, runner);
        Assert.Empty(again.Made);
        Assert.Equal(2, again.Done.Count);
        Assert.Equal(2, runner.Prompts.Count);

        var imported = WebAttempts.Import(run);
        Assert.Equal(2, imported.Imported.Count);
        Assert.Empty(imported.Refused);
        Assert.Empty(imported.Problems);

        var artifact = ForeignArtifact.Read(Path.Combine(run, "artifacts", ids[0]), "schema");
        Assert.Equal((ORMEnum.Dapper, ORMEnum.EFCore, WebAttempts.Producer), (artifact.Source, artifact.Target, artifact.Producer));
        using var metadata = JsonDocument.Parse(artifact.Metadata);
        Assert.Equal("code", metadata.RootElement.GetProperty("channel").GetString());
        Assert.Equal("default", metadata.RootElement.GetProperty("temperature").GetString());
        Assert.Equal(5000, metadata.RootElement.GetProperty("tokensOut").GetInt32());
    }

    [Fact]
    public void TheDriverStopsAfterItsBatchAndTheNextCallGoesOn()
    {
        using var temp = new TempDirectory();
        var run = temp.Path;
        var ids = Prepare(run, "zero-shot", 2);
        var runner = new StandIn((given, prompt) => Call(given, prompt, Reply));

        var first = CodeAttempts.Make(run, Settings(run), runner, batch: 1);
        Assert.Equal([ids[0]], first.Made);
        Assert.Contains("batch of 1", first.Stopped);

        var second = CodeAttempts.Make(run, Settings(run), runner, batch: 1);
        Assert.Equal([ids[1]], second.Made);
        Assert.Equal([ids[0]], second.Done);
        Assert.Null(second.Stopped);
    }

    [Fact]
    public void TheAttemptsGoInPassesZeroShotBeforeFewShot()
    {
        Dictionary<string, string> Row(string id, string variant, int attempt) => new() { ["id"] = id, ["variant"] = variant, ["attempt"] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture) };

        List<Dictionary<string, string>> rows =
        [
            Row("a-few-1", "few-shot", 1), Row("a-few-2", "few-shot", 2), Row("a-zero-1", "zero-shot", 1),
            Row("b-zero-1", "zero-shot", 1), Row("a-zero-2", "zero-shot", 2), Row("b-few-1", "few-shot", 1),
        ];

        Assert.Equal(["a-zero-1", "b-zero-1", "a-few-1", "b-few-1", "a-zero-2", "a-few-2"], CodeAttempts.Passes(rows).Select(row => row["id"]));
    }

    [Fact]
    public void AWindowUsedToItsBoundStopsTheRunBeforeTheNextAttempt()
    {
        using var temp = new TempDirectory();
        var run = temp.Path;
        var ids = Prepare(run, "zero-shot", 2);
        var runner = new StandIn((given, prompt) => Call(given, prompt, Reply, fiveHour: 0.85m));

        var report = CodeAttempts.Make(run, Settings(run), runner);

        Assert.Equal([ids[0]], report.Made);
        Assert.Single(runner.Prompts);
        Assert.Contains("five-hour window of the subscription is used to 85 %, the bound is 80 %", report.Stopped);
        Assert.Contains("renews at", report.Stopped);
    }

    [Fact]
    public void ARequestWithMoreContextStopsTheRunByName()
    {
        using var temp = new TempDirectory();
        var run = temp.Path;
        var id = Assert.Single(Prepare(run, "zero-shot", 1));
        var runner = new StandIn((given, prompt) => Call(given, prompt, Reply, reminderSections: ["userEmail", "claudeMd"]));

        var failure = Assert.Throws<InvalidOperationException>(() => CodeAttempts.Make(run, Settings(run), runner));

        Assert.Contains(id, failure.Message);
        Assert.Contains("the reminder beside the prompt carries the section claudeMd", failure.Message);
        Assert.False(File.Exists(Path.Combine(WebAttempts.AttemptDirectory(run, id), WebAttempts.ReplyFileName)));
        Assert.True(File.Exists(Path.Combine(WebAttempts.AttemptDirectory(run, id), CodeAttempts.RequestFileName)));
    }

    [Theory]
    [InlineData("tools", "the init event lists tools: Bash")]
    [InlineData("key", "takes its credentials from ANTHROPIC_API_KEY")]
    [InlineData("effort", "sets the effort xhigh, not high")]
    [InlineData("cap", "sets max_tokens 64000, not 32768")]
    [InlineData("system", "the system prompt carries")]
    [InlineData("turns", "took 2 turns")]
    [InlineData("changed", "no user message carries the prompt unchanged")]
    public void ACallBeyondTheMeasuredShapeStopsTheRun(string change, string named)
    {
        using var temp = new TempDirectory();
        var run = temp.Path;
        var id = Assert.Single(Prepare(run, "zero-shot", 1));
        var runner = new StandIn((given, prompt) => Call(given, prompt, Reply, change: change));

        var failure = Assert.Throws<InvalidOperationException>(() => CodeAttempts.Make(run, Settings(run), runner));

        Assert.Contains(id, failure.Message);
        Assert.Contains(named, failure.Message);
    }

    [Fact]
    public void AnErrorOfTheCallStopsTheRunByName()
    {
        using var temp = new TempDirectory();
        var run = temp.Path;
        var id = Assert.Single(Prepare(run, "zero-shot", 1));
        var runner = new StandIn((given, prompt) => Call(given, prompt, "You've hit your limit", isError: true));

        var failure = Assert.Throws<InvalidOperationException>(() => CodeAttempts.Make(run, Settings(run), runner));

        Assert.Contains(id, failure.Message);
        Assert.Contains("Claude Code reported an error", failure.Message);
        Assert.Contains("You've hit your limit", failure.Message);
        Assert.False(File.Exists(Path.Combine(WebAttempts.AttemptDirectory(run, id), WebAttempts.ReplyFileName)));
    }

    [Fact]
    public void TheProcessGetsTheCaptureTheLimitAndNoKeyOrSessionOfItsOwn()
    {
        var settings = Settings(Path.GetTempPath());
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PATH"] = "C:\\bin",
            ["CLAUDECODE"] = "1",
            ["CLAUDE_CODE_SESSION_ID"] = "s",
            ["CLAUDE_EFFORT"] = "xhigh",
            ["CLAUDE_CODE_GIT_BASH_PATH"] = "C:\\git\\bash.exe",
            ["ANTHROPIC_API_KEY"] = "k",
            ["ANTHROPIC_BASE_URL"] = "https://elsewhere",
        };

        CodeAttempts.Prepare(environment, settings, "http://127.0.0.1:5000");

        Assert.Equal(
            ["ANTHROPIC_BASE_URL=http://127.0.0.1:5000", "CLAUDE_CODE_GIT_BASH_PATH=C:\\git\\bash.exe", "CLAUDE_CODE_MAX_OUTPUT_TOKENS=32768", "DISABLE_AUTOUPDATER=1", "PATH=C:\\bin"],
            environment.Select(pair => $"{pair.Key}={pair.Value}").Order(StringComparer.Ordinal));

        var arguments = CodeAttempts.Arguments(settings);
        Assert.Equal(["-p", "--output-format", "stream-json", "--verbose", "--model", Model, "--effort", "high", "--tools", "", "--safe-mode", "--strict-mcp-config", "--disable-slash-commands", "--no-session-persistence", "--system-prompt", ""], arguments);
    }

    /// <summary>The capture over a stand-in for the vendor: the request arrives there with its headers and body, the capture keeps it, and the answer streams back unchanged.</summary>
    [Fact]
    public async Task TheCaptureForwardsTheRequestAndStreamsTheAnswerBack()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var vendor = builder.Build();
        vendor.Run(async context =>
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync($"event: message_start\ndata: {{\"version\":\"{context.Request.Headers["anthropic-version"]}\",\"path\":\"{context.Request.Path}{context.Request.QueryString}\",\"length\":{body.Length}}}\n\n");
            await context.Response.WriteAsync("event: message_stop\ndata: {}\n\n");
        });
        await vendor.StartAsync();
        var address = vendor.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        using var capture = CaptureProxy.Start(new Uri(address));
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, capture.BaseUrl + "/v1/messages?beta=true")
        {
            Content = new StringContent("""{"model":"m"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "secret");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var answer = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode, answer);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"version\":\"2023-06-01\",\"path\":\"/v1/messages?beta=true\",\"length\":13", answer);
        Assert.Contains("event: message_stop", answer);
        var captured = Assert.Single(capture.Requests);
        Assert.Equal(("POST", "/v1/messages?beta=true", "/v1/messages", """{"model":"m"}"""), (captured.Method, captured.Target, captured.Path, captured.Body));
    }

    [Fact]
    public void TheReportedContextOfTheInterfaceIsNoTraceInThisChannelAlone()
    {
        var reply = WebReply.Parse(Reply);

        Assert.Null(WebTrace.Find(reply, Reply, CodeAttempts.Channel));
        Assert.Contains("contextUsed", WebTrace.Find(reply, Reply, "chat"));

        var withTool = Reply.Replace("\"toolsUsed\": []", "\"toolsUsed\": [\"web search\"]", StringComparison.Ordinal);
        Assert.Contains("toolsUsed", WebTrace.Find(WebReply.Parse(withTool), withTool, CodeAttempts.Channel));
    }

    [Fact]
    public void TheSettingsTakeTheirDefaultsAndNameWhatIsMissing()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, CodeSettings.FileName);
        File.WriteAllText(file, """{ "workingDirectory": "C:\\run", "model": "m", "version": "v", "effort": "high", "maxTokens": 32768, "frontend": "f" }""");

        var settings = CodeSettings.Read(file);
        Assert.Equal(("claude", 0.8m, 0.9m), (settings.Executable, settings.StopAtFiveHourUtilization, settings.StopAtSevenDayUtilization));

        File.WriteAllText(file, """{ "workingDirectory": "C:\\run", "model": "m", "version": "v", "effort": "huge", "maxTokens": 32768, "frontend": "f" }""");
        Assert.Contains("\"effort\"", Assert.Throws<InvalidOperationException>(() => CodeSettings.Read(file)).Message);

        File.WriteAllText(file, """{ "model": "m", "version": "v", "effort": "high", "maxTokens": 32768, "frontend": "f" }""");
        Assert.Contains("\"workingDirectory\"", Assert.Throws<InvalidOperationException>(() => CodeSettings.Read(file)).Message);

        File.WriteAllText(file, """{ "workingDirectory": "C:\\run", "model": "m", "version": "v", "effort": "high", "maxTokens": 32768, "frontend": "f", "stopAtFiveHourUtilization": 80 }""");
        Assert.Contains("shares of a window", Assert.Throws<InvalidOperationException>(() => CodeSettings.Read(file)).Message);
    }

    [Fact]
    public void TheWorkingDirectoryStandsOutsideAnyRepositoryAndInstructions()
    {
        using var temp = new TempDirectory();
        var inner = Directory.CreateDirectory(Path.Combine(temp.Path, "outer", "inner")).FullName;

        Settings(inner).CheckWorkingDirectory();

        File.WriteAllText(Path.Combine(temp.Path, "outer", "CLAUDE.md"), "# instructions");
        Assert.Contains("CLAUDE.md", Assert.Throws<InvalidOperationException>(() => Settings(inner).CheckWorkingDirectory()).Message);

        Assert.Contains("does not exist", Assert.Throws<InvalidOperationException>(() => Settings(Path.Combine(temp.Path, "missing")).CheckWorkingDirectory()).Message);
    }

    private const string Reply = """
        FILE: Shop.cs | CSharpEntity
        ```csharp
        namespace Shop;
        public class ShopOrderLine { public int ProductId { get; set; } }
        ```
        REPORT:
        ```json
        { "model": "x", "toolsUsed": [], "sourcesConsulted": [], "contextUsed": ["the user's email address, supplied by the interface"], "assumptions": [] }
        ```
        """;

    private static (string Run, CodeSettings Settings) Configured()
    {
        var run = WebAttempts.RunDirectory;
        if (run is null)
        {
            Assert.Skip($"No run configured: set {WebAttempts.RunVariable} to its directory, with {CodeSettings.FileName} in it (decision 205).");
        }

        var settings = CodeSettings.ReadIfPresent(run);
        if (settings is null)
        {
            Assert.Skip($"{Path.Combine(run, CodeSettings.FileName)} does not exist: the run is one of another channel (decision 205).");
        }

        return (run, settings);
    }

    private static List<string> Prepare(string run, string variant, int attempts)
    {
        File.WriteAllText(Path.Combine(run, WebCell.FileName), $"answers\tsource\ttarget\tvariant\tattempts\n{TrialKey}\tDapper\tEFCore\t{variant}\t{attempts}\n");
        return WebAttempts.Prepare(run);
    }

    private static CodeSettings Settings(string run)
        => new("claude", run, Model, Model, "high", 32768, "Claude Code headless, subscription", 0.8m, 0.9m);

    /// <summary>
    /// One call as Claude Code 2.1.317 makes it (decision 205): the events of <c>stream-json</c> and
    /// the request the capture saw, with the context measured on 2026-11-03 - or with one thing
    /// changed, named by <paramref name="change"/>.
    /// </summary>
    private static CodeCall Call(CodeSettings settings, string prompt, string reply, decimal fiveHour = 0.1m, bool isError = false, string[]? reminderSections = null, string? change = null)
    {
        var init = new JsonObject
        {
            ["type"] = "system",
            ["subtype"] = "init",
            ["tools"] = change == "tools" ? new JsonArray("Bash") : new JsonArray(),
            ["mcp_servers"] = new JsonArray(),
            ["skills"] = new JsonArray(),
            ["slash_commands"] = new JsonArray(),
            ["model"] = settings.Model,
            ["apiKeySource"] = change == "key" ? "ANTHROPIC_API_KEY" : "none",
            ["claude_code_version"] = "2.1.317",
        };

        var limit = new JsonObject
        {
            ["type"] = "rate_limit_event",
            ["rate_limit_info"] = new JsonObject
            {
                ["status"] = "allowed",
                ["unifiedWindows"] = new JsonObject
                {
                    ["five_hour"] = new JsonObject { ["utilization"] = fiveHour, ["resetsAt"] = 1793715600, ["status"] = "allowed" },
                    ["seven_day"] = new JsonObject { ["utilization"] = 0.3m, ["resetsAt"] = 1794229200, ["status"] = "allowed" },
                },
            },
        };

        var result = new JsonObject
        {
            ["type"] = "result",
            ["subtype"] = isError ? "error_during_execution" : "success",
            ["is_error"] = isError,
            ["num_turns"] = change == "turns" ? 2 : 1,
            ["result"] = reply,
            ["stop_reason"] = "end_turn",
            ["total_cost_usd"] = 0.2701m,
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = 10,
                ["cache_creation_input_tokens"] = 2000,
                ["cache_read_input_tokens"] = 0,
                ["output_tokens"] = 5000,
                ["output_tokens_details"] = new JsonObject { ["thinking_tokens"] = 4000 },
            },
            ["modelUsage"] = new JsonObject { [settings.Model] = new JsonObject() },
        };

        var sections = string.Concat((reminderSections ?? ["userEmail"]).Select(section => $"# {section}\nsomething about {section}\n\n"));
        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["messages"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray(
                        new JsonObject { ["type"] = "text", ["text"] = $"<system-reminder>\nAs you answer the user's questions, you can use the following context:\n{sections}</system-reminder>\n" },
                        new JsonObject { ["type"] = "text", ["text"] = change == "changed" ? prompt + " Think twice." : prompt }),
                },
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "# Environment\nYou have been invoked in the following environment: \n - Primary working directory: C:\\run\n - Is a git repository: false\n" }),
                }),
            ["system"] = new JsonArray(
                new JsonObject { ["type"] = "text", ["text"] = "x-anthropic-billing-header: cc_version=2.1.317.ef6; cc_entrypoint=sdk-cli;" },
                new JsonObject { ["type"] = "text", ["text"] = change == "system" ? "You are Claude Code, Anthropic's official CLI for Claude." : CodeRequest.Identity }),
            ["tools"] = new JsonArray(),
            ["metadata"] = new JsonObject { ["user_id"] = "device and account" },
            ["max_tokens"] = change == "cap" ? 64000 : settings.MaxTokens,
            ["thinking"] = new JsonObject { ["type"] = "adaptive", ["display"] = "updates" },
            ["output_config"] = new JsonObject { ["effort"] = change == "effort" ? "xhigh" : settings.Effort },
            ["stream"] = true,
        };

        var stdout = string.Join('\n', init.ToJsonString(), limit.ToJsonString(), result.ToJsonString()) + "\n";
        return new CodeCall(0, stdout, string.Empty, TimeSpan.FromSeconds(42), [new("HEAD", "/api/hello", string.Empty), new("POST", "/v1/messages?beta=true", body.ToJsonString())]);
    }

    /// <summary>A stand-in for Claude Code: answers every call with what the function gives, and keeps the prompts.</summary>
    private sealed class StandIn(Func<CodeSettings, string, CodeCall> answer) : ICodeRunner
    {
        public List<string> Prompts { get; } = [];

        public CodeCall Run(CodeSettings settings, string prompt)
        {
            Prompts.Add(prompt);
            return answer(settings, prompt);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("ormconvertor-code-");

        public string Path => directory.FullName;

        public void Dispose() => directory.Delete(recursive: true);
    }
}
