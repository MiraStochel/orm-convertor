using Model;
using SampleData;
using Tests.CatalogRuns;

namespace Tests.FibenJudge;

/// <summary>
/// The FIBEN catalog as the on-demand run reads it (decision 149): the catalog's queries in the
/// shape of <see cref="CatalogRun"/>, grouped by Kim's type, each with the entities of the
/// tables it reads and its text as one more unit, without parameters - the benchmark writes
/// every value into the text. The run itself is <see cref="CatalogRun"/>, the same for FIBEN
/// and SEDE.
/// </summary>
internal static class FibenRun
{
    public static CatalogDefinition Definition { get; } = new(
        "FIBEN",
        CatalogDatabase.Fiben,
        "fiben.dataset",
        FibenSample.DataSet,
        [.. FibenSample.Queries.Select(query => new CatalogRunQuery(
            query.Key,
            query.Nesting.ToString(),
            query.Columns,
            query.OrderingKey,
            () => Units(query),
            [],
            query.Translation != FibenTranslation.NotTranslated))]);

    /// <summary>The run of this test run over the FIBEN catalog, made by the first test that asks for it.</summary>
    public static CatalogRun Current => CatalogRun.Of(Definition);

    /// <summary>The units of a conversion: the entities of the tables the query reads, then the query as one more unit.</summary>
    internal static List<ConversionSource> Units(FibenQuery query) =>
    [
        .. FibenSample.EntitiesOf(query).Select(entity => new ConversionSource
        {
            Name = entity.FileName,
            ContentType = ConversionContentType.CSharp,
            Content = entity.Content,
        }),
        new ConversionSource { Name = $"{query.Key}.sql", ContentType = ConversionContentType.SqlQuery, Content = query.Sql },
    ];
}
