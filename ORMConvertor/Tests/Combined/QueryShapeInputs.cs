using System.Text.RegularExpressions;
using AbstractWrappers.Descriptors;
using Model;

namespace Tests.Combined;

/// <summary>
/// One query written in the language of a source framework, per source framework that can
/// write it, together with what its translation has to contain per target and which target
/// refuses it by its descriptor. The unit under test is a category of requirement T2 - or the
/// deliberately bad query of <see cref="QueryShapeInputs.DeeplyNested"/> - and the matrix
/// tests run every shape through every direction the enum yields.
/// </summary>
/// <param name="Name">The category, as the theory data names it.</param>
/// <param name="Sources">The query units per source framework - one, or for MyBatis the mapper and the interface that types its parameters. A source absent here cannot state the shape in its language.</param>
/// <param name="Hallmarks">Substrings every query artifact of the target has to contain, joined over all query artifacts.</param>
/// <param name="RefusedBy">Targets whose descriptor cannot express the shape, with the feature the refusal names.</param>
/// <param name="RefusedFrom">Sources that can write the shape but whose reading the tool refuses by a stated rule, with the feature the refusal names - a Dapper parameter without a catalog to type it from (decision 083).</param>
public sealed record QueryShape(
    string Name,
    IReadOnlyDictionary<ORMEnum, IReadOnlyList<ConversionSource>> Sources,
    IReadOnlyDictionary<ORMEnum, string[]> Hallmarks,
    IReadOnlyDictionary<ORMEnum, QueryFeature> RefusedBy,
    IReadOnlyDictionary<ORMEnum, QueryFeature> RefusedFrom)
{
    public override string ToString() => Name;
}

/// <summary>
/// The inputs of the query-shape matrices: one domain of five entities in the languages of
/// all six frameworks, the query categories of requirement T2 written once per source
/// language over it, and one deeply nested query per source that every target has to carry.
///
/// The domain and the deeply nested query are read from <c>Tests/Database/QueryShapes</c>,
/// the place both suites read from (the mechanism of decision 089): the .NET suite embeds
/// the files, the Java suite takes them as a test resource, and the Java targets are judged
/// over the very same inputs at verification levels 2 and 3. The categories are short and
/// live here.
///
/// The domain is the one of the shared fixture schema (<c>Tests/Database/TestSchema.sql</c>):
/// customers, orders under a two-part key, order lines under a three-part key whose leading
/// parts are a multi-column foreign key, allocations under a four-part key, and products -
/// so the joins here run over two and three columns, which is what makes the join category
/// worth measuring. The order entity is called <c>CustomerOrder</c> because <c>order</c> is
/// a keyword of HQL and JPQL; the two sources that state no table (Dapper, MyBatis) name it
/// <c>CustomerOrders</c> so that the singular-plural rule of decision 050 finds the class.
///
/// Until now every category but aggregation and the parameter was proved on the nine .NET
/// directions only (§9 of architecture.md); the Java sources here - JPQL for both JPA
/// implementations, a mapper document for MyBatis - are what widens that to the matrix.
/// </summary>
public static class QueryShapeInputs
{
    public const string Namespace = "Shop";
    public const string Schema = "Sales";

    private const string ResourcePrefix = "Tests.Database.QueryShapes.";

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
        ORMEnum.Dapper => [Unit(Read("entities/dapper/Shop.cs"), ConversionContentType.CSharpEntity)],
        ORMEnum.EFCore => [Unit(Read("entities/efcore/Shop.cs"), ConversionContentType.CSharpEntity)],
        ORMEnum.NHibernate =>
        [
            Unit(Read("entities/nhibernate/Shop.cs"), ConversionContentType.CSharpEntity),
            Unit(Read("entities/nhibernate/Shop.hbm.xml"), ConversionContentType.XML),
        ],
        ORMEnum.Hibernate or ORMEnum.EclipseLink => JpaEntities.Select(e => Unit(Read($"entities/jpa/{e}.java"), ConversionContentType.JavaEntity)).ToList(),
        ORMEnum.MyBatis =>
        [
            .. JpaEntities.Select(e => Unit(Read($"entities/mybatis/{e}.java"), ConversionContentType.JavaEntity)),
            Unit(Read("entities/mybatis/ShopMapper.xml"), ConversionContentType.XML),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(framework), framework, $"{framework} has no inputs in {nameof(QueryShapeInputs)}; its wrapper brings them."),
    };

    private static readonly string[] JpaEntities = ["Customer", "CustomerOrder", "OrderLine", "OrderLineAllocation", "Product"];

    /// <summary>The table the order entity maps to, as the source names it (see the class remarks).</summary>
    public static string OrdersTable(ORMEnum source) =>
        source is ORMEnum.Dapper or ORMEnum.MyBatis ? "CustomerOrders" : "Orders";

    /// <summary>
    /// Text of one shared file under <c>Tests/Database/QueryShapes</c>, embedded by
    /// Tests.csproj; a new file needs no entry there, but it does need to be in the working
    /// copy. The Java suite reads the same path from its test resources.
    /// </summary>
    public static string Read(string path)
    {
        var resource = ResourcePrefix + path.Replace('/', '.');
        using var stream = typeof(QueryShapeInputs).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The query-shape resource \"{resource}\" is missing under Tests/Database/QueryShapes.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("﻿", string.Empty);
    }

    private static ConversionSource Unit(string content, ConversionContentType type) =>
        new() { Content = content, ContentType = type };

    // ---- the query categories of T2 ------------------------------------------------------

    /// <summary>The categories, one shape each; the theory data of the matrix tests.</summary>
    public static IReadOnlyList<QueryShape> Categories { get; } =
    [
        Define(
            "projection",
            sql: "SELECT ol.Description AS Text, ol.Quantity AS Qty FROM Sales.OrderLines AS ol",
            linq: "ctx.OrderLines.Select(ol => new { Text = ol.Description, Qty = ol.Quantity })",
            hql: "select ol.Description as Text, ol.Quantity as Qty from OrderLine ol",
            jpql: "select ol.Description as Text, ol.Quantity as Qty from OrderLine ol",
            hallmarks: Hallmarks(
                sql: ["ol.Description AS Text", "ol.Quantity AS Qty"],
                linq: ["Text = ol.Description", "Qty = ol.Quantity"],
                hql: ["ol.Description as Text", "ol.Quantity as Qty"],
                jpa: ["ol.Description as Text", "ol.Quantity as Qty"])),

        Define(
            "filtering",
            sql: "SELECT * FROM Sales.OrderLines AS ol WHERE (ol.Quantity > 5 OR ol.UnitPrice >= 100.5) AND ol.Description IS NOT NULL AND NOT (ol.ProductId = 3)",
            linq: "ctx.OrderLines.Where(ol => (ol.Quantity > 5 || ol.UnitPrice >= 100.5m) && ol.Description != null && !(ol.ProductId == 3))",
            hql: "from OrderLine ol where (ol.Quantity > 5 or ol.UnitPrice >= 100.5) and ol.Description is not null and not (ol.ProductId = 3)",
            jpql: "select ol from OrderLine ol where (ol.Quantity > 5 or ol.UnitPrice >= 100.5) and ol.Description is not null and not (ol.ProductId = 3)",
            hallmarks: Hallmarks(
                sql: ["WHERE", "ol.Quantity > 5 OR ol.UnitPrice >= 100.5", "ol.Description IS NOT NULL", "NOT ("],
                linq: [".Where(", "ol.Quantity > 5 || ol.UnitPrice >= 100.5", "ol.Description != null", "!("],
                hql: ["where", "ol.Quantity > 5 or ol.UnitPrice >= 100.5", "ol.Description is not null", "not ("],
                jpa: ["where", "ol.Quantity > 5 or ol.UnitPrice >= 100.5", "ol.Description is not null", "not ("])),

        Define(
            "join over two columns",
            sql: """
                SELECT ol.Description AS Text, o.CustomerId AS CustomerId
                FROM Sales.OrderLines AS ol
                INNER JOIN Sales.CustomerOrders AS o ON o.CompanyId = ol.CompanyId AND o.OrderId = ol.OrderId
                WHERE o.CustomerId > 0
                """,
            // The whole joined row through the result selector, then the filter and the
            // projection over the joined table through the members of that row - the same
            // query as the other rows state, since 2026-09-29. A LINQ join writes its
            // condition outer table first, which is why the hallmarks name no order.
            linq: """
                ctx.OrderLines
                    .Join(ctx.Orders,
                        ol => new { ol.CompanyId, ol.OrderId },
                        o => new { o.CompanyId, o.OrderId },
                        (ol, o) => new { ol, o })
                    .Where(x => x.o.CustomerId > 0)
                    .Select(x => new { Text = x.ol.Description, x.o.CustomerId })
                """,
            hql: """
                select ol.Description as Text, o.CustomerId as CustomerId
                from OrderLine ol
                inner join CustomerOrder o with o.CompanyId = ol.CompanyId and o.OrderId = ol.OrderId
                where o.CustomerId > 0
                """,
            jpql: """
                select ol.Description as Text, o.CustomerId as CustomerId
                from OrderLine ol
                inner join CustomerOrder o on o.CompanyId = ol.CompanyId and o.OrderId = ol.OrderId
                where o.CustomerId > 0
                """,
            hallmarks: Hallmarks(
                // The aliases are the ones every source wrote: a LINQ source names the
                // joined row after the result selector's parameter, o here as well, and the
                // filter and the projection over the joined table reach every target.
                sql: ["INNER JOIN Sales.", ".CompanyId = ", ".OrderId = ", " AND ", "ol.Description AS Text", "WHERE o.CustomerId > 0"],
                linq: [".Join(", "ctx.Set<CustomerOrder>()", "ol.CompanyId", "ol.OrderId", ".o.CustomerId > 0", "Text = ", ".ol.Description"],
                hql: ["inner join CustomerOrder o", " with ", ".CompanyId = ", ".OrderId = ", " and ", "ol.Description as Text", "where o.CustomerId > 0"],
                jpa: ["join CustomerOrder o", " on ", ".CompanyId = ", ".OrderId = ", " and ", "ol.Description as Text", "where o.CustomerId > 0"])),

        Define(
            "aggregation, grouping and having",
            sql: """
                SELECT ol.ProductId AS ProductId, SUM(ol.Quantity) AS Total, COUNT(*) AS Lines
                FROM Sales.OrderLines AS ol
                GROUP BY ol.ProductId
                HAVING SUM(ol.Quantity) > 10
                """,
            linq: """
                ctx.OrderLines
                    .GroupBy(ol => ol.ProductId)
                    .Where(g => g.Sum(x => x.Quantity) > 10)
                    .Select(g => new { ProductId = g.Key, Total = g.Sum(x => x.Quantity), Lines = g.Count() })
                """,
            hql: """
                select ol.ProductId as ProductId, sum(ol.Quantity) as Total, count(*) as Lines
                from OrderLine ol
                group by ol.ProductId
                having sum(ol.Quantity) > 10
                """,
            jpql: """
                select ol.ProductId as ProductId, sum(ol.Quantity) as Total, count(ol) as Lines
                from OrderLine ol
                group by ol.ProductId
                having sum(ol.Quantity) > 10
                """,
            hallmarks: Hallmarks(
                sql: ["GROUP BY ol.ProductId", "HAVING SUM(ol.Quantity) > 10", "COUNT(*)"],
                linq: [".GroupBy(", "g.Sum(", "g.Count()", "g.Key"],
                hql: ["group by ol.ProductId", "having sum(ol.Quantity) > 10", "count(*)"],
                jpa: ["group by ol.ProductId", "having sum(ol.Quantity) > 10", "count(ol)"])),

        Define(
            "ordering",
            sql: "SELECT * FROM Sales.OrderLines AS ol ORDER BY ol.ProductId ASC, ol.Quantity DESC",
            linq: "ctx.OrderLines.OrderBy(ol => ol.ProductId).ThenByDescending(ol => ol.Quantity)",
            hql: "from OrderLine ol order by ol.ProductId asc, ol.Quantity desc",
            jpql: "select ol from OrderLine ol order by ol.ProductId asc, ol.Quantity desc",
            hallmarks: Hallmarks(
                sql: ["ORDER BY ol.ProductId ASC, ol.Quantity DESC"],
                linq: [".OrderBy(ol => ol.ProductId)", ".ThenByDescending(ol => ol.Quantity)"],
                hql: ["order by ol.ProductId asc, ol.Quantity desc"],
                jpa: ["order by ol.ProductId asc, ol.Quantity desc"])),

        // The paginated shape is the parameterized one (decision 085), and only the sources
        // whose query language has a slice can state it: HQL 5.7 and EclipseLink's JPQL put
        // it on the query object, so those two rows have nothing to read.
        Define(
            "pagination with bound counts",
            sql: "SELECT * FROM Sales.OrderLines AS ol ORDER BY ol.LineNumber ASC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY",
            linq: "ctx.OrderLines.OrderBy(ol => ol.LineNumber).Skip(skip).Take(take)",
            hibernateJpql: "select ol from OrderLine ol order by ol.LineNumber asc limit :take offset :skip",
            hallmarks: Hallmarks(
                sql: ["OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY"],
                linq: [".Skip(skip)", ".Take(take)"],
                hql: [".SetFirstResult(skip)", ".SetMaxResults(take)"],
                jpa: [".setFirstResult(skip)", ".setMaxResults(take)"],
                myBatis: ["OFFSET #{skip} ROWS FETCH NEXT #{take} ROWS ONLY"])),

        Define(
            "subquery as the right side of IN",
            sql: "SELECT * FROM Sales.OrderLines AS ol WHERE ol.ProductId IN (SELECT p.ProductId FROM Sales.Products AS p WHERE p.UnitPrice > 100)",
            linq: "ctx.OrderLines.Where(ol => ctx.Products.Where(p => p.UnitPrice > 100).Select(p => p.ProductId).Contains(ol.ProductId))",
            hql: "from OrderLine ol where ol.ProductId in (select p.ProductId from Product p where p.UnitPrice > 100)",
            jpql: "select ol from OrderLine ol where ol.ProductId in (select p.ProductId from Product p where p.UnitPrice > 100)",
            hallmarks: Hallmarks(
                sql: ["IN (SELECT p.ProductId", "p.UnitPrice > 100"],
                linq: ["ctx.Set<Product>()", ".Contains(ol.ProductId)", "p.UnitPrice > 100"],
                hql: ["in (select p.ProductId", "p.UnitPrice > 100"],
                jpa: ["in (select p.ProductId", "p.UnitPrice > 100"])),

        Define(
            "correlated EXISTS over three columns",
            sql: """
                SELECT * FROM Sales.OrderLines AS ol
                WHERE EXISTS (SELECT a.AllocationId FROM Sales.OrderLineAllocations AS a
                              WHERE a.CompanyId = ol.CompanyId AND a.OrderId = ol.OrderId AND a.LineNumber = ol.LineNumber)
                """,
            linq: "ctx.OrderLines.Where(ol => ctx.OrderLineAllocations.Any(a => a.CompanyId == ol.CompanyId && a.OrderId == ol.OrderId && a.LineNumber == ol.LineNumber))",
            hql: "from OrderLine ol where exists (select a.AllocationId from OrderLineAllocation a where a.CompanyId = ol.CompanyId and a.OrderId = ol.OrderId and a.LineNumber = ol.LineNumber)",
            jpql: "select ol from OrderLine ol where exists (select a.AllocationId from OrderLineAllocation a where a.CompanyId = ol.CompanyId and a.OrderId = ol.OrderId and a.LineNumber = ol.LineNumber)",
            hallmarks: Hallmarks(
                sql: ["EXISTS (SELECT", "a.LineNumber = ol.LineNumber"],
                linq: [".Any(", "a.LineNumber == ol.LineNumber"],
                hql: ["exists (", "a.LineNumber = ol.LineNumber"],
                jpa: ["exists (", "a.LineNumber = ol.LineNumber"])),

        Define(
            "scalar subquery",
            sql: "SELECT * FROM Sales.OrderLines AS ol WHERE ol.UnitPrice > (SELECT AVG(p.UnitPrice) FROM Sales.Products AS p WHERE p.UnitPrice > 1)",
            linq: "ctx.OrderLines.Where(ol => ol.UnitPrice > ctx.Products.Where(p => p.UnitPrice > 1).Average(p => p.UnitPrice))",
            hql: "from OrderLine ol where ol.UnitPrice > (select avg(p.UnitPrice) from Product p where p.UnitPrice > 1)",
            jpql: "select ol from OrderLine ol where ol.UnitPrice > (select avg(p.UnitPrice) from Product p where p.UnitPrice > 1)",
            hallmarks: Hallmarks(
                sql: ["(SELECT AVG(p.UnitPrice)"],
                linq: [".Average(p => p.UnitPrice)"],
                hql: ["(select avg(p.UnitPrice)"],
                jpa: ["(select avg(p.UnitPrice)"])),

        // HQL 5.7 has no set operation to read, so NHibernate is no source here - and as a
        // target it refuses the shape by its descriptor, which is the one expected refusal
        // of the matrix.
        Define(
            "set operation",
            sql: """
                SELECT ol.Description AS Text FROM Sales.OrderLines AS ol WHERE ol.Quantity > 5
                UNION
                SELECT p.ProductName AS Text FROM Sales.Products AS p WHERE p.UnitPrice > 100
                """,
            linq: """
                ctx.OrderLines.Where(ol => ol.Quantity > 5).Select(ol => new { Text = ol.Description })
                    .Union(ctx.Products.Where(p => p.UnitPrice > 100).Select(p => new { Text = p.ProductName }))
                """,
            jpql: """
                select ol.Description as Text from OrderLine ol where ol.Quantity > 5
                union
                select p.ProductName as Text from Product p where p.UnitPrice > 100
                """,
            hallmarks: Hallmarks(
                sql: ["UNION", "p.ProductName AS Text"],
                linq: [".Union(", "ctx.Set<Product>()"],
                jpa: ["union", "p.ProductName as Text"]),
            refusedBy: new() { [ORMEnum.NHibernate] = QueryFeature.SetOperation }),

        Define(
            "distinct projection",
            sql: "SELECT DISTINCT ol.ProductId AS ProductId FROM Sales.OrderLines AS ol",
            linq: "ctx.OrderLines.Select(ol => new { ProductId = ol.ProductId }).Distinct()",
            hql: "select distinct ol.ProductId as ProductId from OrderLine ol",
            jpql: "select distinct ol.ProductId as ProductId from OrderLine ol",
            hallmarks: Hallmarks(
                sql: ["SELECT DISTINCT ol.ProductId"],
                linq: [".Distinct()"],
                hql: ["select distinct ol.ProductId"],
                jpa: ["select distinct ol.ProductId"])),

        Define(
            "IN over a list of values",
            sql: "SELECT * FROM Sales.OrderLines AS ol WHERE ol.ProductId IN (1, 2, 3)",
            linq: "ctx.OrderLines.Where(ol => new[] { 1, 2, 3 }.Contains(ol.ProductId))",
            hql: "from OrderLine ol where ol.ProductId in (1, 2, 3)",
            jpql: "select ol from OrderLine ol where ol.ProductId in (1, 2, 3)",
            hallmarks: Hallmarks(
                sql: ["ol.ProductId IN (1, 2, 3)"],
                // Without the `new[]`: against a nullable column the array declares its
                // element type (`new int?[]`), which a MyBatis source's Integer field is.
                linq: ["{ 1, 2, 3 }.Contains(ol.ProductId)"],
                hql: ["ol.ProductId in (1, 2, 3)"],
                jpa: ["ol.ProductId in (1, 2, 3)"])),

        // The scalar of a parameter is derived from the column it is compared with, which
        // takes a mapping (decision 083): a Dapper source states neither table nor column,
        // so without a catalog it is refused by a stated rule (§9 of architecture.md), and
        // the MyBatis row states the type where the framework keeps it - in the signature
        // of the mapper interface (decision 084).
        Define(
            "scalar parameter",
            sql: "SELECT * FROM Sales.OrderLines AS ol WHERE ol.Quantity >= @minQuantity",
            linq: "ctx.OrderLines.Where(ol => ol.Quantity >= minQuantity)",
            hql: "from OrderLine ol where ol.Quantity >= :minQuantity",
            jpql: "select ol from OrderLine ol where ol.Quantity >= :minQuantity",
            myBatisInterface: "    List<OrderLine> findScalarParameter(@Param(\"minQuantity\") int minQuantity);",
            refusedFrom: new() { [ORMEnum.Dapper] = QueryFeature.QueryParameter },
            hallmarks: Hallmarks(
                sql: ["ol.Quantity >= @minQuantity", "int minQuantity"],
                linq: ["ol.Quantity >= minQuantity", "int minQuantity"],
                hql: ["ol.Quantity >= :minQuantity", ".SetParameter(\"minQuantity\", minQuantity)"],
                jpa: ["ol.Quantity >= :minQuantity", ".setParameter(\"minQuantity\", minQuantity)"],
                myBatis: ["ol.Quantity &gt;= #{minQuantity}", "@Param(\"minQuantity\")"])),

        Define(
            "collection parameter",
            sql: "SELECT * FROM Sales.OrderLines AS ol WHERE ol.ProductId IN (@ids)",
            linq: "ctx.OrderLines.Where(ol => ids.Contains(ol.ProductId))",
            hql: "from OrderLine ol where ol.ProductId in (:ids)",
            jpql: "select ol from OrderLine ol where ol.ProductId in :ids",
            myBatis: MyBatisMapper("findCollectionParameter", "OrderLine", """
                SELECT * FROM Sales.OrderLines AS ol
                WHERE ol.ProductId IN
                <foreach collection="ids" item="item" open="(" separator="," close=")">#{item}</foreach>
                """),
            myBatisInterface: "    List<OrderLine> findCollectionParameter(@Param(\"ids\") List<Integer> ids);",
            refusedFrom: new() { [ORMEnum.Dapper] = QueryFeature.QueryParameter },
            hallmarks: Hallmarks(
                sql: ["ol.ProductId IN @ids"],
                // The member's value where the column is nullable (`ol.ProductId.Value`).
                linq: ["ids.Contains(ol.ProductId"],
                hql: ["ol.ProductId in (:ids)", ".SetParameterList(\"ids\", ids)"],
                jpa: ["ol.ProductId in :ids"],
                myBatis: ["<foreach"])),

        // A moment is a constructor in LINQ and a JDBC escape in JPQL; T-SQL and HQL write
        // it as a string, which the readers carry as a string and the builder template
        // types from the column it is compared with (§7 of architecture.md) - so the string
        // sources state the category too, and every target writes the moment with its time
        // of day whichever source left it at the date.
        Define(
            "constant of a moment",
            sql: "SELECT * FROM Sales.CustomerOrders AS o WHERE o.PlacedAt > '2025-01-01'",
            linq: "ctx.Orders.Where(o => o.PlacedAt > new DateTime(2025, 1, 1))",
            hql: "from CustomerOrder o where o.PlacedAt > '2025-01-01'",
            jpql: "select o from CustomerOrder o where o.PlacedAt > {ts '2025-01-01 00:00:00'}",
            resultType: "CustomerOrder",
            hallmarks: Hallmarks(
                sql: ["o.PlacedAt > '2025-01-01 00:00:00'"],
                linq: ["o.PlacedAt > DateTime.Parse(\"2025-01-01 00:00:00\")"],
                hql: ["o.PlacedAt > '2025-01-01 00:00:00'"],
                jpa: ["o.PlacedAt > {ts '2025-01-01 00:00:00'}"],
                myBatis: ["o.PlacedAt &gt; '2025-01-01 00:00:00'"])),

        // The shared LINQ parser reads no string method, so EF Core is no source of a
        // pattern; as a target it gets the translated form of decision 051.
        Define(
            "LIKE with an anchored pattern",
            sql: "SELECT * FROM Sales.Products AS p WHERE p.ProductName LIKE 'W%'",
            hql: "from Product p where p.ProductName like 'W%'",
            jpql: "select p from Product p where p.ProductName like 'W%'",
            resultType: "Product",
            hallmarks: Hallmarks(
                sql: ["p.ProductName LIKE 'W%'"],
                linq: ["p.ProductName.StartsWith(\"W\")"],
                hql: ["p.ProductName like 'W%'"],
                jpa: ["p.ProductName like 'W%'"])),

        // The three constructs decision 102 carries, each added before the matrix is
        // measured over it. The modifier of the aggregate is a one-column Select collapsed
        // before the aggregate in LINQ and the word inside the function everywhere else.
        Define(
            "count over distinct values",
            sql: """
                SELECT ol.ProductId AS ProductId, COUNT(DISTINCT ol.OrderId) AS Orders
                FROM Sales.OrderLines AS ol
                GROUP BY ol.ProductId
                """,
            linq: """
                ctx.OrderLines
                    .GroupBy(ol => ol.ProductId)
                    .Select(g => new { ProductId = g.Key, Orders = g.Select(x => x.OrderId).Distinct().Count() })
                """,
            hql: "select ol.ProductId as ProductId, count(distinct ol.OrderId) as Orders from OrderLine ol group by ol.ProductId",
            jpql: "select ol.ProductId as ProductId, count(distinct ol.OrderId) as Orders from OrderLine ol group by ol.ProductId",
            hallmarks: Hallmarks(
                sql: ["COUNT(DISTINCT ol.OrderId) AS Orders", "GROUP BY ol.ProductId"],
                linq: [".GroupBy(", ".Distinct().Count()", "g.Key"],
                hql: ["count(distinct ol.OrderId) as Orders", "group by ol.ProductId"],
                jpa: ["count(distinct ol.OrderId) as Orders", "group by ol.ProductId"])),

        // An escaped wildcard: the LINQ target splits the pattern past the escape and the
        // core goes out without it, as EF Core escapes the argument itself. No LINQ source,
        // for the same reason as the row above.
        Define(
            "LIKE with an escaped wildcard",
            sql: "SELECT * FROM Sales.Products AS p WHERE p.ProductName LIKE 'W!_%' ESCAPE '!'",
            hql: "from Product p where p.ProductName like 'W!_%' escape '!'",
            jpql: "select p from Product p where p.ProductName like 'W!_%' escape '!'",
            resultType: "Product",
            hallmarks: Hallmarks(
                sql: ["p.ProductName LIKE 'W!_%' ESCAPE '!'"],
                linq: ["p.ProductName.StartsWith(\"W_\")"],
                hql: ["p.ProductName like 'W!_%' escape '!'"],
                jpa: ["p.ProductName like 'W!_%' escape '!'"])),

        // A bound value among the listed ones takes its scalar from the column, so the
        // Dapper row is refused without a catalog as the other parameter rows are.
        Define(
            "in list with a bound value",
            sql: "SELECT * FROM Sales.OrderLines AS ol WHERE ol.ProductId IN (1, 2, @extra)",
            linq: "ctx.OrderLines.Where(ol => new[] { 1, 2, extra }.Contains(ol.ProductId))",
            hql: "from OrderLine ol where ol.ProductId in (1, 2, :extra)",
            jpql: "select ol from OrderLine ol where ol.ProductId in (1, 2, :extra)",
            myBatisInterface: "    List<OrderLine> findInListWithABoundValue(@Param(\"extra\") int extra);",
            refusedFrom: new() { [ORMEnum.Dapper] = QueryFeature.QueryParameter },
            hallmarks: Hallmarks(
                sql: ["ol.ProductId IN (1, 2, @extra)", "int extra"],
                // Without the `new[]`, for the reason the list row gives.
                linq: ["{ 1, 2, extra }.Contains(ol.ProductId)", "int extra"],
                hql: ["ol.ProductId in (1, 2, :extra)", ".SetParameter(\"extra\", extra)"],
                jpa: ["ol.ProductId in (1, 2, :extra)", ".setParameter(\"extra\", extra)"],
                myBatis: ["ol.ProductId IN (1, 2, #{extra})", "@Param(\"extra\")"])),
    ];

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
    public static QueryShape DeeplyNested { get; } = Define(
        "deeply nested",
        sql: Read("DeeplyNested/dapper/FindHeavyLines.sql"),
        linqMethod: Read("DeeplyNested/efcore/FindHeavyLines.query.cs"),
        hql: Read("DeeplyNested/nhibernate/FindHeavyLines.hql"),
        jpql: Read("DeeplyNested/eclipselink/FindHeavyLines.jpql"),
        hibernateJpql: Read("DeeplyNested/hibernate/FindHeavyLines.jpql"),
        myBatis: Read("DeeplyNested/mybatis/FindHeavyLinesMapper.xml"),
        myBatisInterfaceUnit: Read("DeeplyNested/mybatis/FindHeavyLinesMapper.query.java"),
        hallmarks: Hallmarks(
            sql:
            [
                "INNER JOIN Sales.", "LEFT JOIN Sales.Products p",
                ".LineNumber = ", ".OrderId = ",
                "GROUP BY ol.ProductId", "HAVING SUM(ol.Quantity) >",
                "IN (SELECT DISTINCT ", "EXISTS (SELECT", "NOT (EXISTS (SELECT",
                "IN (1, 2, 3)", "(SELECT AVG(p2.UnitPrice)", "(SELECT MIN(ol3.Quantity)", "(SELECT MAX(ol4.UnitPrice)",
                "ol.Description IS NOT NULL OR",
            ],
            linq:
            [
                ".Join(", ".LeftJoin(", "ctx.Set<CustomerOrder>()", "ctx.Set<OrderLineAllocation>()", "ctx.Set<Product>()",
                ".LineNumber }", ".GroupBy(", "g.Sum(", "g.Count()",
                ".Distinct()", ".Any(", "!", "{ 1, 2, 3 }.Contains(", ".Average(", ".Min(", ".Max(",
                "ol.Description != null ||",
            ],
            hql:
            [
                "inner join CustomerOrder ", "left join Product ", " with ", ".LineNumber = ",
                "group by ol.ProductId", "having sum(ol.Quantity) >",
                "in (select distinct ", "exists (", "not (exists (",
                "in (1, 2, 3)", "avg(p2.UnitPrice)", "min(ol3.Quantity)", "max(ol4.UnitPrice)",
                "ol.Description is not null or",
            ],
            jpa:
            [
                "join CustomerOrder ", "left join Product ", " on ", ".LineNumber = ",
                "group by ol.ProductId", "having sum(ol.Quantity) >",
                "in (select distinct ", "exists (", "not (exists (",
                "in (1, 2, 3)", "avg(p2.UnitPrice)", "min(ol3.Quantity)", "max(ol4.UnitPrice)",
                "ol.Description is not null or",
            ]));

    /// <summary>How many query scopes the bad query has: the outer one and eight subqueries.</summary>
    public const int DeeplyNestedScopes = 9;

    // ---- shapes: how a row is put together -----------------------------------------------

    /// <summary>
    /// A shape from the texts a source can state. <paramref name="sql"/> feeds Dapper as a
    /// bare SqlQuery unit and MyBatis as a mapper document (the parameter spelling switched
    /// from @ to #{}) unless <paramref name="myBatis"/> gives the document itself; a MyBatis
    /// row with parameters adds the mapper interface whose method signature types them -
    /// the declarations (<paramref name="myBatisInterface"/>) or the whole unit
    /// (<paramref name="myBatisInterfaceUnit"/>). <paramref name="linq"/> is a chain the
    /// method is put around, <paramref name="linqMethod"/> the whole method. <paramref name="jpql"/>
    /// feeds both JPA implementations, and <paramref name="hibernateJpql"/> only Hibernate,
    /// for a text that uses the HQL dialect clause.
    /// </summary>
    private static QueryShape Define(
        string name,
        string? sql = null,
        string? linq = null,
        string? linqMethod = null,
        string? hql = null,
        string? jpql = null,
        string? hibernateJpql = null,
        string? myBatis = null,
        string? myBatisInterface = null,
        string? myBatisInterfaceUnit = null,
        string resultType = "OrderLine",
        Dictionary<ORMEnum, string[]>? hallmarks = null,
        Dictionary<ORMEnum, QueryFeature>? refusedBy = null,
        Dictionary<ORMEnum, QueryFeature>? refusedFrom = null)
    {
        var sources = new Dictionary<ORMEnum, IReadOnlyList<ConversionSource>>();

        if (sql is not null)
        {
            sources[ORMEnum.Dapper] = [Unit(sql, ConversionContentType.SqlQuery)];
        }

        var id = MethodNameOf(name);
        var mapper = myBatis ?? (sql is null ? null : MyBatisMapper(id, resultType, ForXml(MyBatisPlaceholders(sql))));
        if (mapper is not null)
        {
            var interfaceUnit = myBatisInterfaceUnit
                ?? (myBatisInterface is null ? null : MyBatisInterface(MapperNamespaceOf(mapper), myBatisInterface));

            sources[ORMEnum.MyBatis] = interfaceUnit is null
                ? [Unit(mapper, ConversionContentType.XML)]
                : [Unit(interfaceUnit, ConversionContentType.JavaQuery), Unit(mapper, ConversionContentType.XML)];
        }

        if (linqMethod is not null || linq is not null)
        {
            sources[ORMEnum.EFCore] = [Unit(linqMethod ?? LinqMethod(linq!), ConversionContentType.CSharpQuery)];
        }

        if (hql is not null)
        {
            sources[ORMEnum.NHibernate] = [Unit(hql, ConversionContentType.HqlQuery)];
        }

        if (hibernateJpql is not null || jpql is not null)
        {
            sources[ORMEnum.Hibernate] = [Unit(hibernateJpql ?? jpql!, ConversionContentType.JpqlQuery)];
        }

        if (jpql is not null)
        {
            sources[ORMEnum.EclipseLink] = [Unit(jpql, ConversionContentType.JpqlQuery)];
        }

        return new QueryShape(name, sources, hallmarks ?? [], refusedBy ?? [], refusedFrom ?? []);
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

    private static string LinqMethod(string chain) => $$"""
        public void Query()
        {
            var q = {{chain.Trim()}}
                .ToList();
        }
        """;

    private static string MyBatisPlaceholders(string sql) => Regex.Replace(sql, @"@(\w+)", "#{$1}");

    private static string ForXml(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string MethodNameOf(string name) =>
        "find" + string.Concat(Regex.Replace(name, @"[^A-Za-z0-9 ]", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    /// <summary>The mapper interface a statement belongs to: the statement's name in the case of a Java type.</summary>
    private static string MapperNameOf(string id) => char.ToUpperInvariant(id[0]) + id[1..] + "Mapper";

    private static string MapperNamespaceOf(string mapper)
        => Regex.Match(mapper, "namespace=\"([^\"]+)\"").Groups[1].Value;

    /// <summary>A mapper document with one statement; the mapping of the domain lives in the shared ShopMapper.xml.</summary>
    private static string MyBatisMapper(string id, string resultType, string statement) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE mapper PUBLIC "-//mybatis.org//DTD Mapper 3.0//EN"
                "https://mybatis.org/dtd/mybatis-3-mapper.dtd">
        <mapper namespace="Shop.{{MapperNameOf(id)}}">
          <select id="{{id}}" resultType="{{resultType}}">
            {{statement.Trim()}}
          </select>
        </mapper>
        """;

    /// <summary>
    /// The mapper interface beside a mapper document, named by the document's namespace: the
    /// one place MyBatis keeps the type of a parameter (decision 084), so the row states it
    /// there and nowhere else.
    /// </summary>
    private static string MyBatisInterface(string mapperNamespace, string methods)
    {
        var name = mapperNamespace[(mapperNamespace.LastIndexOf('.') + 1)..];

        return $$"""
            package Shop;

            import java.util.List;
            import org.apache.ibatis.annotations.Param;

            public interface {{name}} {
            {{methods.TrimEnd()}}
            }
            """;
    }
}
