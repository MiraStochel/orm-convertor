using System.IO.Compression;
using AbstractWrappers;
using DatabaseCatalog;
using OrmConvertor;
using ORMConvertorAPI.Data;
using ORMConvertorAPI.Dtos;
using ORMConvertorAPI.Dtos.Advisor;
using ORMConvertorAPI.Services;

namespace ORMConvertorAPI;

public static class Endpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("");

        group.MapGet("/required-content", () => RequiredContent.GetRequiredContent)
            .WithName("RequiredContent")
            .Produces<List<RequiredContentDefinition>>(StatusCodes.Status200OK);

        group.MapGet("/required-content-advisor", () => RequiredContent.GetRequiredContentAdvisor)
            .WithName("RequiredContentAdvisor")
            .Produces<List<AdvisorRequiredContentDefinition>>(StatusCodes.Status200OK);

        group.MapPost("/convert", ConvertHandler)
           .WithName("Convert")
           .Produces<ConvertResponse>(StatusCodes.Status200OK)
           .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/samples", () => Samples.GetSamples)
            .Produces<Dictionary<int, string>>(StatusCodes.Status200OK);

        group.MapGet("/samples-advisor", () => SamplesAdvisor.GetSamples)
            .Produces<Dictionary<int, string>>(StatusCodes.Status200OK);

        // Whole conversion inputs for the explanatory page (decision 099): the direction and
        // the named units, which the page sends to /convert unchanged.
        group.MapGet("/examples", () => Examples.GetExamples)
            .WithName("Examples")
            .Produces<List<ExampleDefinition>>(StatusCodes.Status200OK);

        // The LDBC query catalog for its page (decision 110): the entity units over the LDBC
        // tables and the 41 read queries with how much of each the tool translates.
        group.MapGet("/ldbc", () => Ldbc.GetCatalog)
            .WithName("Ldbc")
            .Produces<LdbcCatalogDefinition>(StatusCodes.Status200OK);

        // The FIBEN query catalog for its page and for the on-demand run of the Java suite
        // (decision 149): the entity units over the FIBEN tables and the 237 queries with
        // their questions, each naming the units it reads.
        group.MapGet("/fiben", () => Fiben.GetCatalog)
            .WithName("Fiben")
            .Produces<FibenCatalogDefinition>(StatusCodes.Status200OK);

        group.MapPost("/advisor-test", AdvisorTestHandler)
            .WithName("AdvisorTest")
              .Produces<AdvisorSolveResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/advisor/run", AdvisorRunHandler)
            .WithName("AdvisorRun")
            .Produces<AdvisorRunResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/archive", ArchiveHandler)
            .WithName("Archive")
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// The one shape a failing endpoint answers with: <c>ProblemDetails</c> per RFC 9457,
    /// which is what every endpoint's <c>.ProducesProblem(400)</c> has described all along
    /// (decision 044). The message travels in <c>detail</c>; it is the same text the handlers
    /// used to return as a bare string, so threat 4 of the threat model - a server or instance
    /// name can appear in it - is unchanged and this is not its fix. The credentials of the
    /// instance do not: the message is text the tool did not compose, so it leaves through
    /// the redaction (decision 160). The refusal is logged with the exception (decision 196),
    /// through the same redaction, so that a defect of the tool behind a 400 leaves a trace
    /// on the instance and not only in the client's window.
    /// </summary>
    private static IResult Failure(Exception e, CredentialRedaction redaction, ILogger log, string endpoint)
    {
        log.LogWarning(e, "{Endpoint} refused a request: {Message}", endpoint, e.Message);
        return Results.Problem(detail: redaction.Redact(e.Message), statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// A request refused for what it is, before any work is done on it: the sentence is the
    /// tool's own and names what the client has to change (decision 196).
    /// </summary>
    private static IResult Refused(string reason, ILogger log, string endpoint)
    {
        log.LogWarning("{Endpoint} refused a request: {Reason}", endpoint, reason);
        return Results.Problem(detail: reason, statusCode: StatusCodes.Status400BadRequest);
    }

    private static readonly char[] CharactersNoFileNameMay =
        ['<', '>', ':', '"', '|', '?', '*', .. Enumerable.Range(0, 32).Select(code => (char)code)];

    /// <summary>
    /// Why a client-named entry may not go into the archive, or null where it may (decision
    /// 198): an entry is a relative path under the directory the archive is unpacked into,
    /// so it has a name, starts with no separator, leaves no directory by <c>..</c> and
    /// carries no character a file name cannot. The only client the tool knows, the
    /// frontend, names entries after the artifacts; anything else is a client's mistake and
    /// is told as one, by name, rather than silently rewritten into a path the client did
    /// not ask for.
    /// </summary>
    private static string? EntryNameRefusal(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "A file of the archive has no name; every entry is named by the client (decision 198).";
        }

        if (name[0] is '/' or '\\')
        {
            return $"The file name '{name}' is an absolute path; an entry of the archive is a path relative to the directory it is unpacked into (decision 198).";
        }

        if (name.Split('/', '\\').Any(segment => segment == ".."))
        {
            return $"The file name '{name}' leaves the directory the archive is unpacked into ('..'), which an entry may not (decision 198).";
        }

        if (name.IndexOfAny(CharactersNoFileNameMay) >= 0)
        {
            return $"The file name '{name}' carries a character no file name may ('<', '>', ':', '\"', '|', '?', '*' or a control character; decision 198).";
        }

        return null;
    }

    // Packs client-named files into a ZIP for the complete-output download of S7
    // (decision 033); translates nothing.
    private static IResult ArchiveHandler(ArchiveRequest req, CredentialRedaction redaction, ILoggerFactory loggers)
    {
        var log = loggers.CreateLogger("ORMConvertorAPI.Archive");

        try
        {
            if (req.Files is null)
            {
                return Refused("The request carries no 'files' list.", log, "/archive");
            }

            for (var i = 0; i < req.Files.Count; i++)
            {
                if (req.Files[i] is null)
                {
                    return Refused($"File {i + 1} of the archive is null; a file has a name and a content.", log, "/archive");
                }

                if (EntryNameRefusal(req.Files[i].Name) is { } refusal)
                {
                    return Refused(refusal, log, "/archive");
                }
            }

            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in req.Files)
                {
                    var entry = archive.CreateEntry(file.Name);
                    using var writer = new StreamWriter(entry.Open());
                    writer.Write(file.Content);
                }
            }
            return Results.File(buffer.ToArray(), "application/zip", "conversion.zip");
        }
        catch (Exception e)
        {
            return Failure(e, redaction, log, "/archive");
        }
    }

    private static IResult ConvertHandler(
        ConvertRequest req, IConfiguration configuration, ParseLimits limits, CredentialRedaction redaction, ILoggerFactory loggers)
    {
        var log = loggers.CreateLogger("ORMConvertorAPI.Convert");

        try
        {
            // What the request has to be before any of it is read: a list of units, none of
            // them absent. A unit with blank content is an ordinary input the orchestration
            // leaves out (decision 081); a missing unit is a request nobody can act on.
            if (req.Sources is null)
            {
                return Refused("The request carries no 'sources' list.", log, "/convert");
            }

            var missing = req.Sources.IndexOf(null!);
            if (missing >= 0)
            {
                return Refused($"Source unit {missing + 1} is null; a unit declares a content type and a content.", log, "/convert");
            }

            // The optional input of decision 015: with it the completion phase fills the
            // intermediate representation from the catalog, without it the translation
            // proceeds on conventions and the records say so.
            var catalogConnectionString = configuration.GetConnectionString("CatalogDatabase");

            // The limits come from the container, not from req: an operator's fact, not the
            // sender's (decision 092).
            var converted = ConversionHandler.Convert(
                req.SourceOrm, req.TargetOrm, req.Sources, catalogConnectionString, req.DeclaredSourceDialect, limits);

            // A defect of the tool met during the run is a Failure record for the caller and
            // an exception for whoever runs the instance (decision 196): the record says what
            // was not translated, the log says why, with the stack, through the redaction.
            foreach (var defect in converted.Defects)
            {
                log.LogError(
                    defect.Exception,
                    "Translation defect in run {RunId} ({Source} -> {Target}): unit {Unit}, entity {Entity}, query {Query}",
                    converted.RunId, converted.SourceFramework, converted.TargetFramework, defect.Unit, defect.Entity, defect.Query);
            }

            return Results.Ok(new ConvertResponse(
                converted.RunId,
                converted.ToolVersion,
                converted.SourceFramework,
                converted.SourceFrameworkVersion,
                converted.TargetFramework,
                converted.TargetFrameworkVersion,
                converted.TargetDatabaseDialect,
                converted.Sources,
                converted.Records,
                converted.Facts,
                converted.CatalogState,
                converted.CatalogReadTime?.TotalMilliseconds,
                converted.DeclaredSourceDialect,
                converted.MaxNestingDepth,
                converted.MaxQueryTextLength));
        }
        catch (Exception e)
        {
            return Failure(e, redaction, log, "/convert");
        }
    }

    private static IResult AdvisorTestHandler(AdvisorSolveRequest req, CredentialRedaction redaction, ILoggerFactory loggers)
    {
        try
        {
            int[] selected = new int[req.F];
            int[] assignment = new int[req.Q];
            int status = Advisor.Advisor.Solve(
                req.Memory, req.Cost, req.Z, req.MEM, req.N, req.Q, req.F,
                out int objective, selected, assignment
            );
            var response = new AdvisorSolveResponse(
                status, objective, (int[])selected.Clone(), (int[])assignment.Clone()
            );
            return Results.Ok(response);
        }
        catch (Exception e)
        {
            return Failure(e, redaction, loggers.CreateLogger("ORMConvertorAPI.Advisor"), "/advisor-test");
        }
    }

    private static async Task<IResult> AdvisorRunHandler(
        AdvisorRunRequest req,
        IAdvisorRunCoordinator coordinator,
        CredentialRedaction redaction,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await coordinator.RunAsync(req, cancellationToken);
            return Results.Ok(result);
        }
        catch (Exception e)
        {
            return Failure(e, redaction, loggers.CreateLogger("ORMConvertorAPI.Advisor"), "/advisor/run");
        }
    }
}
