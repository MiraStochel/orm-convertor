using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Model;

namespace Tests.Foreign;

/// <summary>
/// The channel <c>api</c> of decision 202: the driver that makes the attempts of a run against an
/// OpenAI-compatible endpoint and fills their rows. One case is a run on demand, which skips
/// without <c>ORMCONVERTOR_LLM_RUN</c> or without <c>api.json</c> in the run; the rest answers
/// with a stub of the endpoint and needs no model.
/// </summary>
public class ApiAttemptTest(ITestOutputHelper output)
{
    private static readonly ForeignKey TrialKey = new(ForeignKey.Matrix, "AggregationGroupingAndHaving");

    [Fact]
    public void TheAttemptsAreMadeWhereAsked()
    {
        var run = WebAttempts.RunDirectory;
        if (run is null)
        {
            Assert.Skip($"No run configured: set {WebAttempts.RunVariable} to its directory, with {ApiSettings.FileName} in it (decision 202).");
        }

        var settings = ApiSettings.ReadIfPresent(run);
        if (settings is null)
        {
            Assert.Skip($"{Path.Combine(run, ApiSettings.FileName)} does not exist: the run is one of the web frontend, or the settings of the channel api are still to be written (decision 202).");
        }

        var report = ApiAttempts.Make(run, settings, log: output.WriteLine);
        output.WriteLine($"made: {report.Made.Count}; already with a reply: {report.Done.Count}; the measurements are in {Path.Combine(run, WebAttempts.AttemptsFileName)}.");
    }

    /// <summary>
    /// A run of two attempts over a stub endpoint: the request carries the prompt as the one user
    /// message with the settings and the extra fields, the reply and the measurements land where the
    /// import reads them, a second run makes nothing again, and the import takes the rows as the
    /// operator's.
    /// </summary>
    [Fact]
    public void AnAttemptOverTheApiWritesTheReplyAndItsMeasurements()
    {
        using var temp = new TempDirectory();
        var run = temp.Path;
        File.WriteAllText(Path.Combine(run, WebCell.FileName), $"answers\tsource\ttarget\tvariant\tattempts\n{TrialKey}\tDapper\tEFCore\tzero-shot\t2\n");
        var ids = WebAttempts.Prepare(run);
        Assert.Equal(2, ids.Count);

        File.WriteAllText(Path.Combine(run, ApiSettings.FileName), """
            {
              "url": "http://stub.local/v1/",
              "model": "Qwen3.5-9B-Q4_K_M",
              "version": "sha256:03b7",
              "frontend": "llama.cpp server b12030",
              "temperature": 0,
              "maxTokens": 8192,
              "seed": 1,
              "usdPerMillionTokensIn": 1.0,
              "usdPerMillionTokensOut": 2.0,
              "extra": { "cache_prompt": false }
            }
            """);

        var content = string.Join("\n",
            "FILE: Shop.cs | CSharpEntity",
            "```csharp",
            "namespace Shop;",
            "public class ShopOrderLine { public int ProductId { get; set; } }",
            "```",
            "FILE: FindAggregationGroupingAndHaving.cs | CSharpQuery",
            "```csharp",
            "public static IQueryable FindAggregationGroupingAndHaving(DbContext ctx) => ctx.Set<ShopOrderLine>();",
            "```",
            "REPORT:",
            "```json",
            "{ \"model\": \"x\", \"toolsUsed\": [], \"sourcesConsulted\": [], \"assumptions\": [] }",
            "```");

        var requests = new List<(Uri Uri, string? Authorization, JsonObject Body)>();
        using var stub = new StubEndpoint(request =>
        {
            requests.Add((request.RequestMessage!.RequestUri!, request.RequestMessage.Headers.Authorization?.ToString(), (JsonObject)JsonNode.Parse(request.Body)!));
            return (HttpStatusCode.OK, new JsonObject
            {
                ["model"] = "/models/Qwen3.5-9B-Q4_K_M.gguf",
                ["choices"] = new JsonArray(new JsonObject { ["finish_reason"] = "stop", ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content } }),
                ["usage"] = new JsonObject { ["prompt_tokens"] = 1000, ["completion_tokens"] = 500 },
                ["timings"] = new JsonObject { ["prompt_n"] = 1000, ["prompt_ms"] = 60000.0, ["prompt_per_second"] = 16.7, ["predicted_n"] = 500, ["predicted_ms"] = 180000.0, ["predicted_per_second"] = 2.8 },
            }.ToJsonString());
        });

        var settings = ApiSettings.Read(Path.Combine(run, ApiSettings.FileName));
        var report = ApiAttempts.Make(run, settings, stub, output.WriteLine);

        Assert.Equal(ids.OrderBy(id => id, StringComparer.Ordinal), report.Made);
        Assert.Empty(report.Done);
        Assert.Equal(2, requests.Count);

        var (uri, authorization, body) = requests[0];
        Assert.Equal("http://stub.local/v1/chat/completions", uri.ToString());
        Assert.Null(authorization);
        Assert.Equal("Qwen3.5-9B-Q4_K_M", body["model"]!.GetValue<string>());
        Assert.Equal(0m, body["temperature"]!.GetValue<decimal>());
        Assert.Equal(8192, body["max_tokens"]!.GetValue<int>());
        Assert.Equal(1, body["seed"]!.GetValue<int>());
        Assert.False(body["cache_prompt"]!.GetValue<bool>());
        var message = Assert.Single(body["messages"]!.AsArray());
        Assert.Equal("user", message!["role"]!.GetValue<string>());
        Assert.Equal(File.ReadAllText(Path.Combine(WebAttempts.AttemptDirectory(run, ids[0]), WebAttempts.PromptFileName)), message["content"]!.GetValue<string>());

        var rows = WebAttempts.ReadTable(Path.Combine(run, WebAttempts.AttemptsFileName));
        Assert.All(rows, row =>
        {
            Assert.Equal("Qwen3.5-9B-Q4_K_M", row["model"]);
            Assert.Equal("sha256:03b7", row["version"]);
            Assert.Equal("api", row["channel"]);
            Assert.Equal("llama.cpp server b12030", row["frontend"]);
            Assert.Equal("0", row["temperature"]);
            Assert.Equal("1000", row["tokensIn"]);
            Assert.Equal("500", row["tokensOut"]);
            Assert.Equal("0.002", row["costUsd"]);
            Assert.True(decimal.TryParse(row["seconds"], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _), row["seconds"]);
            Assert.Contains("finish=stop", row["note"]);
            Assert.Contains("generation 500 tok 180.0 s (2.8 tok/s)", row["note"]);
            Assert.Contains("endpoint names the model /models/Qwen3.5-9B-Q4_K_M.gguf", row["note"]);
        });
        Assert.Equal(content + "\n", File.ReadAllText(Path.Combine(WebAttempts.AttemptDirectory(run, ids[0]), WebAttempts.ReplyFileName)).TrimEnd('\n') + "\n");

        // Made again: nothing is sent, every attempt already has its reply.
        var again = ApiAttempts.Make(run, settings, stub);
        Assert.Empty(again.Made);
        Assert.Equal(2, again.Done.Count);
        Assert.Equal(2, requests.Count);

        var imported = WebAttempts.Import(run);
        Assert.Equal(2, imported.Imported.Count);
        Assert.Empty(imported.Refused);
        Assert.Empty(imported.Problems);

        var artifact = ForeignArtifact.Read(Path.Combine(run, "artifacts", ids[0]), "schema");
        Assert.Equal((ORMEnum.Dapper, ORMEnum.EFCore, WebAttempts.Producer), (artifact.Source, artifact.Target, artifact.Producer));
        using var metadata = JsonDocument.Parse(artifact.Metadata);
        var root = metadata.RootElement;
        Assert.Equal("api", root.GetProperty("channel").GetString());
        Assert.Equal(0m, root.GetProperty("temperature").GetDecimal());
        Assert.Equal(1000, root.GetProperty("tokensIn").GetInt32());
        Assert.Equal(500, root.GetProperty("tokensOut").GetInt32());
        Assert.Equal(0.002m, root.GetProperty("costUsd").GetDecimal());
        Assert.Equal("sha256:03b7", root.GetProperty("version").GetString());
    }

    [Fact]
    public void TheKeyGoesIntoTheHeaderFromTheVariableTheSettingsName()
    {
        using var temp = new TempDirectory();
        var run = temp.Path;
        File.WriteAllText(Path.Combine(run, WebCell.FileName), $"answers\tsource\ttarget\tvariant\tattempts\n{TrialKey}\tDapper\tEFCore\tzero-shot\t1\n");
        WebAttempts.Prepare(run);

        const string Variable = "ORMCONVERTOR_TEST_STUB_API_KEY";
        var settings = Settings(run) with { ApiKeyVariable = Variable, Seed = null };

        var failure = Assert.Throws<InvalidOperationException>(() => ApiAttempts.Make(run, settings, new StubEndpoint(_ => (HttpStatusCode.OK, "{}"))));
        Assert.Contains(Variable, failure.Message);

        Environment.SetEnvironmentVariable(Variable, "secret-1");
        try
        {
            string? authorization = null;
            JsonObject? body = null;
            using var stub = new StubEndpoint(request =>
            {
                authorization = request.RequestMessage!.Headers.Authorization?.ToString();
                body = (JsonObject)JsonNode.Parse(request.Body)!;
                return (HttpStatusCode.OK, """{"choices":[{"finish_reason":"stop","message":{"content":"Not possible."}}]}""");
            });

            ApiAttempts.Make(run, settings, stub);

            Assert.Equal("Bearer secret-1", authorization);
            Assert.False(body!.ContainsKey("seed"));
            var row = Assert.Single(WebAttempts.ReadTable(Path.Combine(run, WebAttempts.AttemptsFileName)));
            Assert.Equal(string.Empty, row["tokensIn"]);
            Assert.Equal(string.Empty, row["costUsd"]);
            Assert.Equal("finish=stop", row["note"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, null);
        }
    }

    /// <summary>
    /// A model whose endpoint takes no temperature (decision 203): the settings leave it out, the
    /// request carries none, the row stays blank and the import reads it as the second setting of
    /// decision 175, <c>default</c>.
    /// </summary>
    [Fact]
    public void AModelWithoutATemperatureSendsNoneAndItsRowSaysDefault()
    {
        using var temp = new TempDirectory();
        var run = temp.Path;
        File.WriteAllText(Path.Combine(run, WebCell.FileName), $"answers\tsource\ttarget\tvariant\tattempts\n{TrialKey}\tDapper\tEFCore\tzero-shot\t1\n");
        var id = Assert.Single(WebAttempts.Prepare(run));

        File.WriteAllText(Path.Combine(run, ApiSettings.FileName), """
            { "url": "http://stub.local/v1", "model": "claude-fable-5-1", "version": "claude-fable-5-1", "frontend": "Claude API", "maxTokens": 32768, "usdPerMillionTokensIn": 10, "usdPerMillionTokensOut": 50 }
            """);
        var settings = ApiSettings.Read(Path.Combine(run, ApiSettings.FileName));
        Assert.Null(settings.Temperature);
        Assert.Null(settings.Seed);

        var content = string.Join("\n",
            "FILE: Shop.cs | CSharpEntity",
            "```csharp",
            "namespace Shop;",
            "public class ShopOrderLine { public int ProductId { get; set; } }",
            "```",
            "REPORT:",
            "```json",
            "{ \"model\": \"x\", \"toolsUsed\": [], \"sourcesConsulted\": [], \"assumptions\": [] }",
            "```");

        JsonObject? body = null;
        using var stub = new StubEndpoint(request =>
        {
            body = (JsonObject)JsonNode.Parse(request.Body)!;
            return (HttpStatusCode.OK, new JsonObject
            {
                ["model"] = "claude-fable-5-1",
                ["choices"] = new JsonArray(new JsonObject { ["finish_reason"] = "stop", ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content } }),
                ["usage"] = new JsonObject { ["prompt_tokens"] = 2000, ["completion_tokens"] = 6000 },
            }.ToJsonString());
        });

        ApiAttempts.Make(run, settings, stub);

        Assert.False(body!.ContainsKey("temperature"));
        Assert.False(body.ContainsKey("seed"));
        Assert.Equal(32768, body["max_tokens"]!.GetValue<int>());
        var row = Assert.Single(WebAttempts.ReadTable(Path.Combine(run, WebAttempts.AttemptsFileName)));
        Assert.Equal(string.Empty, row["temperature"]);
        Assert.Equal("0.32", row["costUsd"]);
        Assert.Equal("finish=stop", row["note"]);

        var imported = WebAttempts.Import(run);
        Assert.Equal([id], imported.Imported);
        Assert.Empty(imported.Refused);
        Assert.Empty(imported.Problems);
        var artifact = ForeignArtifact.Read(Path.Combine(run, "artifacts", id), "schema");
        using var metadata = JsonDocument.Parse(artifact.Metadata);
        Assert.Equal("default", metadata.RootElement.GetProperty("temperature").GetString());
        Assert.Equal("api", metadata.RootElement.GetProperty("channel").GetString());
    }

    [Fact]
    public void AnEndpointThatFailsStopsTheRunByName()
    {
        using var temp = new TempDirectory();
        var run = temp.Path;
        File.WriteAllText(Path.Combine(run, WebCell.FileName), $"answers\tsource\ttarget\tvariant\tattempts\n{TrialKey}\tDapper\tEFCore\tzero-shot\t1\n");
        var id = Assert.Single(WebAttempts.Prepare(run));

        using var stub = new StubEndpoint(_ => (HttpStatusCode.InternalServerError, """{"error":"the model is loading"}"""));
        var failure = Assert.Throws<InvalidOperationException>(() => ApiAttempts.Make(run, Settings(run), stub));

        Assert.Contains(id, failure.Message);
        Assert.Contains("500", failure.Message);
        Assert.False(File.Exists(Path.Combine(WebAttempts.AttemptDirectory(run, id), WebAttempts.ReplyFileName)));
    }

    [Theory]
    [InlineData("""{ "model": "m", "version": "v", "frontend": "f", "temperature": 0, "maxTokens": 10, "usdPerMillionTokensIn": 0, "usdPerMillionTokensOut": 0 }""", "\"url\"")]
    [InlineData("""{ "url": "http://x/v1", "version": "v", "frontend": "f", "temperature": 0, "maxTokens": 10, "usdPerMillionTokensIn": 0, "usdPerMillionTokensOut": 0 }""", "\"model\"")]
    [InlineData("""{ "url": "http://x/v1", "model": "m", "version": "v", "frontend": "f", "temperature": "zero", "maxTokens": 10, "usdPerMillionTokensIn": 0, "usdPerMillionTokensOut": 0 }""", "\"temperature\"")]
    [InlineData("""{ "url": "http://x/v1", "model": "m", "version": "v", "frontend": "f", "temperature": 0, "maxTokens": 10, "usdPerMillionTokensIn": 0 }""", "\"usdPerMillionTokensOut\"")]
    [InlineData("""{ "url": "http://x/v1", "model": "m", "version": "v", "frontend": "f", "temperature": 0, "maxTokens": 0, "usdPerMillionTokensIn": 0, "usdPerMillionTokensOut": 0 }""", "\"maxTokens\"")]
    [InlineData("not json", "is not JSON")]
    public void TheSettingsNameWhatIsMissing(string json, string named)
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, ApiSettings.FileName);
        File.WriteAllText(file, json);

        var failure = Assert.Throws<InvalidOperationException>(() => ApiSettings.Read(file));

        Assert.Contains(named, failure.Message);
    }

    [Fact]
    public void AnAnswerIsReadWithItsUsageAndItsReasoningKeptOut()
    {
        var reply = ApiReply.Parse("""
            {
              "model": "m",
              "choices": [{ "finish_reason": "length", "message": { "role": "assistant", "reasoning_content": "thinking…", "content": "FILE: a | b" } }],
              "usage": { "prompt_tokens": 12, "completion_tokens": 34 }
            }
            """);

        Assert.Equal(("FILE: a | b", "length", 12, 34, 9, "m"), (reply.Content, reply.FinishReason, reply.TokensIn, reply.TokensOut, reply.ReasoningChars, reply.Model));
        Assert.Null(reply.Timings);

        var failure = Assert.Throws<InvalidOperationException>(() => ApiReply.Parse("""{"object":"list"}"""));
        Assert.Contains("choices[0].message", failure.Message);
    }

    private static ApiSettings Settings(string run)
    {
        File.WriteAllText(Path.Combine(run, ApiSettings.FileName), """
            { "url": "http://stub.local/v1", "model": "m", "version": "v", "frontend": "f", "temperature": 0, "maxTokens": 100, "seed": 1, "usdPerMillionTokensIn": 0, "usdPerMillionTokensOut": 0 }
            """);
        return ApiSettings.Read(Path.Combine(run, ApiSettings.FileName));
    }

    /// <summary>A stand-in for the endpoint: answers every request with what the function gives, and hands the request over for inspection.</summary>
    private sealed class StubEndpoint(Func<(HttpRequestMessage? RequestMessage, string Body), (HttpStatusCode Status, string Json)> answer) : HttpMessageHandler
    {
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            var (status, json) = answer((request, body));
            return new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("ormconvertor-api-");

        public string Path => directory.FullName;

        public void Dispose() => directory.Delete(recursive: true);
    }
}
