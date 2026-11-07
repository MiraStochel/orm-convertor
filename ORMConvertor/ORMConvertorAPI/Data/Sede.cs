using Model;
using ORMConvertorAPI.Dtos;
using SampleData;

namespace ORMConvertorAPI.Data;

/// <summary>
/// The SEDE query catalog (decision 208) for the page that shows it and for the Java suite's
/// on-demand run, which takes it from here (decision 078). The content lives in
/// <see cref="SedeSample"/>; this class only shapes it for the wire.
/// </summary>
public static class Sede
{
    public static SedeCatalogDefinition GetCatalog =>
        new(
            ORMEnum.Dapper,
            SedeSample.DataSet,
            [.. SedeSample.Entities.Select(entity => new ConversionSource
            {
                Name = entity.FileName,
                ContentType = ConversionContentType.CSharp,
                Content = entity.Content,
            })],
            [.. SedeSample.Queries.Select(query => new SedeQueryDefinition(
                query.Key,
                query.QuerySetId,
                query.Set,
                query.Title,
                query.Description,
                [.. query.Parameters.Select(parameter => new SedeParameterDefinition(parameter.Name, parameter.SqlType, parameter.Value))],
                [.. query.Tables],
                [.. query.Columns],
                query.Ordered,
                [.. query.OrderingKey],
                query.Sql,
                query.SedeSql,
                query.Translation,
                query.Note))]);
}
