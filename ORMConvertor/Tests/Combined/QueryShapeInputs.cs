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
/// <param name="RefusedBy">Targets that refuse the shape, with the feature the refusal names.</param>
/// <param name="RefusedWithoutCatalog">Sources that can write the shape but whose reading the tool refuses by a stated rule in a run without a catalog, with the feature the refusal names - a Dapper parameter, whose scalar takes a mapping the source does not state (decision 083) and the catalog supplies on the query's own demand (decision 105). The matrices here convert dry, so for them it is a refusal; a run with a catalog holding the domain yields the artifact.</param>
/// <param name="FallbackBy">Targets whose query language does not speak the shape and which write it in the native SQL of their dialect instead, with the feature the record of kind Fallback names (decision 113).</param>
/// <param name="FallbackWithoutCatalog">Directions that write the shape in native SQL in a run without a catalog only, with the feature the record names - the target needs a fact of the mapping the source does not state and the catalog supplies, such as a column holding no NULL, which a MyBatis source leaves open and EF Core's list needs (decision 113). The matrices here convert dry, so for them it is a fallback; a run with a catalog holding the domain translates the direction.</param>
public sealed record QueryShape(
    string Name,
    IReadOnlyDictionary<ORMEnum, IReadOnlyList<ConversionSource>> Sources,
    IReadOnlyDictionary<ORMEnum, string[]> Hallmarks,
    IReadOnlyDictionary<ORMEnum, QueryFeature> RefusedBy,
    IReadOnlyDictionary<ORMEnum, QueryFeature> RefusedWithoutCatalog,
    IReadOnlyDictionary<ORMEnum, QueryFeature> FallbackBy,
    IReadOnlyDictionary<(ORMEnum Source, ORMEnum Target), QueryFeature> FallbackWithoutCatalog)
{
    /// <summary>The feature a direction of a dry run writes in native SQL for, or null where it translates the shape.</summary>
    public QueryFeature? FallsBack(ORMEnum source, ORMEnum target)
        => FallbackBy.TryGetValue(target, out var feature) ? feature
            : FallbackWithoutCatalog.TryGetValue((source, target), out var dry) ? dry
            : null;

    /// <summary>
    /// What the direction's text has to carry: the target's hallmarks, or the SQL ones where
    /// the direction falls back, because the escape path writes the text the Dapper target
    /// writes (decision 113).
    /// </summary>
    public string[] HallmarksOf(ORMEnum source, ORMEnum target)
        => FallbackWithoutCatalog.ContainsKey((source, target))
            ? Hallmarks.GetValueOrDefault(ORMEnum.Dapper, [])
            : Hallmarks.GetValueOrDefault(target, []);

    public override string ToString() => Name;
}

/// <summary>
/// The inputs of the query-shape matrices: one domain of seven entities in the languages of
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
/// read the same input. The seven entities carry the prefix <c>Shop</c> - <c>ShopOrder</c>,
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

    /// <summary>What the framework reads the seven entities from, as the shared files state them.</summary>
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

    private static readonly string[] JpaEntities = ["ShopCustomer", "ShopOrder", "ShopOrderLine", "ShopOrderLineAllocation", "ShopProduct", "ShopDepartment", "ShopProductLink"];

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
            var fallbackBy = new Dictionary<ORMEnum, QueryFeature>();
            var fallbackWithoutCatalog = new Dictionary<(ORMEnum, ORMEnum), QueryFeature>();

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
                    case "fallbackBy":
                        fallbackBy = Refusals(id, value);
                        break;
                    case "fallbackWithoutCatalog":
                        fallbackWithoutCatalog = Directions(id, value);
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

            shapes.Add(Shape(id, units, hallmarks, refusedBy, refusedWithoutCatalog, fallbackBy, fallbackWithoutCatalog));
        }

        var unstated = CategoryHallmarks.Keys.Except(shapes.Select(shape => shape.Name)).ToList();
        if (unstated.Count > 0)
        {
            throw new InvalidOperationException(
                $"{nameof(QueryShapeInputs)} has hallmarks for {string.Join(", ", unstated)}, which categories.txt does not state; the two are one list.");
        }

        return shapes;
    }

    /// <summary>A list of directions of the manifest: <c>Source&gt;Target:Feature</c> entries, all three spelled as the enums spell them.</summary>
    private static Dictionary<(ORMEnum, ORMEnum), QueryFeature> Directions(string id, string value)
    {
        var directions = new Dictionary<(ORMEnum, ORMEnum), QueryFeature>();

        foreach (var entry in SharedInputs.List(value))
        {
            var arrow = entry.IndexOf('>');
            var colon = entry.IndexOf(':');
            if (arrow < 0 || colon < arrow
                || !Enum.TryParse<ORMEnum>(entry[..arrow], ignoreCase: false, out var source) || !Enum.IsDefined(source)
                || !Enum.TryParse<ORMEnum>(entry[(arrow + 1)..colon], ignoreCase: false, out var target) || !Enum.IsDefined(target)
                || !Enum.TryParse<QueryFeature>(entry[(colon + 1)..], ignoreCase: false, out var feature) || !Enum.IsDefined(feature))
            {
                throw new InvalidOperationException($"categories.txt: [{id}] states the direction \"{entry}\", which is not written as Source>Target:Feature.");
            }

            directions[(source, target)] = feature;
        }

        return directions;
    }

    /// <summary>A refusal or fallback list of the manifest: <c>Framework:Feature</c> entries, both spelled as the enums spell them.</summary>
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

        // The inner join inside the subquery (decision 113): every target keeps it a join
        // except EclipseLink, whose 5.0.0 writes an entity join of a subquery into no SQL
        // (JpaImplementationProfile.DropsJoinsInSubqueries) and therefore gets JPQL's
        // original spelling - a further range variable, the join condition first in the
        // where clause. The condition names no order, as the join category explains.
        ["SubqueryWithAJoinAsTheRightSideOfIn"] = Hallmarks(
            sql: ["IN (SELECT ol2.ProductId", $"INNER JOIN {Schema}.ShopOrders o ON", ".CompanyId = ", ".OrderId = ", "o.CustomerId > 1"],
            linq: [".Join(", "ctx.Set<ShopOrder>()", "ol2.CompanyId", "ol2.OrderId", ".o.CustomerId > 1", ".ol2.ProductId", ".Contains(ol.ProductId"],
            hql: ["in (select ol2.ProductId", "inner join ShopOrder o", " with ", ".CompanyId = ", ".OrderId = ", "o.CustomerId > 1"],
            jpa: ["in (select ol2.ProductId", "join ShopOrder o", " on ", ".CompanyId = ", ".OrderId = ", "o.CustomerId > 1"],
            eclipseLink: ["in (select ol2.ProductId", " from ShopOrderLine ol2, ShopOrder o where (", ".CompanyId = ", ".OrderId = ", ") and o.CustomerId > 1)"]),

        ["CorrelatedExistsWithAJoin"] = Hallmarks(
            sql: ["EXISTS (SELECT", $"INNER JOIN {Schema}.ShopOrders o ON", "a.LineNumber = ol.LineNumber", "o.CustomerId > 1"],
            linq: [".Join(", "ctx.Set<ShopOrder>()", ".Any(", "a.LineNumber == ol.LineNumber", ".o.CustomerId > 1"],
            hql: ["exists (", "inner join ShopOrder o", " with ", "a.LineNumber = ol.LineNumber", "o.CustomerId > 1"],
            jpa: ["exists (", "join ShopOrder o", " on ", "a.LineNumber = ol.LineNumber", "o.CustomerId > 1"],
            eclipseLink: ["exists (select ", " from ShopOrderLineAllocation a, ShopOrder o where (", ") and a.CompanyId = ol.CompanyId", "a.LineNumber = ol.LineNumber", "o.CustomerId > 1)"]),

        // The quantified comparison (decision 119): SQL, HQL and JPQL keep the keyword, LINQ
        // moves the comparison into the lambda of All() or Any() over the projected values. Both
        // categories run over the one nullable column of the domain, so that the fourth level
        // measures the NULL case that separates SQL's ALL from C#'s All().
        ["QuantifiedComparisonOverAll"] = Hallmarks(
            sql: ["d.DepartmentId < ALL (SELECT c.ParentDepartmentId"],
            linq: [".Select(c => c.ParentDepartmentId).All(v => d.DepartmentId < v)"],
            hql: ["d.DepartmentId < all (select c.ParentDepartmentId"],
            jpa: ["d.DepartmentId < all (select c.ParentDepartmentId"]),

        ["QuantifiedComparisonOverAny"] = Hallmarks(
            sql: ["d.ParentDepartmentId < ANY (SELECT c.DepartmentId"],
            linq: [".Select(c => c.DepartmentId).Any(v => d.ParentDepartmentId < v)"],
            hql: ["d.ParentDepartmentId < any (select c.DepartmentId"],
            jpa: ["d.ParentDepartmentId < any (select c.DepartmentId"]),

        // A set operation as the body of a subquery operand (decision 120): SQL and JPQL write
        // the operation inside the parentheses of IN, LINQ composes the two chains and ranges
        // over the result; HQL has none and NHibernate falls back.
        ["InOverASetOperation"] = Hallmarks(
            sql: ["p.ProductId IN (SELECT ol.ProductId", "UNION SELECT l.ToProductId"],
            linq: [".Select(ol => ol.ProductId).Union(", ".Contains(p.ProductId"],
            jpa: ["p.ProductId in (select ol.ProductId", "union select l.ToProductId"]),

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

        // The LINQ targets order over the group before the projection, the alias resolved to
        // the count it names, so that the key the projection does not carry survives the
        // slice. The LINQ source states the count itself where the SQL and JPQL ones name
        // the alias, and the text targets write the first key as the source did, so the
        // hallmarks name the second key alone.
        ["OrderingByAnUnprojectedKeyUnderASlice"] = Hallmarks(
            sql: ["SELECT TOP (3)", "GROUP BY p.ProductId, p.ProductName", " DESC, p.ProductId ASC"],
            linq: [".OrderByDescending(g => g.Count())", ".ThenBy(g => g.Key.ProductId)", ".Select(g => new { ProductName = g.Key.ProductName, Lines = g.Count() })", ".Take(3)"],
            hql: ["group by p.ProductId, p.ProductName", " desc, p.ProductId asc", ".SetMaxResults(3)"],
            jpa: ["group by p.ProductId, p.ProductName", " desc, p.ProductId asc", ".setMaxResults(3)"]),

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
        // table of the source included; NHibernate and EclipseLink write it in native SQL,
        // and so carry the SQL hallmarks (decision 113).
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

        // ---- the rows of decision 113, native SQL ----
        //
        // EF Core writes the first in native SQL and so carries the SQL hallmarks; the second
        // is written by every target in its own language, and its parameter is named by the
        // source - minPrice, or p1 from a JPA source, which binds by position - so the
        // hallmarks leave the name out.
        ["AggregateOverTheWholeResult"] = Hallmarks(
            sql: ["COUNT(*) AS Lines", "SUM(ol.Quantity) AS Quantity", "WHERE ol.Quantity > 5"],
            hql: ["count(*) as Lines", "sum(ol.Quantity) as Quantity", "where ol.Quantity > 5"],
            jpa: ["count(ol) as Lines", "sum(ol.Quantity) as Quantity", "where ol.Quantity > 5"]),

        ["NativeSqlInCode"] = Hallmarks(
            sql: ["p.ProductName AS ProductName", "WHERE p.UnitPrice > @", "ORDER BY p.UnitPrice DESC, p.ProductId ASC"],
            linq: ["ctx.Set<ShopProduct>()", ".Where(p => p.UnitPrice > ", ".OrderByDescending(p => p.UnitPrice)", ".ThenBy(p => p.ProductId)"],
            hql: ["p.ProductName as ProductName", "where p.UnitPrice > :", "order by p.UnitPrice desc, p.ProductId asc"],
            jpa: ["p.ProductName as ProductName", "where p.UnitPrice > ", "order by p.UnitPrice desc, p.ProductId asc"],
            myBatis: ["WHERE p.UnitPrice &gt; #{", "ORDER BY p.UnitPrice DESC, p.ProductId ASC"]),

        // ---- the rows of decision 113, recursion ----
        //
        // The recursive member comes out named by position after the anchor, so the counter
        // the source left without an alias carries the anchor's name in every target. EF Core,
        // NHibernate and EclipseLink write both rows in native SQL, Hibernate the second, so
        // they carry the SQL hallmarks; the limit of recursion closes the statement.
        ["RecursiveDescentOfAHierarchy"] = Hallmarks(
            sql: ["WITH DepartmentTree AS (", "0 AS Depth", "UNION ALL", "t.Depth + 1 AS Depth", "INNER JOIN DepartmentTree t ON c.ParentDepartmentId = t.DepartmentId", "WHERE t.Depth >= 2"],
            jpa: ["with DepartmentTree as (", "0 as Depth", "union all", "t.Depth + 1 as Depth", "join DepartmentTree t on c.ParentDepartmentId = t.DepartmentId", "where t.Depth >= 2"]),

        // The walk guards its cycle over the path walked so far, the keys converted to text
        // (decision 113); NOT LIKE is written as the negation of the LIKE it is.
        ["RecursiveWalkOfACyclicGraph"] = Hallmarks(
            sql: ["WITH Walk AS (", "',1,' + CAST(l.ToProductId AS NVARCHAR(MAX)) + ',' AS Path", "UNION ALL", "w.Path + CAST(n.ToProductId AS NVARCHAR(MAX)) + ',' AS Path", "NOT (w.Path LIKE '%,' + CAST(n.ToProductId AS NVARCHAR(MAX)) + ',%')", "SELECT DISTINCT w.ProductId AS ProductId", "OPTION (MAXRECURSION 10)"]),

        // ---- the rows of decision 113, the vocabulary of expressions ----
        //
        // A target that falls back carries the SQL hallmarks. The LINQ target names the
        // expression of a key of several parts after the projection that projects it; a
        // MyBatis source declares its moments, numbers and keys as Java wrappers, which the
        // LINQ target reaches through .Value and casts into a nullable type, so the LINQ
        // hallmarks leave both open. The CASE key carries literals, which EclipseLink binds as
        // parameters: its JPQL is the same as Hibernate's and the method sets the hint that
        // has the query write them inline (decision 113).
        ["GroupingByAnExpression"] = Hallmarks(
            sql: ["YEAR(o.PlacedAt) AS PlacedYear", "CASE WHEN o.CustomerId = 1 THEN 1 ELSE 0 END AS KeyAccount", "COUNT(*) AS OrderCount", "WHERE o.CustomerId > 0", "GROUP BY YEAR(o.PlacedAt), o.CompanyId, CASE WHEN o.CustomerId = 1 THEN 1 ELSE 0 END"],
            linq: [".GroupBy(o => new { PlacedYear = o.PlacedAt.", "Year, o.CompanyId, KeyAccount = (", " ? 1 : 0) })", "PlacedYear = g.Key.PlacedYear", "CompanyId = g.Key.CompanyId", "KeyAccount = g.Key.KeyAccount", "OrderCount = g.Count()"],
            hql: ["year(o.PlacedAt) as PlacedYear", "case when o.CustomerId = 1 then 1 else 0 end as KeyAccount", "where o.CustomerId > 0", "group by year(o.PlacedAt), o.CompanyId, case when o.CustomerId = 1 then 1 else 0 end"],
            jpa: ["extract(year from o.PlacedAt) as PlacedYear", "case when o.CustomerId = 1 then 1 else 0 end as KeyAccount", "where o.CustomerId > 0", "group by extract(year from o.PlacedAt), o.CompanyId, case when o.CustomerId = 1 then 1 else 0 end"],
            eclipseLink: ["extract(year from o.PlacedAt) as PlacedYear", "case when o.CustomerId = 1 then 1 else 0 end as KeyAccount", "where o.CustomerId > 0", "group by extract(year from o.PlacedAt), o.CompanyId, case when o.CustomerId = 1 then 1 else 0 end", ".setHint(QueryHints.BIND_PARAMETERS, HintValues.FALSE)"]),

        ["DateArithmetic"] = Hallmarks(
            sql: ["DATEADD(day, 30, o.PlacedAt) AS DueAt", "DATEDIFF(hour, '2025-01-01 00:00:00', o.PlacedAt) AS HoursFromNewYear", "WHERE DATEDIFF(day, o.PlacedAt, '2025-03-01 00:00:00') > 0"],
            linq: ["DueAt = o.PlacedAt.", "AddDays(30)", "HoursFromNewYear = EF.Functions.DateDiffHour(DateTime.Parse(\"2025-01-01 00:00:00\"), o.PlacedAt)", ".Where(o => EF.Functions.DateDiffDay(o.PlacedAt, DateTime.Parse(\"2025-03-01 00:00:00\")) > 0)"],
            jpa: ["timestampadd(day, 30, o.PlacedAt) as DueAt", "timestampdiff(hour, {ts '2025-01-01 00:00:00'}, o.PlacedAt) as HoursFromNewYear", "where timestampdiff(day, o.PlacedAt, {ts '2025-03-01 00:00:00'}) > 0"]),

        ["RoundingAndSquareRoot"] = Hallmarks(
            sql: ["ROUND(p.UnitPrice, 0) AS RoundedPrice", "SQRT(", " AS PriceRoot", "WHERE ROUND(p.UnitPrice, 0) > 50"],
            linq: ["RoundedPrice = Math.Round(p.UnitPrice", ", 0)", "PriceRoot = Math.Sqrt(", ".Where(p => Math.Round(p.UnitPrice", ", 0) > 50)"],
            hql: ["round(p.UnitPrice, 0) as RoundedPrice", "sqrt(", " as PriceRoot", "where round(p.UnitPrice, 0) > 50"],
            jpa: ["round(p.UnitPrice, 0) as RoundedPrice", "sqrt(", " as PriceRoot", "where round(p.UnitPrice, 0) > 50"]),

        ["CastInAConcatenation"] = Hallmarks(
            sql: ["'#' + CAST(p.ProductId AS NVARCHAR(MAX)) + ' ' + p.ProductName AS Label", "WHERE CAST(p.UnitPrice AS INT) > 100"],
            linq: ["Label = \"#\" + p.ProductId.", "ToString() + \" \" + p.ProductName", ".Where(p => (int", "(p.UnitPrice) > 100)"],
            hql: ["concat('#', cast(p.ProductId as string), ' ', p.ProductName) as Label", "where cast(p.UnitPrice as int) > 100"],
            jpa: ["concat('#', cast(p.ProductId as String), ' ', p.ProductName) as Label", "where cast(p.UnitPrice as Integer) > 100"]),

        ["BestRowPerGroup"] = Hallmarks(
            sql: ["WITH Ranked AS (", "ROW_NUMBER() OVER (PARTITION BY ol.ProductId ORDER BY ol.Quantity DESC, ol.LineNumber ASC) AS RowNumber", "WHERE r.RowNumber = 1"],
            jpa: ["with Ranked as (", "row_number() over (partition by ol.ProductId order by ol.Quantity desc, ol.LineNumber asc) as RowNumber", "where r.RowNumber = 1"]),

        ["ListAggregation"] = Hallmarks(
            sql: ["STRING_AGG(ol.Description, '; ') WITHIN GROUP (ORDER BY ol.LineNumber ASC) AS Descriptions", "WHERE ol.Quantity > 1", "GROUP BY ol.ProductId"],
            linq: ["Descriptions = string.Join(\"; \", g.OrderBy(ol => ol.LineNumber).Select(ol => ol.Description))", ".Where(ol => ol.Quantity > 1)"],
            jpa: ["listagg(ol.Description, '; ') within group (order by ol.LineNumber asc) as Descriptions", "where ol.Quantity > 1", "group by ol.ProductId"]),

        // ---- the rows of decision 113, joins in LINQ ----
        //
        // A source that states the join by its keys reads the equality with the row of the
        // chain first, so the SQL hallmarks leave the order of its sides open. LINQ filters
        // the joined set by the conjunct over it alone and writes a condition over both rows
        // as the correlated SelectMany, a left one over DefaultIfEmpty().
        ["OuterJoinWithAFilterInOn"] = Hallmarks(
            sql: [$"LEFT JOIN {Schema}.ShopOrderLines ol ON ", ".ProductId = ", " AND ol.Quantity > 5", "p.ProductName AS ProductName", "ol.Description AS Description"],
            linq: [".LeftJoin(ctx.Set<ShopOrderLine>().Where(ol => ol.Quantity > 5), p => p.ProductId, ol => ol.ProductId, (p, ol) => new { p, ol })", "ProductName = t.p.ProductName", "Description = t.ol.Description"],
            hql: ["left join ShopOrderLine ol with ", ".ProductId = ", " and ol.Quantity > 5", "p.ProductName as ProductName"],
            jpa: ["left join ShopOrderLine ol on ", ".ProductId = ", " and ol.Quantity > 5", "p.ProductName as ProductName"]),

        ["JoinBeyondEqualities"] = Hallmarks(
            sql: [$"LEFT JOIN {Schema}.ShopOrderLines ol ON ol.ProductId = p.ProductId AND ol.UnitPrice < p.UnitPrice", "p.ProductName AS ProductName"],
            linq: [".SelectMany(p => ctx.Set<ShopOrderLine>().Where(ol => ol.ProductId == p.ProductId && ol.UnitPrice < p.UnitPrice).DefaultIfEmpty(), (p, ol) => new { p, ol })", "ProductName = t.p.ProductName", "Description = t.ol.Description"],
            hql: ["left join ShopOrderLine ol with ol.ProductId = p.ProductId and ol.UnitPrice < p.UnitPrice", "p.ProductName as ProductName"],
            jpa: ["left join ShopOrderLine ol on (ol.ProductId = p.ProductId and ol.UnitPrice < p.UnitPrice)", "p.ProductName as ProductName"]),
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
        refusedWithoutCatalog: [],
        fallbackBy: [],
        fallbackWithoutCatalog: []);

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
        Dictionary<ORMEnum, QueryFeature> refusedWithoutCatalog,
        Dictionary<ORMEnum, QueryFeature> fallbackBy,
        Dictionary<(ORMEnum, ORMEnum), QueryFeature> fallbackWithoutCatalog)
    {
        var sources = new Dictionary<ORMEnum, IReadOnlyList<ConversionSource>>();
        foreach (var (source, paths) in units)
        {
            sources[source] = [.. paths.Select(Unit)];
        }

        // A target that falls back writes the text the Dapper target writes (decision 113),
        // so its hallmarks are the SQL ones; where the source states none, it has none.
        var marks = new Dictionary<ORMEnum, string[]>(hallmarks);
        foreach (var target in fallbackBy.Keys)
        {
            marks[target] = hallmarks.GetValueOrDefault(ORMEnum.Dapper, []);
        }

        return new QueryShape(name, sources, marks, refusedBy, refusedWithoutCatalog, fallbackBy, fallbackWithoutCatalog);
    }

    /// <summary>
    /// Hallmarks per target language, spread over the targets that write it. The two JPA
    /// targets share the JPQL marks except where the implementation profile makes EclipseLink
    /// spell a construct otherwise (<c>eclipseLink</c>), as it does an inner join inside a
    /// subquery.
    /// </summary>
    private static Dictionary<ORMEnum, string[]> Hallmarks(
        string[]? sql = null,
        string[]? linq = null,
        string[]? hql = null,
        string[]? jpa = null,
        string[]? myBatis = null,
        string[]? eclipseLink = null)
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
            marks[ORMEnum.EclipseLink] = eclipseLink ?? jpa;
        }

        return marks;
    }

    private static string MyBatisPlaceholders(string sql) => Regex.Replace(sql, @"@(\w+)", "#{$1}");

    private static string ForXml(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
