using Model;
using ORMConvertorAPI.Dtos;
using SampleData;

namespace ORMConvertorAPI.Data;

/// <summary>
/// The FIBEN query catalog (decision 149) for the page that shows it and for the Java suite's
/// on-demand run, which takes it from here (decision 078). The content lives in
/// <see cref="FibenSample"/>; this class only shapes it for the wire.
/// </summary>
public static class Fiben
{
    public static FibenCatalogDefinition GetCatalog =>
        new(
            ORMEnum.Dapper,
            FibenSample.DataSet,
            [.. FibenSample.Entities.Select(entity => new ConversionSource
            {
                Name = entity.FileName,
                ContentType = ConversionContentType.CSharp,
                Content = entity.Content,
            })],
            [.. FibenSample.Queries.Select(query => new FibenQueryDefinition(
                query.Key,
                query.Number,
                query.Nesting,
                [.. query.Questions],
                [.. query.Tables],
                [.. query.Columns],
                query.Ordered,
                [.. query.OrderingKey],
                query.Sql,
                query.BenchmarkSql,
                query.Translation,
                query.Note))]);
}
