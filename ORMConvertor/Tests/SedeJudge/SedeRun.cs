using Model;
using SampleData;
using Tests.CatalogRuns;

namespace Tests.SedeJudge;

/// <summary>
/// The SEDE catalog as the on-demand run reads it (decision 208): the catalog's queries in the
/// shape of <see cref="CatalogRun"/>, grouped by the part of the set, each with the entities of
/// the tables it reads, its text as one more unit and the values of its parameters. The run
/// itself is <see cref="CatalogRun"/>, the same for FIBEN and SEDE.
/// </summary>
internal static class SedeRun
{
    public static CatalogDefinition Definition { get; } = new(
        "SEDE",
        CatalogDatabase.Sede,
        "sede.dataset",
        SedeSample.DataSet,
        [.. SedeSample.Queries.Select(query => new CatalogRunQuery(
            query.Key,
            query.Set.ToString(),
            query.Columns,
            query.OrderingKey,
            () => Units(query),
            query.Sql,
            [.. query.Parameters.Select(parameter => (parameter.Name, parameter.SqlType, parameter.Value))],
            query.Translation != SedeTranslation.NotTranslated))]);

    /// <summary>The run of this test run over the SEDE catalog, made by the first test that asks for it.</summary>
    public static CatalogRun Current => CatalogRun.Of(Definition);

    /// <summary>The units of a conversion: the entities of the tables the query reads, then the query as one more unit.</summary>
    internal static List<ConversionSource> Units(SedeQuery query) =>
    [
        .. SedeSample.EntitiesOf(query).Select(entity => new ConversionSource
        {
            Name = entity.FileName,
            ContentType = ConversionContentType.CSharp,
            Content = entity.Content,
        }),
        new ConversionSource { Name = $"{query.Key}.sql", ContentType = ConversionContentType.SqlQuery, Content = query.Sql },
    ];
}
