using System.Text.RegularExpressions;
using AbstractWrappers.Descriptors;
using Model;
using Tests.Database;

namespace Tests.Combined;

/// <summary>
/// One query written in the language of a source framework, per source framework that can
/// write it, together with what its translation has to contain per target and which target
/// refuses it by its descriptor. The unit under test is a category of requirement T2 - or the
/// deliberately bad query of <see cref="QueryShapeInputs.DeeplyNested"/> - and the matrix
/// tests run every shape through every direction the enum yields.
/// </summary>
/// <param name="Name">The category, as the shared manifest names it (the section of <c>categories.txt</c>) and as the theory data names it.</param>
/// <param name="Sources">The query units per source framework - one, or for MyBatis the mapper and the interface that types its parameters. A source absent here cannot state the shape in its language.</param>
/// <param name="Hallmarks">Substrings every query artifact of the target has to contain, joined over all query artifacts.</param>
/// <param name="RefusedBy">Targets whose descriptor cannot express the shape, with the feature the refusal names.</param>
/// <param name="RefusedWithoutCatalog">Sources that can write the shape but whose reading the tool refuses by a stated rule in a run without a catalog, with the feature the refusal names - a Dapper parameter, whose scalar takes a mapping the source does not state (decision 083) and the catalog supplies on the query's own demand (decision 105). The matrices here convert dry, so for them it is a refusal; a run with a catalog holding the domain yields the artifact.</param>
public sealed record QueryShape(
    string Name,
    IReadOnlyDictionary<ORMEnum, IReadOnlyList<ConversionSource>> Sources,
    IReadOnlyDictionary<ORMEnum, string[]> Hallmarks,
    IReadOnlyDictionary<ORMEnum, QueryFeature> RefusedBy,
    IReadOnlyDictionary<ORMEnum, QueryFeature> RefusedWithoutCatalog)
{
    public override string ToString() => Name;
}

/// <summary>
/// The inputs of the query-shape matrices: one domain of five entities in the languages of
/// all six frameworks, the query categories of requirement T2 written once per source
/// language over it, and one deeply nested query per source that every target has to carry.
///
/// All of it is read from <c>Tests/Database/QueryShapes</c>, the place both suites read
/// from (the mechanism of decision 089): the .NET suite embeds the files, the Java suite
/// takes them as a test resource, and the Java targets are judged over the very same
/// inputs at verification levels 2 and 3 (<c>shapes/QueryCategoryTest</c> and
/// <c>shapes/DeeplyNestedQueryTest</c> there). Which sources state a category, which
/// target refuses it and which source the tool refuses it from without a catalog is stated once, in
/// <c>categories.txt</c> beside the files, and both suites read it there. What stays here
/// is the .NET side's own assertion - the hallmarks the target's text has to carry - keyed
/// by the manifest's section, and the two lists are checked against each other when the
/// categories are read, so neither can name a category the other lacks.
///
/// The domain is the shape of the shared fixture schema (<c>Tests/Database/TestSchema.sql</c>):
/// customers, orders under a two-part key, order lines under a three-part key whose leading
/// parts are a multi-column foreign key, allocations under a four-part key, and products -
/// so the joins here run over two and three columns, which is what makes the join category
/// worth measuring. Since the differential matrix measures every category at the fourth
/// level (decision 089), the domain also has read-only data of its own,
/// <c>QueryShapes/FixtureData.sql</c>, in the fixture's schema beside the fixture's own
/// tables: the shared files carry the <c>{{schema}}</c> placeholder for it and every
/// reader substitutes <see cref="Schema"/>, so a dry text and a run against the database
/// read the same input. The five entities carry the prefix <c>Shop</c> - <c>ShopOrder</c>,
/// <c>ShopOrderLine</c> and so on - and every source names their tables by the plural, which
/// is what the singular-plural rule of decision 050 derives from the class for the two
/// sources that state no table; the prefix is what keeps those tables apart from the
/// fixture schema's own <c>Customers</c>, <c>OrderLines</c> and <c>Products</c>, a table
/// found under two names being a match the catalog refuses to guess at.
/// </summary>
public static class QueryShapeInputs
{
    public const string Namespace = "Shop";

    /// <summary>
    /// The schema of the domain, substituted for the placeholder of every shared file: the
    /// schema of the test fixture, which holds its read-only data. Declared before the
    /// hallmark table below, which interpolates it.
    /// </summary>
    public static readonly string Schema = TestDatabase.SchemaName;

    /// <summary>The table of the order entity, as every source of the domain names it.</summary>
    public const string OrdersTable = "ShopOrders";

    private const string ResourcePrefix = "Tests.Database.QueryShapes.";
    private const string SchemaPlaceholder = "{{schema}}";

    /// <summary>Every source x target pair, identity directions included, for a theory over one shape.</summary>
    public static TheoryData<QueryShape, ORMEnum, ORMEnum> Directions(IEnumerable<QueryShape> shapes)
    {
        var data = new TheoryData<QueryShape, ORMEnum, ORMEnum>();
        foreach (var shape in shapes)
        {
            foreach (var source in shape.Sources.Keys)
            {
                foreach (var target in Enum.GetValues<ORMEnum>())
                {
                    data.Add(shape, source, target);
                }
            }
        }

        return data;
    }

    /// <summary>The mapping units of the source followed by the shape's query units - a whole conversion input.</summary>
    public static List<ConversionSource> Units(ORMEnum source, QueryShape shape) =>
        [.. MappingUnits(source), .. shape.Sources[source]];

    /// <summary>What the framework reads the five entities from, as the shared files state them.</summary>
    public static List<ConversionSource> MappingUnits(ORMEnum framework) => framework switch
    {
        ORMEnum.Dapper => [Unit("entities/dapper/Shop.cs")],
        ORMEnum.EFCore => [Unit("entities/efcore/Shop.cs")],
        ORMEnum.NHibernate => [Unit("entities/nhibernate/Shop.cs"), Unit("entities/nhibernate/Shop.hbm.xml")],
        ORMEnum.Hibernate or ORMEnum.EclipseLink => JpaEntities.Select(e => Unit($"entities/jpa/{e}.java")).ToList(),
        ORMEnum.MyBatis =>
        [
            .. JpaEntities.Select(e => Unit($"entities/mybatis/{e}.java")),
            Unit("entities/mybatis/ShopMapper.xml"),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(framework), framework, $"{framework} has no inputs in {nameof(QueryShapeInputs)}; its wrapper brings them."),
    };

    private static readonly string[] JpaEntities = ["ShopCustomer", "ShopOrder", "ShopOrderLine", "ShopOrderLineAllocation", "ShopProduct"];

    /// <summary>
    /// Text of one shared file under <c>Tests/Database/QueryShapes</c>, embedded by
    /// Tests.csproj; a new file needs no entry there, but it does need to be in the working
    /// copy. The Java suite reads the same path from its test resources, and substitutes the
    /// schema placeholder the same way (<c>InputUnit.fromShared</c>).
    /// </summary>
    public static string Read(string path)
    {
        var resource = ResourcePrefix + path.Replace('/', '.');
        using var stream = typeof(QueryShapeInputs).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The query-shape resource \"{resource}\" is missing under Tests/Database/QueryShapes.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("﻿", string.Empty).Replace(SchemaPlaceholder, Schema, StringComparison.Ordinal);
    }

    /// <summary>One shared file as a unit of the conversion input, its language taken from its name.</summary>
    private static ConversionSource Unit(string path) => new()
    {
        Name = path[(path.LastIndexOf('/') + 1)..],
        Content = Read(path),
        ContentType = SharedInputs.ContentTypeOf(path),
    };

    // ---- the query categories of T2 ------------------------------------------------------

    /// <summary>
    /// The categories, one shape each, in the order <c>categories.txt</c> states them; the
    /// theory data of the matrix tests. Read on first use, after the hallmark table below
    /// exists.
    /// </summary>
    public static IReadOnlyList<QueryShape> Categories => CategoriesRead.Value;

    private static readonly Lazy<IReadOnlyList<QueryShape>> CategoriesRead = new(ReadCategories);

    private static IReadOnlyList<QueryShape> ReadCategories()
    {
        var shapes = new List<QueryShape>();

        foreach (var (id, values) in SharedInputs.Sections("categories.txt", Read("categories.txt").Split('\n')))
        {
            if (!CategoryHallmarks.TryGetValue(id, out var hallmarks))
            {
                throw new InvalidOperationException(
                    $"categories.txt states the category [{id}] and {nameof(QueryShapeInputs)} has no hallmarks for it; the two are one list.");
            }

            var units = new Dictionary<ORMEnum, string[]>();
            var refusedBy = new Dictionary<ORMEnum, QueryFeature>();
            var refusedWithoutCatalog = new Dictionary<ORMEnum, QueryFeature>();

            foreach (var (key, value) in values)
            {
                switch (key)
                {
                    case "refusedBy":
                        refusedBy = Refusals(id, value);
                        break;
                    case "refusedWithoutCatalog":
                        refusedWithoutCatalog = Refusals(id, value);
                        break;
                    default:
                        if (!Enum.TryParse<ORMEnum>(key, ignoreCase: false, out var source) || !Enum.IsDefined(source))
                        {
                            throw new InvalidOperationException($"categories.txt: [{id}] has the key \"{key}\", which is neither a framework nor a refusal.");
                        }

                        units[source] = [.. SharedInputs.List(value).Select(path => $"{id}/{path}")];
                        break;
                }
            }

            shapes.Add(Shape(id, units, hallmarks, refusedBy, refusedWithoutCatalog));
        }

        var unstated = CategoryHallmarks.Keys.Except(shapes.Select(shape => shape.Name)).ToList();
        if (unstated.Count > 0)
        {
            throw new InvalidOperationException(
                $"{nameof(QueryShapeInputs)} has hallmarks for {string.Join(", ", unstated)}, which categories.txt does not state; the two are one list.");
        }

        return shapes;
    }

    /// <summary>A refusal list of the manifest: <c>Framework:Feature</c> entries, both spelled as the enums spell them.</summary>
    private static Dictionary<ORMEnum, QueryFeature> Refusals(string id, string value)
    {
        var refusals = new Dictionary<ORMEnum, QueryFeature>();

        foreach (var entry in SharedInputs.List(value))
        {
            var colon = entry.IndexOf(':');
            if (colon < 0
                || !Enum.TryParse<ORMEnum>(entry[..colon], ignoreCase: false, out var framework) || !Enum.IsDefined(framework)
                || !Enum.TryParse<QueryFeature>(entry[(colon + 1)..], ignoreCase: false, out var feature) || !Enum.IsDefined(feature))
            {
                throw new InvalidOperationException($"categories.txt: [{id}] states the refusal \"{entry}\", which is not written as Framework:Feature.");
            }

            refusals[framework] = feature;
        }

        return refusals;
    }

    /// <summary>
    /// What the target's text has to carry, per category of the manifest. The .NET side's
    /// own assertion - level 1 over the artifact - and therefore here rather than in the
    /// shared file; the Java suite asks the framework instead of the text.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<ORMEnum, string[]>> CategoryHallmarks = new()
    {
        ["Projection"] = Hallmarks(
            sql: ["ol.Description AS Text", "ol.Quantity AS Qty"],
            linq: ["Text = ol.Description", "Qty = ol.Quantity"],
            hql: ["ol.Description as Text", "ol.Quantity as Qty"],
            jpa: ["ol.Description as Text", "ol.Quantity as Qty"]),

        ["Filtering"] = Hallmarks(
            sql: ["WHERE", "ol.Quantity > 5 OR ol.UnitPrice >= 100.5", "ol.Description IS NOT NULL", "NOT ("],
            linq: [".Where(", "ol.Quantity > 5 || ol.UnitPrice >= 100.5", "ol.Description != null", "!("],
            hql: ["where", "ol.Quantity > 5 or ol.UnitPrice >= 100.5", "ol.Description is not null", "not ("],
            jpa: ["where", "ol.Quantity > 5 or ol.UnitPrice >= 100.5", "ol.Description is not null", "not ("]),

        // The aliases are the ones every source wrote: a LINQ source names the joined row
        // after the result selector's parameter, o here as well, and the filter and the
        // projection over the joined table reach every target. A LINQ join writes its
        // condition outer table first, which is why the hallmarks name no order.
        ["JoinOverTwoColumns"] = Hallmarks(
            sql: [$"INNER JOIN {Schema}.", ".CompanyId = ", ".OrderId = ", " AND ", "ol.Description AS Text", "WHERE o.CustomerId > 0"],
            linq: [".Join(", "ctx.Set<ShopOrder>()", "ol.CompanyId", "ol.OrderId", ".o.CustomerId > 0", "Text = ", ".ol.Description"],
            hql: ["inner join ShopOrder o", " with ", ".CompanyId = ", ".OrderId = ", " and ", "ol.Description as Text", "where o.CustomerId > 0"],
            jpa: ["join ShopOrder o", " on ", ".CompanyId = ", ".OrderId = ", " and ", "ol.Description as Text", "where o.CustomerId > 0"]),

        ["AggregationGroupingAndHaving"] = Hallmarks(
            sql: ["GROUP BY ol.ProductId", "HAVING SUM(ol.Quantity) > 10", "COUNT(*)"],
            linq: [".GroupBy(", "g.Sum(", "g.Count()", "g.Key"],
            hql: ["group by ol.ProductId", "having sum(ol.Quantity) > 10", "count(*)"],
            jpa: ["group by ol.ProductId", "having sum(ol.Quantity) > 10", "count(ol)"]),

        ["Ordering"] = Hallmarks(
            sql: ["ORDER BY ol.ProductId ASC, ol.Quantity DESC"],
            linq: [".OrderBy(ol => ol.ProductId)", ".ThenByDescending(ol => ol.Quantity)"],
            hql: ["order by ol.ProductId asc, ol.Quantity desc"],
            jpa: ["order by ol.ProductId asc, ol.Quantity desc"]),

        ["PaginationWithBoundCounts"] = Hallmarks(
            sql: ["OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY"],
            linq: [".Skip(skip)", ".Take(take)"],
            hql: [".SetFirstResult(skip)", ".SetMaxResults(take)"],
            jpa: [".setFirstResult(skip)", ".setMaxResults(take)"],
            myBatis: ["OFFSET #{skip} ROWS FETCH NEXT #{take} ROWS ONLY"]),

        ["SubqueryAsTheRightSideOfIn"] = Hallmarks(
            sql: ["IN (SELECT p.ProductId", "p.UnitPrice > 100"],
            // The member's value where the column is nullable (`ol.ProductId.Value`), as the
            // collection-parameter row has it: a MyBatis source declares the column as Integer.
            linq: ["ctx.Set<ShopProduct>()", ".Contains(ol.ProductId", "p.UnitPrice > 100"],
            hql: ["in (select p.ProductId", "p.UnitPrice > 100"],
            jpa: ["in (select p.ProductId", "p.UnitPrice > 100"]),

        ["CorrelatedExistsOverThreeColumns"] = Hallmarks(
            sql: ["EXISTS (SELECT", "a.LineNumber = ol.LineNumber"],
            linq: [".Any(", "a.LineNumber == ol.LineNumber"],
            hql: ["exists (", "a.LineNumber = ol.LineNumber"],
            jpa: ["exists (", "a.LineNumber = ol.LineNumber"]),

        ["ScalarSubquery"] = Hallmarks(
            sql: ["(SELECT AVG(p.UnitPrice)"],
            linq: [".Average(p => p.UnitPrice)"],
            hql: ["(select avg(p.UnitPrice)"],
            jpa: ["(select avg(p.UnitPrice)"]),

        ["SetOperation"] = Hallmarks(
            sql: ["UNION", "p.ProductName AS Text"],
            linq: [".Union(", "ctx.Set<ShopProduct>()"],
            jpa: ["union", "p.ProductName as Text"]),

        ["DistinctProjection"] = Hallmarks(
            sql: ["SELECT DISTINCT ol.ProductId"],
            linq: [".Distinct()"],
            hql: ["select distinct ol.ProductId"],
            jpa: ["select distinct ol.ProductId"]),

        ["InOverAListOfValues"] = Hallmarks(
            sql: ["ol.ProductId IN (1, 2, 3)"],
            // Without the `new[]`: against a nullable column the array declares its
            // element type (`new int?[]`), which a MyBatis source's Integer field is.
            linq: ["{ 1, 2, 3 }.Contains(ol.ProductId)"],
            hql: ["ol.ProductId in (1, 2, 3)"],
            jpa: ["ol.ProductId in (1, 2, 3)"]),

        ["ScalarParameter"] = Hallmarks(
            sql: ["ol.Quantity >= @minQuantity", "int minQuantity"],
            linq: ["ol.Quantity >= minQuantity", "int minQuantity"],
            hql: ["ol.Quantity >= :minQuantity", ".SetParameter(\"minQuantity\", minQuantity)"],
            jpa: ["ol.Quantity >= :minQuantity", ".setParameter(\"minQuantity\", minQuantity)"],
            myBatis: ["ol.Quantity &gt;= #{minQuantity}", "@Param(\"minQuantity\")"]),

        ["CollectionParameter"] = Hallmarks(
            sql: ["ol.ProductId IN @ids"],
            // The member's value where the column is nullable (`ol.ProductId.Value`).
            linq: ["ids.Contains(ol.ProductId"],
            hql: ["ol.ProductId in (:ids)", ".SetParameterList(\"ids\", ids)"],
            jpa: ["ol.ProductId in :ids"],
            myBatis: ["<foreach"]),

        ["ConstantOfAMoment"] = Hallmarks(
            sql: ["o.PlacedAt > '2025-01-01 00:00:00'"],
            linq: ["o.PlacedAt > DateTime.Parse(\"2025-01-01 00:00:00\")"],
            hql: ["o.PlacedAt > '2025-01-01 00:00:00'"],
            jpa: ["o.PlacedAt > {ts '2025-01-01 00:00:00'}"],
            myBatis: ["o.PlacedAt &gt; '2025-01-01 00:00:00'"]),

        // The LINQ target gets the translated form of decision 051.
        ["LikeWithAnAnchoredPattern"] = Hallmarks(
            sql: ["p.ProductName LIKE 'W%'"],
            linq: ["p.ProductName.StartsWith(\"W\")"],
            hql: ["p.ProductName like 'W%'"],
            jpa: ["p.ProductName like 'W%'"]),

        ["CountOverDistinctValues"] = Hallmarks(
            sql: ["COUNT(DISTINCT ol.OrderId) AS Orders", "GROUP BY ol.ProductId"],
            linq: [".GroupBy(", ".Distinct().Count()", "g.Key"],
            hql: ["count(distinct ol.OrderId) as Orders", "group by ol.ProductId"],
            jpa: ["count(distinct ol.OrderId) as Orders", "group by ol.ProductId"]),

        // The LINQ target splits the pattern past the escape and the core goes out without
        // it, as EF Core escapes the argument itself.
        ["LikeWithAnEscapedWildcard"] = Hallmarks(
            sql: ["p.ProductName LIKE 'W!_%' ESCAPE '!'"],
            linq: ["p.ProductName.StartsWith(\"W_\")"],
            hql: ["p.ProductName like 'W!_%' escape '!'"],
            jpa: ["p.ProductName like 'W!_%' escape '!'"]),

        ["InListWithABoundValue"] = Hallmarks(
            sql: ["ol.ProductId IN (1, 2, @extra)", "int extra"],
            // Without the `new[]`, for the reason the list row gives.
            linq: ["{ 1, 2, extra }.Contains(ol.ProductId)", "int extra"],
            hql: ["ol.ProductId in (1, 2, :extra)", ".SetParameter(\"extra\", extra)"],
            jpa: ["ol.ProductId in (1, 2, :extra)", ".setParameter(\"extra\", extra)"],
            myBatis: ["ol.ProductId IN (1, 2, #{extra})", "@Param(\"extra\")"]),

        // ---- the expression rows of decision 107 ----
        //
        // The pattern differs by source: the LINQ source escapes the bound value (EF Core's
        // provider does at run time), so the SQL targets get the chain of REPLACE and the
        // LINQ target StartsWith(prefix); the other sources concatenate as written, so the
        // LINQ target gets EF.Functions.Like over the concatenation. The hallmarks name what
        // every source shares.
        ["LikeWithABoundPrefix"] = Hallmarks(
            sql: ["p.ProductName LIKE ", " + '%'", "string prefix"],
            linq: ["p.ProductName", "prefix", "string prefix"],
            hql: ["p.ProductName like concat(", "'%')", ".SetParameter(\"prefix\", prefix)"],
            jpa: ["p.ProductName like concat(", "'%')", ".setParameter(\"prefix\", prefix)"],
            myBatis: ["p.ProductName LIKE ", "#{prefix}", "@Param(\"prefix\") String prefix"]),

        ["ArithmeticInAProjection"] = Hallmarks(
            sql: ["ol.Quantity * ol.UnitPrice AS Total"],
            linq: ["Total = ol.Quantity * ol.UnitPrice"],
            // The HQL target casts the mixed arithmetic to decimal, which NHibernate 5.7.0 would
            // otherwise cut to a whole number (a finding of the fourth level).
            hql: ["cast(ol.Quantity * ol.UnitPrice as decimal) as Total"],
            jpa: ["ol.Quantity * ol.UnitPrice as Total"]),

        ["FunctionInAFilter"] = Hallmarks(
            sql: ["LEN(p.ProductName) = 6"],
            linq: ["p.ProductName.Length == 6"],
            hql: ["length(p.ProductName) = 6"],
            jpa: ["length(p.ProductName) = 6"]),

        ["CoalesceInAFilter"] = Hallmarks(
            sql: ["COALESCE(c.Notes, 'none') = 'none'"],
            linq: ["(c.Notes ?? \"none\") == \"none\""],
            hql: ["coalesce(c.Notes, 'none') = 'none'"],
            jpa: ["coalesce(c.Notes, 'none') = 'none'"]),

        ["CaseInAProjection"] = Hallmarks(
            sql: ["CASE WHEN ol.Quantity > 5 THEN 'bulk' ELSE 'single' END AS Volume"],
            linq: ["Volume = (ol.Quantity > 5 ? \"bulk\" : \"single\")"],
            hql: ["case when ol.Quantity > 5 then 'bulk' else 'single' end as Volume"],
            jpa: ["case when ol.Quantity > 5 then 'bulk' else 'single' end as Volume"]),

        ["OrderingByAnAggregate"] = Hallmarks(
            sql: ["ORDER BY COUNT(*) DESC, ol.ProductId ASC"],
            linq: [".OrderByDescending(g => g.Count())", ".ThenBy(g => g.Key)"],
            hql: ["order by count(*) desc, ol.ProductId asc"],
            jpa: ["order by count(ol) desc, ol.ProductId asc"]),

        // The parameter is a long because the subquery projects a COUNT (decision 083).
        ["ScalarSubqueryAgainstABoundValue"] = Hallmarks(
            sql: ["(SELECT COUNT(*)", ") >= @minLines", "long minLines"],
            linq: [".Count() >= minLines", "long minLines"],
            hql: ["(select count(*)", ") >= :minLines", ".SetParameter(\"minLines\", minLines)"],
            jpa: ["(select count(ol)", ") >= :minLines", ".setParameter(\"minLines\", minLines)"],
            myBatis: ["(SELECT COUNT(*)", ") &gt;= #{minLines}", "@Param(\"minLines\") long minLines"]),

        // ---- the rows of decision 112, a query as a source of rows ----
        //
        // Every target that writes the shape writes it as a named definition before the
        // statement - WITH, HQL's with, a local variable of the LINQ method -, the derived
        // table of the source included; NHibernate and EclipseLink refuse it.
        ["GroupingOverAGroupedResult"] = Hallmarks(
            sql: ["WITH lc AS (", "COUNT(*) AS Lines", "GROUP BY ol.CompanyId, ol.OrderId", "FROM lc AS lc", "WHERE lc.CompanyId > 1", "GROUP BY lc.Lines", "COUNT(*) AS Orders"],
            linq: ["var lc = ", "Lines = g.Count()", "return lc", ".CompanyId > 1", "Orders = g.Count()"],
            jpa: ["with lc as (", "count(ol) as Lines", "from lc lc", "where lc.CompanyId > 1", "group by lc.Lines", "count(*) as Orders"]),

        // The definition is named LineCount by the SQL and HQL rows and lineCount by the LINQ
        // variable, so the hallmarks leave the name out and name what every row shares.
        ["IntermediateResultReadTwice"] = Hallmarks(
            sql: ["WITH ", "COUNT(*) AS Lines", "GROUP BY ol.CompanyId, ol.OrderId", " lc ON ", "lc.Lines >= (SELECT MAX(m.Lines) FROM "],
            linq: ["var ", "Lines = g.Count()", ".Join(", ".lc.Lines >= ", ".Max(m => m.Lines)"],
            jpa: ["with ", "count(ol) as Lines", "join ", " lc on ", "lc.Lines >= (select max(m.Lines) from "]),
    };

    // ---- the deliberately bad query ------------------------------------------------------

    /// <summary>
    /// One very bad query per source, all over the same rows, read from the shared files:
    /// eight nested subqueries four levels deep - IN over IN over IN over a scalar aggregate,
    /// a correlated EXISTS with a scalar subquery correlated two scopes up, a NOT EXISTS of
    /// the same kind, a scalar subquery inside a disjunction -, three joins of which one runs
    /// over two columns and one over three, a DISTINCT and a list of values inside the
    /// nesting, grouping with a HAVING over a parameter, ordering and, where the source
    /// language has a slice, bound pagination. Every target has to carry all of it
    /// (NHibernate's HQL and EclipseLink's JPQL carry no slice, so their rows state none),
    /// every level of verification the .NET suite can run dry is run over it in
    /// <c>Verification/DeeplyNestedQueryVerificationTest</c>, and the Java suite compiles it
    /// and has each Java framework parse it over the same files.
    ///
    /// The LINQ row keeps to what the shared parser reads after a join: keys and filters of
    /// the root, whole rows through the joins, grouping and aggregates over the root's
    /// columns. It is valid C# over a real context, which the SQL-shaped rows have no need
    /// to be. The Dapper row writes its two thresholds and its slice as literals, because a
    /// parameter of a Dapper source has no mapping to take its scalar from without a
    /// catalog (decision 083, §9 of architecture.md); every other row binds them.
    /// </summary>
    public static QueryShape DeeplyNested { get; } = Shape(
        "DeeplyNested",
        new Dictionary<ORMEnum, string[]>
        {
            [ORMEnum.Dapper] = ["DeeplyNested/dapper/FindHeavyLines.sql"],
            [ORMEnum.EFCore] = ["DeeplyNested/efcore/FindHeavyLines.query.cs"],
            [ORMEnum.NHibernate] = ["DeeplyNested/nhibernate/FindHeavyLines.hql"],
            [ORMEnum.Hibernate] = ["DeeplyNested/hibernate/FindHeavyLines.jpql"],
            [ORMEnum.EclipseLink] = ["DeeplyNested/eclipselink/FindHeavyLines.jpql"],
            [ORMEnum.MyBatis] = ["DeeplyNested/mybatis/FindHeavyLinesMapper.query.java", "DeeplyNested/mybatis/FindHeavyLinesMapper.xml"],
        },
        Hallmarks(
            sql:
            [
                $"INNER JOIN {Schema}.", $"LEFT JOIN {Schema}.ShopProducts p",
                ".LineNumber = ", ".OrderId = ",
                "GROUP BY ol.ProductId", "HAVING SUM(ol.Quantity) >",
                "IN (SELECT DISTINCT ", "EXISTS (SELECT", "NOT (EXISTS (SELECT",
                "IN (1, 2, 3)", "(SELECT AVG(p2.UnitPrice)", "(SELECT MIN(ol3.Quantity)", "(SELECT MAX(ol4.UnitPrice)",
                "ol.Description IS NOT NULL OR",
            ],
            linq:
            [
                ".Join(", ".LeftJoin(", "ctx.Set<ShopOrder>()", "ctx.Set<ShopOrderLineAllocation>()", "ctx.Set<ShopProduct>()",
                ".LineNumber }", ".GroupBy(", "g.Sum(", "g.Count()",
                ".Distinct()", ".Any(", "!", "{ 1, 2, 3 }.Contains(", ".Average(", ".Min(", ".Max(",
                "ol.Description != null ||",
            ],
            hql:
            [
                "inner join ShopOrder ", "left join ShopProduct ", " with ", ".LineNumber = ",
                "group by ol.ProductId", "having sum(ol.Quantity) >",
                "in (select distinct ", "exists (", "not (exists (",
                "in (1, 2, 3)", "avg(p2.UnitPrice)", "min(ol3.Quantity)", "max(ol4.UnitPrice)",
                "ol.Description is not null or",
            ],
            jpa:
            [
                "join ShopOrder ", "left join ShopProduct ", " on ", ".LineNumber = ",
                "group by ol.ProductId", "having sum(ol.Quantity) >",
                "in (select distinct ", "exists (", "not (exists (",
                "in (1, 2, 3)", "avg(p2.UnitPrice)", "min(ol3.Quantity)", "max(ol4.UnitPrice)",
                "ol.Description is not null or",
            ]),
        refusedBy: [],
        refusedWithoutCatalog: []);

    /// <summary>How many query scopes the bad query has: the outer one and eight subqueries.</summary>
    public const int DeeplyNestedScopes = 9;

    // ---- shapes: how a row is put together -----------------------------------------------

    /// <summary>
    /// A shape from the shared files: per source, the paths of its query units under
    /// <c>Tests/Database/QueryShapes</c>, each read and sent under the language its name
    /// states, in the order given.
    /// </summary>
    private static QueryShape Shape(
        string name,
        Dictionary<ORMEnum, string[]> units,
        Dictionary<ORMEnum, string[]> hallmarks,
        Dictionary<ORMEnum, QueryFeature> refusedBy,
        Dictionary<ORMEnum, QueryFeature> refusedWithoutCatalog)
    {
        var sources = new Dictionary<ORMEnum, IReadOnlyList<ConversionSource>>();
        foreach (var (source, paths) in units)
        {
            sources[source] = [.. paths.Select(Unit)];
        }

        return new QueryShape(name, sources, hallmarks, refusedBy, refusedWithoutCatalog);
    }

    /// <summary>Hallmarks per target language, spread over the targets that write it.</summary>
    private static Dictionary<ORMEnum, string[]> Hallmarks(
        string[]? sql = null,
        string[]? linq = null,
        string[]? hql = null,
        string[]? jpa = null,
        string[]? myBatis = null)
    {
        var marks = new Dictionary<ORMEnum, string[]>();

        if (sql is not null)
        {
            marks[ORMEnum.Dapper] = sql;

            // The mapper carries the same statement, only with #{} where Dapper writes @ and
            // with the markup characters escaped, because the document is XML.
            marks[ORMEnum.MyBatis] = myBatis ?? sql.Select(mark => ForXml(MyBatisPlaceholders(mark))).ToArray();
        }
        else if (myBatis is not null)
        {
            marks[ORMEnum.MyBatis] = myBatis;
        }

        if (linq is not null)
        {
            marks[ORMEnum.EFCore] = linq;
        }

        if (hql is not null)
        {
            marks[ORMEnum.NHibernate] = hql;
        }

        if (jpa is not null)
        {
            marks[ORMEnum.Hibernate] = jpa;
            marks[ORMEnum.EclipseLink] = jpa;
        }

        return marks;
    }

    private static string MyBatisPlaceholders(string sql) => Regex.Replace(sql, @"@(\w+)", "#{$1}");

    private static string ForXml(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
