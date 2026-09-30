using Model;
using ORMConvertorAPI.Dtos;
using SampleData;

namespace ORMConvertorAPI.Data;

/// <summary>
/// The LDBC query catalog (decision 110) for the page that shows it. The content lives in
/// <see cref="LdbcSnbSample"/>, beside the other inputs the server hands out, and the suite
/// holds every claim it makes (<c>Combined/LdbcCatalogTest</c>); this class only shapes it for
/// the wire.
/// </summary>
public static class Ldbc
{
    public static LdbcCatalogDefinition GetCatalog =>
        new(
            ORMEnum.Dapper,
            [.. LdbcSnbSample.Entities.Select(entity => new ConversionSource
            {
                Name = entity.FileName,
                ContentType = ConversionContentType.CSharpEntity,
                Content = entity.Content,
            })],
            [.. LdbcSnbSample.Queries.Select(query => new LdbcQueryDefinition(
                query.Key,
                query.Workload,
                query.Number,
                query.Title,
                query.Translation,
                query.Note,
                query.Sql,
                [.. query.Parameters.Select(parameter => new LdbcParameterDefinition(
                    parameter.Name, parameter.SqlType, parameter.Example, parameter.IsList))],
                [.. query.Refusals.Select(refusal => new LdbcRefusalDefinition(refusal.Target, refusal.Reason))]))]);
}
