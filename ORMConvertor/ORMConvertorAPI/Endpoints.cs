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
        var group = app.MapGroup("/orm");

        group.MapGet("/required-content", () => RequiredContent.GetRequiredContent)
            .WithName("RequiredContent")
            .Produces<List<RequiredContentDefinition>>(StatusCodes.Status200OK)
            .WithOpenApi();

        group.MapGet("/required-content-advisor", () => RequiredContent.GetRequiredContentAdvisor)
            .WithName("RequiredContentAdvisor")
            .Produces<List<RequiredContentDefinition>>(StatusCodes.Status200OK)
            .WithOpenApi();

        group.MapPost("/convert", ConvertHandler)
           .WithName("Convert")
           .Produces<ConvertResponse>(StatusCodes.Status200OK)
           .ProducesProblem(StatusCodes.Status400BadRequest)
           .WithOpenApi();

        group.MapGet("/samples", () => Samples.GetSamples)
            .Produces<Dictionary<int, string>>(StatusCodes.Status200OK)
            .WithOpenApi();

        group.MapGet("/samples-advisor", () => SamplesAdvisor.GetSamples)
            .Produces<Dictionary<int, string>>(StatusCodes.Status200OK)
            .WithOpenApi();

        group.MapPost("/advisor-test", AdvisorTestHandler)
            .WithName("AdvisorTest")
              .Produces<AdvisorSolveResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status400BadRequest)
              .WithOpenApi();

        group.MapPost("/advisor/run", AdvisorRunHandler)
            .WithName("AdvisorRun")
            .Produces<AdvisorRunResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithOpenApi();
    }

    private static IResult ConvertHandler(ConvertRequest req)
    {
        try
        {
            var converted = ConversionHandler.Convert(req.SourceOrm, req.TargetOrm, req.Sources);
            return Results.Ok(new ConvertResponse(converted));
        }
        catch (Exception e)
        {
            return Results.BadRequest(e.Message);
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
            return Results.BadRequest(e.Message);
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
            return Results.BadRequest(e.Message);
        }
    }
}
