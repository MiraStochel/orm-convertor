using AbstractWrappers;
using Model;
using OrmConvertor.Factories;

namespace OrmConvertor;

public static class ConversionHandler
{
    public static List<ConversionSource> Convert(
        ORMEnum sourceOrm,
        ORMEnum targetOrm,
        List<ConversionSource> sources
    )
    {
        var entityBuilder = EntityBuilderFactory.Create(targetOrm);
        var queryBuilder = QueryBuilderFactory.Create(targetOrm);

        if (entityBuilder == null)
        {
            throw new InvalidOperationException("Target ORM not supported");
        }

        var results = new List<ConversionSource>();

        // 1) Build entity maps using entity parsers only
        var entityParsers = ParserFactory.Create(sourceOrm, entityBuilder, qb: null)
            .Where(p => !p.CanParse(ConversionContentType.CSharpQuery))
            .ToList();
        foreach (var parser in entityParsers)
        {
            foreach (var src in sources.Where(x => parser.CanParse(x.ContentType)))
            {
                parser.Parse(src.Content);
            }
        }

        // Emit entities for target ORM
        results.AddRange(entityBuilder.Build());

        // 2) Translate each query independently so we can return multiple query outputs
        var querySources = sources.Where(s => s.ContentType == ConversionContentType.CSharpQuery).ToList();
        foreach (var qsrc in querySources)
        {
            var qb = QueryBuilderFactory.Create(targetOrm);
            if (qb is null)
            {
                // Target ORM has no query builder; skip query translation for this target
                continue;
            }

            var queryParsers = ParserFactory.Create(sourceOrm, entityBuilder, qb)
                .Where(p => p.CanParse(ConversionContentType.CSharpQuery))
                .OfType<IQueryParser>()
                .ToList();

            if (queryParsers.Count == 0)
            {
                continue;
            }

            // Use the first available query parser for the source ORM
            var qp = queryParsers.First();
            qp.Parse(qsrc.Content, entityBuilder.EntityMaps);

            results.AddRange(qb.Build());
        }

        return results;
    }
}
