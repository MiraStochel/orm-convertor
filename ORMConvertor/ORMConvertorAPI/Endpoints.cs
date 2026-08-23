using System.IO.Compression;
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
            .Produces<List<RequiredContentDefinition>>(StatusCodes.Status200OK);

        group.MapPost("/convert", ConvertHandler)
           .WithName("Convert")
           .Produces<ConvertResponse>(StatusCodes.Status200OK)
           .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/samples", () => Samples.GetSamples)
            .Produces<Dictionary<int, string>>(StatusCodes.Status200OK);

        group.MapGet("/samples-advisor", () => SamplesAdvisor.GetSamples)
            .Produces<Dictionary<int, string>>(StatusCodes.Status200OK);

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
    /// name can appear in it - is unchanged and this is not its fix.
    /// </summary>
    private static IResult Failure(Exception e) =>
        Results.Problem(detail: e.Message, statusCode: StatusCodes.Status400BadRequest);

    // Packs client-named files into a ZIP for the complete-output download of S7
    // (decision 033); translates nothing.
    private static IResult ArchiveHandler(ArchiveRequest req)
    {
        try
        {
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
            return Failure(e);
        }
    }

    private static IResult ConvertHandler(ConvertRequest req, IConfiguration configuration)
    {
        try
        {
            // The optional input of decision 015: with it the completion phase fills the
            // intermediate representation from the catalog, without it the translation
            // proceeds on conventions and the records say so.
            var catalogConnectionString = configuration.GetConnectionString("CatalogDatabase");

            var converted = ConversionHandler.Convert(req.SourceOrm, req.TargetOrm, req.Sources, catalogConnectionString);
            return Results.Ok(new ConvertResponse(
                converted.RunId,
                converted.ToolVersion,
                converted.SourceFramework,
                converted.SourceFrameworkVersion,
                converted.TargetFramework,
                converted.TargetFrameworkVersion,
                converted.Sources,
                converted.Records,
                converted.CatalogState,
                converted.CatalogReadTime?.TotalMilliseconds));
        }
        catch (Exception e)
        {
            return Failure(e);
        }
    }

    private static IResult AdvisorTestHandler(AdvisorSolveRequest req)
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
            return Failure(e);
        }
    }

    private static async Task<IResult> AdvisorRunHandler(
        AdvisorRunRequest req,
        IAdvisorRunCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await coordinator.RunAsync(req, cancellationToken);
            return Results.Ok(result);
        }
        catch (Exception e)
        {
            return Failure(e);
        }
    }
}
