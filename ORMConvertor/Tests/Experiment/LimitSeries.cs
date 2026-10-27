using System.Globalization;
using System.Text;
using Model;
using Tests.Combined;
using Tests.Foreign;

namespace Tests.Experiment;

/// <summary>
/// One axis of the search for the tool's limits (decision 182): what grows, the points it
/// grows through, the sources that state it, and how the units of one point are made.
/// </summary>
/// <param name="Id">The name of the axis; the key of a point is <c>series:&lt;Id&gt;-&lt;point&gt;</c>.</param>
/// <param name="Grows">What the point counts, for the summary.</param>
/// <param name="Points">The points of the series in the order a walk takes them; a geometric sequence up to the ceiling of the axis.</param>
/// <param name="Sources">The source frameworks that state the series in their language.</param>
/// <param name="Uncapped">Whether the series is read with the depth cap switched off - the shapes the cap defends against, nesting in the text (decision 092) and chains the model holds as deep as they are long (decision 191) -, so that the walk finds what lies behind the cap and not the cap itself.</param>
/// <param name="OverTheFixture">Whether a point carries one query over the domain of the query-shape matrices, which runs over the fixture at the fourth level; a series of entities, or of a project over no table of the fixture, ends at the third.</param>
/// <param name="Units">The units of one point for one source, as the tool receives them.</param>
/// <param name="Beyond">Points past the ceiling that the walk takes with the translation alone (decision 190): where the ceiling was set by the cost of the path rather than by the order anyone writes, the translator's own limit lies further.</param>
internal sealed record LimitAxis(
    string Id,
    string Grows,
    IReadOnlyList<int> Points,
    IReadOnlyList<ORMEnum> Sources,
    bool Uncapped,
    bool OverTheFixture,
    Func<ORMEnum, int, List<ConversionSource>> Units,
    IReadOnlyList<int>? Beyond = null)
{
    public ForeignKey Key(int point) => new(ForeignKey.Series, $"{Id}-{point.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>Every point a walk takes: the series up to its ceiling, then the points past it.</summary>
    public IEnumerable<int> Walked => Beyond is null ? Points : Points.Concat(Beyond);

    /// <summary>Whether a point lies past the ceiling, where the path does not judge and nothing is exported.</summary>
    public bool IsBeyond(int point) => Beyond is not null && Beyond.Contains(point) && !Points.Contains(point);

    public override string ToString() => Id;
}

/// <summary>
/// The series of the search for limits (decision 182): synthetic inputs over the domain of
/// the query-shape matrices (<see cref="QueryShapeInputs"/>) that grow along one axis at a
/// time - one more join, one more level of nesting, one more property - in geometric steps,
/// each written in the language of every source that can state it. The smallest point of
/// every series translates in every direction, which <c>LimitSeriesTest</c> holds; where a
/// larger point stops is what the walk of <see cref="LimitSearch"/> measures, never a test.
///
/// The query series project <c>ProductName</c> from the product table and filter on columns
/// the fixture's data keeps, so that every point returns rows at the fourth level. The
/// entity series state their key themselves, because no table of any database of the tool
/// holds a synthetic class and a source that states no key would be refused for the catalog's
/// silence, not for its size.
/// </summary>
internal static class LimitSeries
{
    private static readonly ORMEnum[] QuerySources = [ORMEnum.Dapper, ORMEnum.EFCore, ORMEnum.NHibernate, ORMEnum.Hibernate, ORMEnum.EclipseLink, ORMEnum.MyBatis];

    /// <summary>The sources whose query language has no set operation - HQL - stay out of the union series, as they stay out of the category (categories.txt).</summary>
    private static readonly ORMEnum[] UnionSources = [ORMEnum.Dapper, ORMEnum.EFCore, ORMEnum.Hibernate, ORMEnum.EclipseLink, ORMEnum.MyBatis];

    /// <summary>The sources that state an intermediate result (categories.txt): HQL and EclipseLink's JPQL have none.</summary>
    private static readonly ORMEnum[] IntermediateResultSources = [ORMEnum.Dapper, ORMEnum.EFCore, ORMEnum.Hibernate, ORMEnum.MyBatis];

    /// <summary>The sources that state a key of their own, in both class languages.</summary>
    private static readonly ORMEnum[] EntitySources = [ORMEnum.EFCore, ORMEnum.NHibernate, ORMEnum.Hibernate, ORMEnum.EclipseLink];

    public static IReadOnlyList<LimitAxis> Axes { get; } =
    [
        new("joins", "inner joins over the product table, each on the key of the one before", Geometric(1, 512), QuerySources, Uncapped: false, OverTheFixture: true, Joins),
        new("conditions", "predicates joined by AND in one flat filter", Geometric(1, 4096), QuerySources, Uncapped: false, OverTheFixture: true, Conditions),
        new("nesting", "levels of parentheses around the one predicate of the filter; the cap of decision 092 is off", Geometric(2, 4096), QuerySources, Uncapped: true, OverTheFixture: true, Nesting),
        new("subqueries", "levels of subqueries nested in the right side of IN; the cap of decision 092 is off", Geometric(1, 2048), QuerySources, Uncapped: true, OverTheFixture: true, Subqueries),
        new("exists", "correlated EXISTS subqueries side by side in one filter", Geometric(1, 512), QuerySources, Uncapped: false, OverTheFixture: true, Exists),
        new("ctes", "intermediate results chained one on the other, the query reading the last", Geometric(1, 256), IntermediateResultSources, Uncapped: false, OverTheFixture: true, Ctes),
        new("unions", "set operations chaining selects over the product table; the cap is off", Geometric(1, 256), UnionSources, Uncapped: true, OverTheFixture: true, Unions),
        new("expression", "terms of one arithmetic expression in the projection; the cap is off", Geometric(1, 4096), QuerySources, Uncapped: true, OverTheFixture: true, Expression),
        new("case", "levels of CASE nested in the ELSE of the one above, in the projection; the cap is off", Geometric(1, 4096), QuerySources, Uncapped: true, OverTheFixture: true, Case),
        new("not", "NOTs chained before the one predicate of the filter; the cap is off", Geometric(2, 4096), QuerySources, Uncapped: true, OverTheFixture: true, Not),
        new("properties", "scalar properties of one entity beside its key", Geometric(8, 4096), EntitySources, Uncapped: false, OverTheFixture: false, Properties),
        new("relations", "many-to-one references from one entity to the customer of the domain", Geometric(1, 256), EntitySources, Uncapped: false, OverTheFixture: false, Relations),
        new("key-parts", "parts of the composite key of one entity", Geometric(1, 64), EntitySources, Uncapped: false, OverTheFixture: false, KeyParts),
        new("unit-size", "kilobytes of comment in the file of the domain's entities", [64, 256, 1024, 4096, 16384, 65536], QuerySources, Uncapped: false, OverTheFixture: false, UnitSize),
        // The ceiling is low on purpose: the path compiles every query of a project with every
        // entity of it, so a point costs the square of its size, and the translation time the
        // row carries is what S3 asks about. The translation alone goes on past it (decision 190).
        new("entities", "entities with one query each in one request, the project of S3 scaled", [100, 200, 400, 800], [ORMEnum.EFCore, ORMEnum.Hibernate], Uncapped: false, OverTheFixture: false, Entities, Beyond: [1600, 3200, 6400, 12800]),
    ];

    public static LimitAxis? Axis(string id) => Axes.FirstOrDefault(axis => axis.Id.Equals(id, StringComparison.Ordinal));

    /// <summary>The axis and the point a key of the kind <c>series</c> names, or null where no series has that point.</summary>
    public static (LimitAxis Axis, int Point)? Parse(string keyId)
    {
        var dash = keyId.LastIndexOf('-');
        if (dash <= 0 || !int.TryParse(keyId[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var point))
        {
            return null;
        }

        var axis = Axis(keyId[..dash]);
        return axis is null || !axis.Points.Contains(point) ? null : (axis, point);
    }

    /// <summary>The powers of two from the first point up to the ceiling, both included.</summary>
    private static int[] Geometric(int first, int ceiling)
    {
        var points = new List<int>();
        for (var point = first; point <= ceiling; point *= 2)
        {
            points.Add(point);
        }

        return [.. points];
    }

    private static string Schema => QueryShapeInputs.Schema;

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    // ---- the query series ----------------------------------------------------------------

    private static List<ConversionSource> Joins(ORMEnum source, int n)
    {
        var sql = new StringBuilder($"SELECT p0.ProductName AS ProductName FROM {Schema}.ShopProducts AS p0");
        var hql = new StringBuilder("select p0.ProductName as ProductName from ShopProduct p0");
        var jpql = new StringBuilder("select p0.ProductName as ProductName from ShopProduct p0");
        var linq = new StringBuilder("from p0 in ctx.ShopProducts");
        for (var i = 1; i <= n; i++)
        {
            sql.Append($" INNER JOIN {Schema}.ShopProducts AS p{i} ON p{i}.ProductId = p{i - 1}.ProductId");
            hql.Append($" inner join ShopProduct p{i} with p{i}.ProductId = p{i - 1}.ProductId");
            jpql.Append($" inner join ShopProduct p{i} on p{i}.ProductId = p{i - 1}.ProductId");
            linq.Append($" join p{i} in ctx.ShopProducts on p{i - 1}.ProductId equals p{i}.ProductId");
        }

        sql.Append($" WHERE p{n}.UnitPrice > 0");
        hql.Append($" where p{n}.UnitPrice > 0");
        jpql.Append($" where p{n}.UnitPrice > 0");
        linq.Append($" where p{n}.UnitPrice > 0 select new {{ ProductName = p0.ProductName }}");

        return Query(source, "FindJoins", sql.ToString(), $"({linq})", hql.ToString(), jpql.ToString());
    }

    private static List<ConversionSource> Conditions(ORMEnum source, int n)
    {
        var sql = string.Join(" AND ", Enumerable.Range(1, n).Select(i => $"p.ProductId <> -{N(i)}"));
        var hql = string.Join(" and ", Enumerable.Range(1, n).Select(i => $"p.ProductId <> -{N(i)}"));
        var linq = string.Join(" && ", Enumerable.Range(1, n).Select(i => $"p.ProductId != -{N(i)}"));

        return Query(
            source,
            "FindConditions",
            $"SELECT p.ProductName AS ProductName FROM {Schema}.ShopProducts AS p WHERE {sql}",
            $"ctx.ShopProducts.Where(p => {linq}).Select(p => new {{ ProductName = p.ProductName }})",
            $"select p.ProductName as ProductName from ShopProduct p where {hql}",
            $"select p.ProductName as ProductName from ShopProduct p where {hql}");
    }

    private static List<ConversionSource> Nesting(ORMEnum source, int n)
    {
        var open = new string('(', n);
        var close = new string(')', n);

        return Query(
            source,
            "FindNesting",
            $"SELECT p.ProductName AS ProductName FROM {Schema}.ShopProducts AS p WHERE {open}p.UnitPrice > 0{close}",
            $"ctx.ShopProducts.Where(p => {open}p.UnitPrice > 0{close}).Select(p => new {{ ProductName = p.ProductName }})",
            $"select p.ProductName as ProductName from ShopProduct p where {open}p.UnitPrice > 0{close}",
            $"select p.ProductName as ProductName from ShopProduct p where {open}p.UnitPrice > 0{close}");
    }

    private static List<ConversionSource> Subqueries(ORMEnum source, int n)
    {
        // Level i filters its product by the ids level i + 1 selects; the innermost level
        // filters on the price, so that every level is a subquery with a filter of its own.
        var sql = new StringBuilder($"SELECT p0.ProductName AS ProductName FROM {Schema}.ShopProducts AS p0 WHERE ");
        var hql = new StringBuilder("select p0.ProductName as ProductName from ShopProduct p0 where ");
        for (var i = 1; i <= n; i++)
        {
            sql.Append($"p{i - 1}.ProductId IN (SELECT p{i}.ProductId FROM {Schema}.ShopProducts AS p{i} WHERE ");
            hql.Append($"p{i - 1}.ProductId in (select p{i}.ProductId from ShopProduct p{i} where ");
        }

        sql.Append($"p{n}.UnitPrice > 0").Append(new string(')', n));
        hql.Append($"p{n}.UnitPrice > 0").Append(new string(')', n));

        var linq = $"p{n}.UnitPrice > 0";
        for (var i = n; i >= 1; i--)
        {
            linq = $"ctx.ShopProducts.Where(p{i} => {linq}).Select(p{i} => p{i}.ProductId).Contains(p{i - 1}.ProductId)";
        }

        return Query(
            source,
            "FindSubqueries",
            sql.ToString(),
            $"ctx.ShopProducts.Where(p0 => {linq}).Select(p0 => new {{ ProductName = p0.ProductName }})",
            hql.ToString(),
            hql.ToString());
    }

    private static List<ConversionSource> Exists(ORMEnum source, int n)
    {
        var sql = string.Join(" AND ", Enumerable.Range(1, n).Select(i =>
            $"EXISTS (SELECT l{i}.LineNumber FROM {Schema}.ShopOrderLines AS l{i} WHERE l{i}.ProductId = p.ProductId AND l{i}.Quantity > -{N(i)})"));
        var hql = string.Join(" and ", Enumerable.Range(1, n).Select(i =>
            $"exists (select l{i}.LineNumber from ShopOrderLine l{i} where l{i}.ProductId = p.ProductId and l{i}.Quantity > -{N(i)})"));
        var linq = string.Join(" && ", Enumerable.Range(1, n).Select(i =>
            $"ctx.ShopOrderLines.Any(l{i} => l{i}.ProductId == p.ProductId && l{i}.Quantity > -{N(i)})"));

        return Query(
            source,
            "FindExists",
            $"SELECT p.ProductName AS ProductName FROM {Schema}.ShopProducts AS p WHERE {sql}",
            $"ctx.ShopProducts.Where(p => {linq}).Select(p => new {{ ProductName = p.ProductName }})",
            $"select p.ProductName as ProductName from ShopProduct p where {hql}",
            $"select p.ProductName as ProductName from ShopProduct p where {hql}");
    }

    private static List<ConversionSource> Ctes(ORMEnum source, int n)
    {
        var sql = new StringBuilder("WITH ");
        var jpql = new StringBuilder("with ");
        var linq = new StringBuilder();
        for (var i = 1; i <= n; i++)
        {
            var from = i == 1 ? $"{Schema}.ShopProducts AS p" : $"c{i - 1} AS p";
            sql.Append(i > 1 ? ", " : string.Empty)
                .Append($"c{i} AS (SELECT p.ProductId AS ProductId, p.ProductName AS ProductName FROM {from} WHERE p.ProductId <> -{N(i)})");
            jpql.Append(i > 1 ? ", " : string.Empty)
                .Append($"c{i} as (select p.ProductId as ProductId, p.ProductName as ProductName from {(i == 1 ? "ShopProduct p" : $"c{i - 1} p")} where p.ProductId <> -{N(i)})");
            linq.Append($"    var c{i} = {(i == 1 ? "ctx.ShopProducts" : $"c{i - 1}")}.Where(p => p.ProductId != -{N(i)}).Select(p => new {{ ProductId = p.ProductId, ProductName = p.ProductName }});\n");
        }

        sql.Append($" SELECT c.ProductName AS ProductName FROM c{n} AS c WHERE c.ProductId > 0");
        jpql.Append($" select c.ProductName as ProductName from c{n} c where c.ProductId > 0");

        var method = $"public void Query()\n{{\n{linq}    var q = c{n}.Where(c => c.ProductId > 0).Select(c => new {{ ProductName = c.ProductName }})\n        .ToList();\n}}";

        return Query(source, "FindCtes", sql.ToString(), null, null, jpql.ToString(), method);
    }

    private static List<ConversionSource> Unions(ORMEnum source, int n)
    {
        var sql = string.Join(" UNION ", Enumerable.Range(0, n + 1).Select(i =>
            $"SELECT p.ProductName AS ProductName FROM {Schema}.ShopProducts AS p WHERE p.ProductId <> -{N(i + 1)}"));
        var jpql = string.Join(" union ", Enumerable.Range(0, n + 1).Select(i =>
            $"select p.ProductName as ProductName from ShopProduct p where p.ProductId <> -{N(i + 1)}"));
        var linq = new StringBuilder($"ctx.ShopProducts.Where(p => p.ProductId != -1).Select(p => new {{ ProductName = p.ProductName }})");
        for (var i = 1; i <= n; i++)
        {
            linq.Append($".Union(ctx.ShopProducts.Where(p => p.ProductId != -{N(i + 1)}).Select(p => new {{ ProductName = p.ProductName }}))");
        }

        return Query(source, "FindUnions", sql, linq.ToString(), null, jpql);
    }

    private static List<ConversionSource> Expression(ORMEnum source, int n)
    {
        var terms = string.Join(" + ", Enumerable.Range(1, n).Select(N));

        return Query(
            source,
            "FindExpression",
            $"SELECT p.ProductName AS ProductName, p.UnitPrice + {terms} AS Total FROM {Schema}.ShopProducts AS p",
            $"ctx.ShopProducts.Select(p => new {{ ProductName = p.ProductName, Total = p.UnitPrice + {terms} }})",
            $"select p.ProductName as ProductName, p.UnitPrice + {terms} as Total from ShopProduct p",
            $"select p.ProductName as ProductName, p.UnitPrice + {terms} as Total from ShopProduct p");
    }

    private static List<ConversionSource> Case(ORMEnum source, int n)
    {
        // Level i takes the value i where the price exceeds i and the CASE of level i + 1
        // otherwise; the innermost ELSE is 0. No bracket anywhere, so the depth is the model's.
        string sql = "0", jpql = "0", linq = "0";
        for (var i = n; i >= 1; i--)
        {
            sql = $"CASE WHEN p.UnitPrice > {N(i)} THEN {N(i)} ELSE {sql} END";
            jpql = $"case when p.UnitPrice > {N(i)} then {N(i)} else {jpql} end";
            linq = $"p.UnitPrice > {N(i)} ? {N(i)} : {linq}";
        }

        return Query(
            source,
            "FindCase",
            $"SELECT p.ProductName AS ProductName, {sql} AS Total FROM {Schema}.ShopProducts AS p",
            $"ctx.ShopProducts.Select(p => new {{ ProductName = p.ProductName, Total = {linq} }})",
            $"select p.ProductName as ProductName, {jpql} as Total from ShopProduct p",
            $"select p.ProductName as ProductName, {jpql} as Total from ShopProduct p");
    }

    private static List<ConversionSource> Not(ORMEnum source, int n)
    {
        // An even number of NOTs, so that the filter is the predicate and every point returns rows.
        var sql = string.Concat(Enumerable.Repeat("NOT ", n));
        var jpql = string.Concat(Enumerable.Repeat("not ", n));

        return Query(
            source,
            "FindNot",
            $"SELECT p.ProductName AS ProductName FROM {Schema}.ShopProducts AS p WHERE {sql}p.UnitPrice > 0",
            $"ctx.ShopProducts.Where(p => {new string('!', n)}(p.UnitPrice > 0)).Select(p => new {{ ProductName = p.ProductName }})",
            $"select p.ProductName as ProductName from ShopProduct p where {jpql}p.UnitPrice > 0",
            $"select p.ProductName as ProductName from ShopProduct p where {jpql}p.UnitPrice > 0");
    }

    /// <summary>
    /// The units of one query point for one source: the domain's entities as the source states
    /// them, then the query in the source's language - a SQL file for Dapper, a mapper document
    /// for MyBatis, a method for EF Core, bare HQL for NHibernate, JPQL for the two JPA sources.
    /// A null text means the source does not state the series.
    /// </summary>
    private static List<ConversionSource> Query(ORMEnum source, string name, string sql, string? linq, string? hql, string? jpql, string? linqMethod = null)
    {
        var statementId = char.ToLowerInvariant(name[0]) + name[1..];
        ConversionSource query = source switch
        {
            ORMEnum.Dapper => new() { Name = $"{name}.sql", ContentType = ConversionContentType.SqlQuery, Content = sql },
            ORMEnum.MyBatis => new()
            {
                Name = $"{name}Mapper.xml",
                ContentType = ConversionContentType.XML,
                Content = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
                    + "<!DOCTYPE mapper PUBLIC \"-//mybatis.org//DTD Mapper 3.0//EN\"\n        \"https://mybatis.org/dtd/mybatis-3-mapper.dtd\">\n"
                    + $"<mapper namespace=\"Shop.{name}Mapper\">\n  <select id=\"{statementId}\" resultType=\"ShopProduct\">\n    "
                    + sql.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal)
                    + "\n  </select>\n</mapper>\n",
            },
            ORMEnum.EFCore => new()
            {
                Name = $"{name}.query.cs",
                ContentType = ConversionContentType.CSharp,
                Content = linqMethod ?? (linq is null ? throw Unstated(source, name) : $"public void Query()\n{{\n    var q = {linq}\n        .ToList();\n}}"),
            },
            ORMEnum.NHibernate => new() { Name = $"{name}.hql", ContentType = ConversionContentType.HqlQuery, Content = hql ?? throw Unstated(source, name) },
            ORMEnum.Hibernate or ORMEnum.EclipseLink => new() { Name = $"{name}.jpql", ContentType = ConversionContentType.JpqlQuery, Content = jpql ?? throw Unstated(source, name) },
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };

        return [.. QueryShapeInputs.MappingUnits(source), query];
    }

    private static InvalidOperationException Unstated(ORMEnum source, string name)
        => new($"{source} does not state the series {name}; the axis lists the sources that do.");

    // ---- the entity series ---------------------------------------------------------------

    private static List<ConversionSource> Properties(ORMEnum source, int n)
    {
        var properties = Enumerable.Range(1, n).Select(i => (Name: $"P{N(i)}", Text: i % 2 == 1)).ToList();

        return Entity(
            source,
            "Wide",
            csharp: string.Concat(properties.Select(p => $"    public {(source == ORMEnum.NHibernate ? "virtual " : string.Empty)}{(p.Text ? "string" : "int")} {p.Name} {{ get; set; }}\n")),
            hbm: string.Concat(properties.Select(p => $"    <property name=\"{p.Name}\" column=\"{p.Name}\" type=\"{(p.Text ? "String" : "Int32")}\"{(p.Text ? string.Empty : " not-null=\"true\"")} />\n")),
            java: string.Concat(properties.Select(p => $"    @Column(name = \"{p.Name}\"{(p.Text ? string.Empty : ", nullable = false")})\n    private {(p.Text ? "String" : "int")} {p.Name};\n\n")),
            keyParts: 1,
            withDomain: false);
    }

    private static List<ConversionSource> Relations(ORMEnum source, int n)
    {
        var references = Enumerable.Range(1, n).Select(i => $"Customer{N(i)}").ToList();

        return Entity(
            source,
            "Hub",
            csharp: string.Concat(references.Select(r => source == ORMEnum.NHibernate
                ? $"    public virtual ShopCustomer {r} {{ get; set; }}\n"
                : $"    public int {r}Id {{ get; set; }}\n    [ForeignKey(nameof({r}Id))]\n    public ShopCustomer {r} {{ get; set; }}\n")),
            hbm: string.Concat(references.Select(r => $"    <many-to-one name=\"{r}\" column=\"{r}Id\" class=\"ShopCustomer\" not-null=\"true\" />\n")),
            java: string.Concat(references.Select(r => $"    @ManyToOne\n    @JoinColumn(name = \"{r}Id\", nullable = false)\n    private ShopCustomer {r};\n\n")),
            keyParts: 1,
            withDomain: true);
    }

    private static List<ConversionSource> KeyParts(ORMEnum source, int n)
        => Entity(
            source,
            "Composite",
            csharp: $"    public {(source == ORMEnum.NHibernate ? "virtual " : string.Empty)}string Payload {{ get; set; }}\n",
            hbm: "    <property name=\"Payload\" column=\"Payload\" type=\"String\" />\n",
            java: "    @Column(name = \"Payload\")\n    private String Payload;\n\n",
            keyParts: n,
            withDomain: false);

    /// <summary>
    /// One entity in the language of the source: the key - one part named after the entity, or
    /// <c>K1</c>…<c>Kn</c> as a composite - and the members given; the domain's units before it
    /// where the members refer to an entity of the domain.
    /// </summary>
    private static List<ConversionSource> Entity(ORMEnum source, string name, string csharp, string hbm, string java, int keyParts, bool withDomain)
    {
        var table = name + "s";
        var keys = keyParts == 1 ? [name + "Id"] : Enumerable.Range(1, keyParts).Select(i => $"K{N(i)}").ToList();
        var domain = withDomain ? QueryShapeInputs.MappingUnits(source) : [];

        switch (source)
        {
            case ORMEnum.EFCore:
            {
                var keyAttribute = keyParts == 1 ? string.Empty : $"[PrimaryKey({string.Join(", ", keys.Select(k => $"nameof({k})"))})]\n";
                var keyMembers = string.Concat(keys.Select(k => $"{(keyParts == 1 ? "    [Key]\n" : string.Empty)}    public int {k} {{ get; set; }}\n"));
                var text = "using System.ComponentModel.DataAnnotations;\nusing System.ComponentModel.DataAnnotations.Schema;\nusing Microsoft.EntityFrameworkCore;\n\nnamespace Shop;\n\n"
                    + $"[Table(\"{table}\", Schema = \"{Schema}\")]\n{keyAttribute}public class {name}\n{{\n{keyMembers}{csharp}}}\n";
                return [.. domain, new ConversionSource { Name = $"{name}.cs", ContentType = ConversionContentType.CSharp, Content = text }];
            }

            case ORMEnum.NHibernate:
            {
                var keyMembers = string.Concat(keys.Select(k => $"    public virtual int {k} {{ get; set; }}\n"));
                var classText = $"namespace Shop;\n\npublic class {name}\n{{\n{keyMembers}{csharp}}}\n";
                var id = keyParts == 1
                    ? $"    <id name=\"{keys[0]}\" column=\"{keys[0]}\" type=\"Int32\">\n      <generator class=\"assigned\" />\n    </id>\n"
                    : "    <composite-id>\n" + string.Concat(keys.Select(k => $"      <key-property name=\"{k}\" column=\"{k}\" type=\"Int32\" />\n")) + "    </composite-id>\n";
                var mapping = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<hibernate-mapping xmlns=\"urn:nhibernate-mapping-2.2\" namespace=\"Shop\">\n"
                    + $"  <class name=\"{name}\" table=\"{table}\" schema=\"{Schema}\">\n{id}{hbm}  </class>\n</hibernate-mapping>\n";
                return
                [
                    .. domain,
                    new ConversionSource { Name = $"{name}.cs", ContentType = ConversionContentType.CSharp, Content = classText },
                    new ConversionSource { Name = $"{name}.hbm.xml", ContentType = ConversionContentType.XML, Content = mapping },
                ];
            }

            case ORMEnum.Hibernate or ORMEnum.EclipseLink:
            {
                var imports = "import jakarta.persistence.Column;\nimport jakarta.persistence.Entity;\nimport jakarta.persistence.Id;\n"
                    + (keyParts > 1 ? "import jakarta.persistence.IdClass;\n" : string.Empty)
                    + (withDomain ? "import jakarta.persistence.JoinColumn;\nimport jakarta.persistence.ManyToOne;\n" : string.Empty)
                    + "import jakarta.persistence.Table;\n"
                    + (keyParts > 1 ? "import java.io.Serializable;\nimport java.util.Objects;\n" : string.Empty);
                var idClass = keyParts > 1 ? $"@IdClass({name}.{name}Id.class)\n" : string.Empty;
                var keyMembers = string.Concat(keys.Select(k => $"    @Id\n    @Column(name = \"{k}\")\n    private Integer {k};\n\n"));
                var nested = keyParts > 1
                    ? $"    public static class {name}Id implements Serializable {{\n"
                      + string.Concat(keys.Select(k => $"        private Integer {k};\n"))
                      + $"\n        public {name}Id() {{\n        }}\n\n        @Override\n        public boolean equals(Object obj) {{\n            return obj instanceof {name}Id other"
                      + string.Concat(keys.Select(k => $"\n                && Objects.equals({k}, other.{k})"))
                      + $";\n        }}\n\n        @Override\n        public int hashCode() {{\n            return Objects.hash({string.Join(", ", keys)});\n        }}\n    }}\n"
                    : string.Empty;
                var text = $"package Shop;\n\n{imports}\n@Entity\n@Table(name = \"{table}\", schema = \"{Schema}\")\n{idClass}public class {name} {{\n{keyMembers}{java}{nested}}}\n";
                return [.. domain, new ConversionSource { Name = $"{name}.java", ContentType = ConversionContentType.Java, Content = text }];
            }

            default:
                throw new InvalidOperationException($"{source} states no key of its own, so it does not state the entity series (decision 182).");
        }
    }

    /// <summary>The domain's units with a comment of the given kilobytes appended to the first class file: the size of a unit grows, nothing else does.</summary>
    private static List<ConversionSource> UnitSize(ORMEnum source, int kilobytes)
    {
        var units = QueryShapeInputs.MappingUnits(source);
        var line = "// " + new string('x', 76) + "\n";
        var padding = new StringBuilder(kilobytes * 1024 + line.Length);
        while (padding.Length < kilobytes * 1024)
        {
            padding.Append(line);
        }

        var first = units[0];
        units[0] = new ConversionSource { Name = first.Name, ContentType = first.ContentType, Content = first.Content + "\n" + padding };
        return units;
    }

    /// <summary>The project of S3 scaled: n entities and one query over each, in EF Core as <see cref="PerformanceProject"/> states it, or in JPA.</summary>
    private static List<ConversionSource> Entities(ORMEnum source, int n)
    {
        var units = new List<ConversionSource>(2 * n);

        if (source == ORMEnum.EFCore)
        {
            for (var i = 0; i < n; i++)
            {
                var entity = PerformanceProject.SyntheticEntity(i);
                units.Add(new ConversionSource { Name = $"Entity{N(i)}.cs", ContentType = entity.ContentType, Content = entity.Content });
            }

            for (var i = 0; i < n; i++)
            {
                var query = PerformanceProject.SyntheticQuery(i);
                units.Add(new ConversionSource { Name = $"Query{N(i)}.query.cs", ContentType = query.ContentType, Content = query.Content });
            }

            return units;
        }

        if (source != ORMEnum.Hibernate)
        {
            throw new InvalidOperationException($"{source} does not state the series of entities; EF Core and Hibernate do (decision 182).");
        }

        for (var i = 0; i < n; i++)
        {
            units.Add(new ConversionSource
            {
                Name = $"Entity{N(i)}.java",
                ContentType = ConversionContentType.Java,
                Content = "package Perf;\n\nimport jakarta.persistence.Column;\nimport jakarta.persistence.Entity;\nimport jakarta.persistence.Id;\nimport jakarta.persistence.Table;\nimport java.math.BigDecimal;\nimport java.time.LocalDateTime;\n\n"
                    + $"@Entity\n@Table(name = \"Entity{N(i)}Rows\", schema = \"Perf\")\npublic class Entity{N(i)} {{\n"
                    + $"    @Id\n    @Column(name = \"Entity{N(i)}Id\")\n    private Integer Entity{N(i)}Id;\n\n"
                    + "    @Column(name = \"Name\", length = 200)\n    private String Name;\n\n"
                    + "    @Column(name = \"Amount\", nullable = false)\n    private BigDecimal Amount;\n\n"
                    + "    @Column(name = \"CreatedAt\", nullable = false)\n    private LocalDateTime CreatedAt;\n\n"
                    + "    @Column(name = \"IsActive\", nullable = false)\n    private boolean IsActive;\n}\n",
            });
        }

        for (var i = 0; i < n; i++)
        {
            units.Add(new ConversionSource
            {
                Name = $"Query{N(i)}.jpql",
                ContentType = ConversionContentType.JpqlQuery,
                Content = $"select e.Name as Name, e.Amount as Amount from Entity{N(i)} e where e.Amount > {N(i)} order by e.Name asc",
            });
        }

        return units;
    }
}
