using System.Text.Json;
using Model;
using OrmConvertor.Factories;
using ORMConvertorAPI.Data;
using Tests.Experiment;

namespace Tests.Foreign;

/// <summary>
/// The web channel of decision 189: the prompt of a cell, the reading of a reply, the trace of a
/// search, an attempt from its prompt to an artifact or a row, and the summary that pairs the
/// attempts with the tool. Two cases are runs on demand - writing the prompts of a run and
/// importing its replies -, which skip without <c>ORMCONVERTOR_LLM_RUN</c>; the rest needs no
/// database and no model.
/// </summary>
public class WebAttemptTest(ITestOutputHelper output)
{
    private static readonly ForeignKey TrialKey = new(ForeignKey.Matrix, "AggregationGroupingAndHaving");

    [Fact]
    public void ThePromptsAreWrittenWhereAsked()
    {
        var run = WebAttempts.RunDirectory;
        if (run is null)
        {
            Assert.Skip($"No run of the web channel configured: set {WebAttempts.RunVariable} to its directory, with a {WebCell.FileName} in it (decision 189).");
        }

        var ids = WebAttempts.Prepare(run);
        foreach (var id in ids)
        {
            output.WriteLine(Path.Combine(WebAttempts.AttemptDirectory(run, id), WebAttempts.PromptFileName));
        }

        output.WriteLine($"{ids.Count} prompts; the measurements go into {Path.Combine(run, WebAttempts.AttemptsFileName)}.");
        Assert.NotEmpty(ids);
    }

    [Fact]
    public void TheRepliesAreImportedWhereAsked()
    {
        var run = WebAttempts.RunDirectory;
        if (run is null)
        {
            Assert.Skip($"No run of the web channel configured: set {WebAttempts.RunVariable} to its directory (decision 189).");
        }

        var report = WebAttempts.Import(run);
        output.WriteLine($"imported: {report.Imported.Count}; without an artifact: {report.WithoutArtifact.Count}; waiting for a reply: {report.Pending.Count}");
        foreach (var id in report.WithoutArtifact)
        {
            output.WriteLine($"  level 0: {id}");
        }

        foreach (var id in report.Pending)
        {
            output.WriteLine($"  no reply yet: {id}");
        }

        var failures = report.Refused.Select(refused => $"{refused.Id} refused - {refused.Trace}; make the attempt again in a new conversation").Concat(report.Problems).ToList();
        if (failures.Count > 0)
        {
            Assert.Fail(string.Join(Environment.NewLine, failures));
        }
    }

    [Fact]
    public void APromptCarriesTheInputTheVersionsAndTheCallButNoTraceOfTheRepository()
    {
        using var temp = new TempDirectory();
        var directory = ExportOne(temp.Path, TrialKey, ORMEnum.Dapper);

        var template = WebTemplate.Load(WebTemplate.ForVariant["zero-shot"]);
        var prompt = WebPrompt.Render(template, directory, ORMEnum.EFCore);
        output.WriteLine(prompt);

        Assert.Equal(prompt, WebPrompt.Render(template, directory, ORMEnum.EFCore));
        Assert.Contains($"Entity Framework Core {DescriptorFactory.Create(ORMEnum.EFCore).Version}", prompt);
        Assert.Contains($"Dapper {DescriptorFactory.Create(ORMEnum.Dapper).Version}", prompt);
        Assert.Contains("FILE: Shop.cs", prompt);
        Assert.Contains("FROM {{schema}}.ShopOrderLines AS ol", prompt);
        Assert.Contains("`DbContext ctx`", prompt);
        Assert.Contains("`ProductId`, `Total`, `Lines`", prompt);
        Assert.Contains("`CSharpEntity`, `CSharpQuery` and `SqlQuery`", prompt);
        Assert.Contains("REPORT:", prompt);
        Assert.Contains("no memory of other conversations", prompt);
        Assert.Contains("do not read or write files on any computer", prompt);
        Assert.Contains("do not open any repository", prompt);
        Assert.Contains("one document in the form given at the end of this message, and nothing else", prompt);
        Assert.Contains("\"contextUsed\": []", prompt);
        Assert.DoesNotContain("{{SHAPE}}", prompt);
        Assert.DoesNotContain("\r", prompt);
        Assert.DoesNotContain(TrialKey.ToString(), prompt);
        Assert.All(WebPrompt.TraceNames, name => Assert.DoesNotContain(name, prompt, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheCallOfAQueryWithParametersNamesThemInTheOrderTheCallerBindsThem()
    {
        using var temp = new TempDirectory();
        var directory = ExportOne(temp.Path, new ForeignKey(ForeignKey.Matrix, "PaginationWithBoundCounts"), ORMEnum.Hibernate);

        var prompt = WebPrompt.Render(WebTemplate.Load(WebTemplate.ForVariant["zero-shot"]), directory, ORMEnum.MyBatis);

        Assert.Contains("with exactly these arguments, in this order: `skip` (a 32-bit integer) and `take` (a 32-bit integer).", prompt);
        Assert.Contains("whole entities", prompt);
    }

    [Fact]
    public void EveryTargetIsDescribedWithEveryRoleItTakes()
    {
        var template = WebTemplate.Load(WebTemplate.ForVariant["zero-shot"]);

        Assert.All(Enum.GetValues<ORMEnum>(), target =>
        {
            Assert.True(template.Shapes.ContainsKey(target), $"The template describes no answer for {target}.");
            Assert.All(WebPrompt.Roles(target), role => Assert.Contains($"`{role}`", template.Shapes[target]));
        });
    }

    /// <summary>A template a recorded run may name stays readable after a newer one took its place (decision 189).</summary>
    [Theory]
    [InlineData("web-zero-shot-1")]
    [InlineData("web-zero-shot-2")]
    public void EveryTemplateStaysReadable(string id)
    {
        var template = WebTemplate.Load(id);

        Assert.Equal(id, template.Id);
        Assert.Contains("{{INPUT}}", template.Prompt);
        Assert.Equal(Enum.GetValues<ORMEnum>().Length, template.Shapes.Count);
    }

    /// <summary>Every input the export writes goes into a prompt, and no prompt names the repository or the tool - the excerpts' marker included (decision 189).</summary>
    [Fact]
    public void EveryInputGoesIntoAPromptWithoutATrace()
    {
        using var temp = new TempDirectory();
        ForeignInputs.Export(temp.Path);
        var template = WebTemplate.Load(WebTemplate.ForVariant["zero-shot"]);

        var directories = Directory.GetDirectories(temp.Path);
        Assert.NotEmpty(directories);

        foreach (var directory in directories)
        {
            var source = Enum.Parse<ORMEnum>(JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, ForeignInputs.ManifestFileName))).RootElement.GetProperty("source").GetString()!);
            var target = Enum.GetValues<ORMEnum>().First(framework => framework != source);
            _ = WebPrompt.Render(template, directory, target);
        }
    }

    [Fact]
    public void TheMarkerOfAnExcerptGoesIntoThePromptNeutral()
    {
        var example = Examples.GetExamples.First(example => Examples.RealProjectKeys.Contains(example.Key)
            && example.Units.Any(unit => unit.Content.Contains(WebPrompt.ExcerptMarker, StringComparison.Ordinal)));

        using var temp = new TempDirectory();
        var directory = ExportOne(temp.Path, new ForeignKey(ForeignKey.Example, example.Key), example.SourceOrm);
        var target = example.SourceOrm == ORMEnum.EFCore ? ORMEnum.NHibernate : ORMEnum.EFCore;

        var prompt = WebPrompt.Render(WebTemplate.Load(WebTemplate.ForVariant["zero-shot"]), directory, target);

        Assert.Contains(WebPrompt.NeutralMarker, prompt);
        Assert.DoesNotContain(WebPrompt.ExcerptMarker, prompt);
    }

    [Fact]
    public void AReplyIsReadIntoItsFilesAndItsReport()
    {
        var reply = WebReply.Parse(Reply(
            "Here is the translation.",
            "",
            "**FILE:** Shop.cs | CSharpEntity",
            "```csharp",
            "namespace Shop;",
            "public class ShopOrderLine { public int ProductId { get; set; } }",
            "```",
            "",
            "### FILE: `FindAggregationGroupingAndHaving.cs` | `CSharpQuery`",
            "",
            "```csharp",
            "public static IQueryable FindAggregationGroupingAndHaving(DbContext ctx) => ctx.Set<ShopOrderLine>();",
            "```",
            "",
            "REPORT:",
            "```json",
            "{ \"model\": \"m\", \"toolsUsed\": [], \"sourcesConsulted\": [], \"assumptions\": [\"none needed\"] }",
            "```"));

        Assert.Null(reply.Unreadable);
        Assert.Collection(
            reply.Files,
            file => Assert.Equal(("Shop.cs", "CSharpEntity", false), (file.Path, file.Role, file.Cut)),
            file => Assert.Equal(("FindAggregationGroupingAndHaving.cs", "CSharpQuery", false), (file.Path, file.Role, file.Cut)));
        Assert.StartsWith("namespace Shop;", reply.Files[0].Content, StringComparison.Ordinal);
        Assert.Contains("\"assumptions\"", reply.Report);
        Assert.Null(WebTrace.Find(reply, reply.Outside));

        var (units, problem) = WebAttempts.Units(reply, ORMEnum.EFCore);
        Assert.Null(problem);
        Assert.Equal(new[] { ConversionContentType.CSharpEntity, ConversionContentType.CSharpQuery }, units.Select(unit => unit.ContentType));
    }

    [Fact]
    public void AReplyThatEndsInsideAFileKeepsTheFileCut()
    {
        var reply = WebReply.Parse(Reply("FILE: Shop.cs | CSharpEntity", "```csharp", "namespace Shop;", "public class ShopOrder"));

        var file = Assert.Single(reply.Files);
        Assert.True(file.Cut);
        Assert.EndsWith("public class ShopOrder", file.Content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("I need the primary keys of the tables first.", "carries no file")]
    [InlineData("FILE: Shop.cs | CSharpEntity\nnamespace Shop;", "is not followed by a code block")]
    [InlineData("FILE: Shop.cs\n```csharp\nnamespace Shop;\n```", "states no role")]
    [InlineData("FILE: Shop.cs | Entity\n```csharp\nnamespace Shop;\n```", "is not one of EFCore's")]
    [InlineData("FILE: ../Shop.cs | CSharpEntity\n```csharp\nnamespace Shop;\n```", "is no path inside")]
    [InlineData("FILE: Shop.cs | CSharpEntity\n```csharp\nnamespace Shop;\n```\nFILE: Shop.cs | CSharpEntity\n```csharp\nnamespace Shop;\n```", "twice")]
    public void AReplyThatGivesNoArtifactSaysWhy(string text, string reason)
    {
        var (units, problem) = WebAttempts.Units(WebReply.Parse(text), ORMEnum.EFCore);

        Assert.Empty(units);
        Assert.NotNull(problem);
        Assert.Contains(reason, problem);
    }

    [Theory]
    [InlineData("As the docs at https://learn.microsoft.com/ef/core say, …\nFILE: Shop.cs | CSharpEntity\n```csharp\nnamespace Shop;\n```", "a link outside its files")]
    [InlineData("FILE: Shop.cs | CSharpEntity\n```csharp\n// as in github.com/MiraStochel/orm-convertor\nnamespace Shop;\n```", "names \"orm-convertor\"")]
    [InlineData("FILE: Shop.cs | CSharpEntity\n```csharp\nnamespace Shop;\n```\nREPORT:\n```json\n{ \"toolsUsed\": [\"web_search\"], \"sourcesConsulted\": [] }\n```", "toolsUsed: web_search")]
    [InlineData("FILE: Shop.cs | CSharpEntity\n```csharp\nnamespace Shop;\n```\nREPORT:\n```json\n{ \"toolsUsed\": [], \"sourcesConsulted\": \"EF Core documentation\" }\n```", "sourcesConsulted: EF Core documentation")]
    [InlineData("FILE: Shop.cs | CSharpEntity\n```csharp\nnamespace Shop;\n```\nREPORT:\n```json\n{ \"toolsUsed\": [], \"sourcesConsulted\": [], \"contextUsed\": [\"memory: the user works on ORM translation\"] }\n```", "contextUsed: memory")]
    public void ATraceOfASearchIsFound(string text, string trace)
    {
        var found = WebTrace.Find(WebReply.Parse(text), text);

        Assert.NotNull(found);
        Assert.Contains(trace, found);
    }

    [Fact]
    public void ALinkInsideAFileAndAnEmptyDeclarationAreNoTrace()
    {
        var text = Reply(
            "FILE: FindMapper.xml | XML",
            "```xml",
            "<!DOCTYPE mapper PUBLIC \"-//mybatis.org//DTD Mapper 3.0//EN\" \"https://mybatis.org/dtd/mybatis-3-mapper.dtd\">",
            "<mapper namespace=\"Shop.FindMapper\"/>",
            "```",
            "REPORT:",
            "```json",
            "{ \"toolsUsed\": [\"none\"], \"sourcesConsulted\": [], \"contextUsed\": [] }",
            "```");

        Assert.Null(WebTrace.Find(WebReply.Parse(text), text));
    }

    /// <summary>
    /// One run of three attempts of the trial cell: the prompts, the operator's table, a reply with
    /// an artifact, a reply with none - a row at level 0 -, and a reply with a link - refused, moved
    /// aside and made again; the operator's measurements survive a second preparation.
    /// </summary>
    [Fact]
    public void AnAttemptGoesFromThePromptToAnArtifactOrARow()
    {
        using var temp = new TempDirectory();
        var run = temp.Path;
        File.WriteAllText(Path.Combine(run, WebCell.FileName), "# the trial cell\nanswers\tsource\ttarget\tvariant\n" + $"{TrialKey}\tDapper\tEFCore\tzero-shot\n");

        var ids = WebAttempts.Prepare(run);

        Assert.Equal(3, ids.Count);
        Assert.Single(Directory.GetDirectories(Path.Combine(run, "inputs")));
        var prompts = ids.Select(id => File.ReadAllText(Path.Combine(WebAttempts.AttemptDirectory(run, id), WebAttempts.PromptFileName))).ToList();
        Assert.Single(prompts.Distinct());

        var table = Path.Combine(run, WebAttempts.AttemptsFileName);
        var rows = WebAttempts.ReadTable(table);
        Assert.All(rows, row => Assert.Equal(WebPrompt.Digest(prompts[0]), row["promptSha256"]));

        // The operator: a chat application, a decimal comma from a spreadsheet.
        File.WriteAllLines(table,
        [
            string.Join('\t', WebAttempts.Columns),
            .. rows.Select(row => string.Join('\t', WebAttempts.Columns.Select(column => column switch
            {
                "model" => "Model X",
                "channel" => "chat",
                "frontend" => "chat.example",
                "seconds" => "37,5",
                _ => row[column],
            }))),
        ]);

        File.WriteAllText(ReplyFile(run,ids[0]), Reply(
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
            "{ \"model\": \"x\", \"secondsWorked\": null, \"secondsBasis\": \"unknown\", \"toolsUsed\": [], \"sourcesConsulted\": [], \"assumptions\": [\"the keys\"] }",
            "```"));
        File.WriteAllText(ReplyFile(run,ids[1]), "I cannot translate this without the primary keys of the tables.");
        File.WriteAllText(ReplyFile(run,ids[2]), Reply(
            "See https://learn.microsoft.com/ef/core/querying for grouping.",
            "FILE: Shop.cs | CSharpEntity",
            "```csharp",
            "namespace Shop;",
            "```"));

        var report = WebAttempts.Import(run);
        foreach (var refused in report.Refused)
        {
            output.WriteLine($"{refused.Id}: {refused.Trace}");
        }

        Assert.Equal(new[] { ids[0] }, report.Imported);
        Assert.Equal(new[] { ids[1] }, report.WithoutArtifact);
        var (refusedId, trace) = Assert.Single(report.Refused);
        Assert.Equal(ids[2], refusedId);
        Assert.Contains("link", trace);
        Assert.Empty(report.Problems);

        var artifact = ForeignArtifact.Read(Path.Combine(run, "artifacts", ids[0]), "schema");
        Assert.Equal((TrialKey, ORMEnum.Dapper, ORMEnum.EFCore, WebAttempts.Producer), (artifact.Key, artifact.Source, artifact.Target, artifact.Producer));
        Assert.Equal(2, artifact.Units.Count);

        using (var metadata = JsonDocument.Parse(artifact.Metadata))
        {
            var root = metadata.RootElement;
            Assert.Equal("Model X", root.GetProperty("model").GetString());
            Assert.Equal("chat", root.GetProperty("channel").GetString());
            Assert.Equal("default", root.GetProperty("temperature").GetString());
            Assert.Equal(WebTemplate.ForVariant["zero-shot"], root.GetProperty("prompt").GetString());
            Assert.Equal(WebPrompt.Digest(prompts[0]), root.GetProperty("promptSha256").GetString());
            Assert.Equal("zero-shot", root.GetProperty("variant").GetString());
            Assert.Equal(1, root.GetProperty("attempt").GetInt32());
            Assert.Equal(37.5m, root.GetProperty("seconds").GetDecimal());
            Assert.Equal(prompts[0].Length, root.GetProperty("promptChars").GetInt32());
            Assert.Equal("the keys", root.GetProperty("selfReport").GetProperty("assumptions")[0].GetString());
            Assert.False(root.TryGetProperty("tokensIn", out _));
        }

        var row = Assert.Single(SummaryRow.Read(Path.Combine(run, WebAttempts.RowsFileName)));
        Assert.Equal((ids[1], 0, ForeignVerdict.Refused, ForeignArtifact.LanguageRefused), (row["artifact"], row.Reached, row.Outcome, row.Language));
        Assert.Contains("carries no file", row["detail"]);
        Assert.Equal("Model X", row.Metadata("model"));

        var attempt = WebAttempts.AttemptDirectory(run, ids[2]);
        Assert.False(File.Exists(Path.Combine(attempt, WebAttempts.ReplyFileName)));
        Assert.True(File.Exists(Path.Combine(attempt, "refused-1.md")));

        // A second preparation keeps what the operator wrote; the attempt made again carries the count of refused replies.
        WebAttempts.Prepare(run);
        Assert.All(WebAttempts.ReadTable(table), prepared => Assert.Equal("Model X", prepared["model"]));

        File.WriteAllText(ReplyFile(run,ids[2]), "Not possible.");
        WebAttempts.Import(run);
        var again = SummaryRow.Read(Path.Combine(run, WebAttempts.RowsFileName)).Single(candidate => candidate["artifact"] == ids[2]);
        Assert.Equal("1", again.Metadata("refusedBefore"));
    }

    [Fact]
    public void AnAttemptWithAReplyKeepsItsPrompt()
    {
        using var temp = new TempDirectory();
        var run = temp.Path;
        File.WriteAllText(Path.Combine(run, WebCell.FileName), $"answers\tsource\ttarget\tvariant\tattempts\n{TrialKey}\tDapper\tEFCore\tzero-shot\t1\n");

        var id = Assert.Single(WebAttempts.Prepare(run));
        File.WriteAllText(ReplyFile(run,id), "Not possible.");

        var table = Path.Combine(run, WebAttempts.AttemptsFileName);
        File.WriteAllText(table, File.ReadAllText(table).Replace(WebAttempts.ReadTable(table)[0]["promptSha256"], "0000", StringComparison.Ordinal));

        var failure = Assert.Throws<InvalidOperationException>(() => WebAttempts.Prepare(run));
        Assert.Contains("has a reply to another prompt", failure.Message);
    }

    [Fact]
    public void ACellOfAVariantWithoutATemplateFailsByName()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, WebCell.FileName), $"answers\tsource\ttarget\tvariant\n{TrialKey}\tDapper\tEFCore\tfew-shot\n");

        var failure = Assert.Throws<InvalidOperationException>(() => WebCell.Read(Path.Combine(temp.Path, WebCell.FileName)));
        Assert.Contains("has no template of the web channel", failure.Message);
    }

    /// <summary>The attempts of a group beside the tool's one row per cell: the share per attempt, best-of-n and the tool's share over the same cells, and a cell the tool has no row of named apart (decision 189).</summary>
    [Fact]
    public void TheSummaryPairsTheAttemptsWithTheTool()
    {
        using var temp = new TempDirectory();
        const string Model = "{\"model\":\"m\",\"channel\":\"chat\",\"variant\":\"zero-shot\"";

        string Row(string id, string answers, string target, string producer, string language, int reached, string outcome, string metadata)
            => string.Join('\t', id, answers, "Dapper", target, producer, language, reached.ToString(System.Globalization.CultureInfo.InvariantCulture), outcome, "", "", "", "0", "0", "0", "0", metadata);

        File.WriteAllLines(Path.Combine(temp.Path, ExperimentRun.RowsFileName),
        [
            ForeignVerdict.Header,
            Row("t1", "matrix:Filtering", "EFCore", "tool", "target", 4, ForeignVerdict.Agrees, "{}"),
            Row("t2", "matrix:Ordering", "EFCore", "tool", "refused", 0, ForeignVerdict.Refused, "{}"),
        ]);
        File.WriteAllLines(Path.Combine(temp.Path, ForeignRows.FileName),
        [
            ForeignVerdict.Header,
            Row("a1", "matrix:Filtering", "EFCore", "llm", "target", 4, ForeignVerdict.Agrees, Model + ",\"attempt\":1}"),
            Row("a2", "matrix:Filtering", "EFCore", "llm", "target", 4, ForeignVerdict.Agrees, Model + ",\"attempt\":2}"),
            Row("a3", "matrix:Filtering", "EFCore", "llm", "target", 1, ForeignVerdict.NotCompiled, Model + ",\"attempt\":3,\"refusedBefore\":1}"),
            Row("c1", "matrix:Projection", "MyBatis", "llm", "target", 4, ForeignVerdict.Agrees, Model + ",\"attempt\":1}"),
        ]);
        File.WriteAllLines(Path.Combine(temp.Path, WebAttempts.RowsFileName),
        [
            ForeignVerdict.Header,
            Row("b1", "matrix:Ordering", "EFCore", "llm", "refused", 0, ForeignVerdict.Refused, Model + ",\"attempt\":1}"),
        ]);

        var lines = ExperimentSummary.Summarize(temp.Path);
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        Assert.Contains("| llm · m · chat · zero-shot | 2 | 4 | 1 | read | 1 of 2 (50 %) | 3 of 4 (75 %) | 1 of 2 (50 %) |", lines);
        Assert.Contains("| | | | | compiled | 1 of 2 (50 %) | 2 of 4 (50 %) | 1 of 2 (50 %) |", lines);
        Assert.Contains("| | | | | agrees | 1 of 2 (50 %) | 2 of 4 (50 %) | 1 of 2 (50 %) |", lines);
        Assert.Contains("| matrix:Filtering Dapper → EFCore | llm · m · chat · zero-shot | 4 agrees | 4 agrees · 4 agrees · 1 not compiled |", lines);
        Assert.Contains(lines, line => line.StartsWith("Cells without a row of the tool", StringComparison.Ordinal) && line.Contains("matrix:Projection Dapper → MyBatis", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("| llm | m | zero-shot | .NET → .NET | 4 | 1 |", StringComparison.Ordinal));
    }

    private static string ExportOne(string root, ForeignKey key, ORMEnum source)
    {
        Assert.Equal(1, ForeignInputs.Export(root, (candidate, from) => candidate == key && from == source));
        return ForeignInputs.DirectoryOf(root, key, source);
    }

    private static string ReplyFile(string run, string id) =>Path.Combine(WebAttempts.AttemptDirectory(run, id), WebAttempts.ReplyFileName);

    private static string Reply(params string[] lines) => string.Join("\n", lines) + "\n";

    private sealed class TempDirectory : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("ormconvertor-web-");

        public string Path => directory.FullName;

        public void Dispose() => directory.Delete(recursive: true);
    }
}
