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

        var parsers = ParserFactory.Create(sourceOrm, entityBuilder, queryBuilder);

        if (parsers.Count == 0)
        {
            throw new InvalidOperationException("Source ORM not supported");
        }

        var results = new List<ConversionSource>();

        // First, feed all entity-like sources to their parsers to accumulate multiple entities
        foreach (var parser in parsers)
        {
            if (parser.CanParse(ConversionContentType.CSharpQuery) && queryBuilder == null)
            {
                continue;
            }

            var matching = sources.Where(x => parser.CanParse(x.ContentType)).ToList();
            if (matching.Count == 0)
            {
                continue;
            }

            // For query parsers, parse the first matching query only (advisor composes per-query requests)
            if (parser is IQueryParser qp)
            {
                var first = matching.First();
                // With multiple entities present, table resolution can rely on entity attributes later; pass null map
                qp.Parse(first.Content, null);
                continue;
            }

            // For entity parsers (CSharp and XML), parse all matching sources to accumulate multiple entities
            foreach (var src in matching)
            {
                parser.Parse(src.Content);
            }
        }

        results.AddRange(entityBuilder.Build());
        if (queryBuilder != null && sources.Any(x => x.ContentType == ConversionContentType.CSharpQuery))
        {
            results.AddRange(queryBuilder.Build());
        }

        return results;
    }
}
