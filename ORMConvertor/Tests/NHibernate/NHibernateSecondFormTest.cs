using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;
using NHibernateWrappers;
using OrmConvertor;
using SampleData;

namespace Tests.NHibernate;

/// <summary>
/// The second form of an NHibernate query (decision 118): the LINQ chain over session.Query
/// that comes out beside the HQL, the content type it carries, the record of kind Omitted
/// where the provider does not speak the query, and the two rules of the template around it -
/// no second form after the escape path, no record twice about one event.
/// </summary>
public class NHibernateSecondFormTest
{
    private static EntityMap Customers()
    {
        var id = new Property { Name = "CustomerId", Type = LangType.Scalar(ScalarType.Int) };
        var name = new Property { Name = "CustomerName", Type = LangType.Scalar(ScalarType.String) };
        var limit = new Property { Name = "CreditLimit", Type = LangType.Scalar(ScalarType.Decimal, isNullable: true) };

        var map = new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [id, name, limit] },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "CustomerId" },
                new PropertyMap { Property = name, ColumnName = "CustomerName", IsNullable = false },
                new PropertyMap { Property = limit, ColumnName = "CreditLimitAmount", IsNullable = true },
            ],
        };
        map.PrimaryKey = new PrimaryKey { Parts = [new PrimaryKeyPart { PropertyMap = map.PropertyMaps[0], Order = 1 }] };
        return map;
    }

    private static EntityMap Orders()
    {
        var id = new Property { Name = "OrderId", Type = LangType.Scalar(ScalarType.Int) };
        var customer = new Property { Name = "CustomerId", Type = LangType.Scalar(ScalarType.Int) };
        var total = new Property { Name = "Total", Type = LangType.Scalar(ScalarType.Decimal) };

        var map = new EntityMap
        {
            Entity = new Entity { Name = "SalesOrder", Properties = [id, customer, total] },
            Table = "Orders",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "OrderId" },
                new PropertyMap { Property = customer, ColumnName = "CustomerId" },
                new PropertyMap { Property = total, ColumnName = "Total" },
            ],
        };
        map.PrimaryKey = new PrimaryKey { Parts = [new PrimaryKeyPart { PropertyMap = map.PropertyMaps[0], Order = 1 }] };
        return map;
    }

    private static NHibernateHqlQueryBuilder Builder() => new() { EntityMaps = [Customers(), Orders()] };

    private static string Form(List<ConversionSource> artifacts, ConversionContentType type, AbstractQueryBuilder? builder = null)
    {
        var matching = artifacts.Where(s => s.ContentType == type).ToList();
        Assert.True(
            matching.Count == 1,
            $"Expected one {type} artifact, found {matching.Count} among [{string.Join(", ", artifacts.Select(a => a.ContentType))}]"
            + (builder is null ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, builder.Records.Select(r => $"  {r.Kind} {r.Feature}: {r.Reason}"))));

        return matching[0].Content;
    }

    /// <summary>
    /// The sample of the translation screen, through the orchestration: the HQL method, the
    /// bare HQL and the LINQ form, which takes the same typed parameter the HQL method binds
    /// and captures it in its lambda (decision 083).
    /// </summary>
    [Fact]
    public void TheSampleComesOutInBothForms()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.NHibernate,
            ORMEnum.NHibernate,
            [
                new() { Content = CustomerSampleNHibernate.Source, ContentType = ConversionContentType.CSharp },
                new() { Content = CustomerSampleNHibernate.XmlMapping, ContentType = ConversionContentType.XML },
            ]);

        var hql = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.CSharpQuery).Content;
        var linq = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.CSharpLinqQuery).Content;

        Assert.Contains("session.CreateQuery(", hql);
        Assert.Contains(".SetParameter(\"minimumCreditLimit\", minimumCreditLimit)", hql);

        Assert.StartsWith("public static IQueryable<Customer> Query(ISession session, decimal minimumCreditLimit)", linq);
        Assert.Contains("return session.Query<Customer>()", linq);
        Assert.Contains(".Where(c => c.CreditLimit > minimumCreditLimit)", linq);
        Assert.Contains(".OrderByDescending(c => c.AccountOpenedDate)", linq);
        Assert.Contains(".ThenBy(c => c.CustomerName)", linq);

        Assert.DoesNotContain(result.Records, r => r.Kind is ConversionRecordKind.Omitted or ConversionRecordKind.Failure);
    }

    /// <summary>
    /// A right outer join: HQL has it, the provider of NHibernate 5.7.0 does not, so the HQL
    /// form stands alone and one record of kind Omitted names the join kind and the form -
    /// not a Failure, since the query is translated, and not a Fallback, since the binding
    /// form is in the target's own language (decision 118).
    /// </summary>
    [Fact]
    public void ARightJoinOmitsTheLinqFormWithARecord()
    {
        var builder = Builder();
        builder.Push();
        builder.From("Sales.Customers", alias: "c");
        builder.Join(
            JoinKind.Right,
            "Sales.Customers",
            "Sales.Orders",
            new ComparisonCondition(QueryOperand.Column("c", "CustomerId"), ComparisonOperator.Equal, QueryOperand.Column("o", "CustomerId")),
            rightTableAlias: "o");
        builder.Project("c", "CustomerName");
        builder.Project("o", "Total");
        builder.Pop();

        var artifacts = builder.Build();

        Assert.Contains("right join", Form(artifacts, ConversionContentType.HqlQuery, builder));
        Assert.DoesNotContain(artifacts, s => s.ContentType == ConversionContentType.CSharpLinqQuery);

        var omitted = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Omitted);
        Assert.Equal(ConversionContentType.CSharpLinqQuery, omitted.Artifact);
        Assert.Equal(QueryFeature.JoinKind, omitted.Feature);
        Assert.Contains("right outer join", omitted.Reason);
        Assert.EndsWith("so the query has no LINQ form and the HQL form stands alone.", omitted.Reason);

        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Fallback);
    }

    /// <summary>
    /// After the escape path there is no second form: the native SQL already is the query, and
    /// what HQL does not speak the provider, which translates into HQL, does not speak either.
    /// </summary>
    [Fact]
    public void TheEscapePathWritesNoLinqForm()
    {
        var builder = Builder();
        builder.Push();
        builder.From("Sales.Customers", alias: "c");
        builder.Pop();
        builder.SetOperation(SetOperationType.Union);
        builder.Push();
        builder.From("Sales.Customers", alias: "c");
        builder.Pop();

        var artifacts = builder.Build();

        Assert.Contains("session.CreateSQLQuery(", Form(artifacts, ConversionContentType.CSharpQuery));
        Assert.DoesNotContain(artifacts, s => s.ContentType == ConversionContentType.CSharpLinqQuery);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Fallback);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Omitted);
    }

    /// <summary>
    /// Both forms derive the type of an unmapped table by the naming convention and both would
    /// say so; the template keeps one record of the event (decision 066), the binding form's.
    /// </summary>
    [Fact]
    public void ARecordBothFormsMakeIsKeptOnce()
    {
        var builder = new NHibernateHqlQueryBuilder();
        builder.Push();
        builder.From("Sales.Invoices", alias: "i");
        builder.Project("i", "Total");
        builder.Pop();

        var artifacts = builder.Build();

        Assert.Contains("session.Query<Invoice>()", Form(artifacts, ConversionContentType.CSharpLinqQuery, builder));
        var convention = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Convention && r.Reason.Contains("No entity was mapped"));
        Assert.Equal(ConversionContentType.CSharpQuery, convention.Artifact);
    }

    /// <summary>
    /// Under DISTINCT the provider keeps an ordering before the projection and refuses one
    /// after Distinct(), the opposite of EF Core (decision 073), so the chain orders over the
    /// row first; an alias of the projection resolves to the value it names.
    /// </summary>
    [Fact]
    public void TheOrderingOfADistinctQueryPrecedesTheProjection()
    {
        var builder = Builder();
        builder.Push();
        builder.From("Sales.Customers", alias: "c");
        builder.Project("c", "CustomerName", "Name");
        builder.Distinct();
        builder.OrderBy(null, "Name", asc: false);
        builder.Pop();

        var linq = Form(builder.Build(), ConversionContentType.CSharpLinqQuery, builder);

        var ordering = linq.IndexOf(".OrderByDescending(c => c.CustomerName)", StringComparison.Ordinal);
        var projection = linq.IndexOf(".Select(c => new { Name = c.CustomerName })", StringComparison.Ordinal);
        var distinct = linq.IndexOf(".Distinct()", StringComparison.Ordinal);

        Assert.True(ordering >= 0 && projection > ordering && distinct > projection, linq);
    }

    /// <summary>
    /// COUNT over a column that may hold NULL is the sum of a conditional over the group, the
    /// shape the provider translates to the count of the non-null values; COUNT over a
    /// column that holds none stays Count().
    /// </summary>
    [Fact]
    public void ACountOverANullableColumnIsASumOverAConditional()
    {
        var builder = Builder();
        builder.Push();
        builder.From("Sales.Customers", alias: "c");
        builder.GroupBy("c", "CustomerName");
        builder.Project("c", "CustomerName", "Name");
        builder.Project("c", "CreditLimitAmount", "Limits", "COUNT");
        builder.Project("c", "CustomerId", "Rows", "COUNT");
        builder.Pop();

        var linq = Form(builder.Build(), ConversionContentType.CSharpLinqQuery, builder);

        Assert.Contains("Limits = g.Sum(c => c.CreditLimit != null ? 1 : 0)", linq);
        Assert.Contains("Rows = g.Count()", linq);
    }

    /// <summary>
    /// The provider does not escape the argument of a string method (decision 051), so a core
    /// the source escaped goes through the Like extension with the escape character; a core
    /// without a wildcard is the string method, as in EF Core.
    /// </summary>
    [Fact]
    public void AnEscapedPatternGoesThroughTheLikeExtension()
    {
        var builder = Builder();
        builder.Push();
        builder.From("Sales.Customers", alias: "c");
        builder.Where(new LogicalCondition(LogicalOperator.And,
        [
            new ComparisonCondition(QueryOperand.Column("c", "CustomerName"), ComparisonOperator.Like, QueryOperand.Value(QueryConstant.Of("A!_%", ScalarType.String)), Escape: "!"),
            new ComparisonCondition(QueryOperand.Column("c", "CustomerName"), ComparisonOperator.Like, QueryOperand.Value(QueryConstant.Of("B%", ScalarType.String))),
        ]));
        builder.Pop();

        var linq = Form(builder.Build(), ConversionContentType.CSharpLinqQuery, builder);

        Assert.Contains("c.CustomerName.Like(\"A!_%\", '!')", linq);
        Assert.Contains("c.CustomerName.StartsWith(\"B\")", linq);
    }

    /// <summary>A left join in the one shape the provider knows: GroupJoin, then the group flattened with DefaultIfEmpty.</summary>
    [Fact]
    public void ALeftJoinIsAGroupJoinFlattenedWithDefaultIfEmpty()
    {
        var builder = Builder();
        builder.Push();
        builder.From("Sales.Customers", alias: "c");
        builder.Join(
            JoinKind.Left,
            "Sales.Customers",
            "Sales.Orders",
            new ComparisonCondition(QueryOperand.Column("c", "CustomerId"), ComparisonOperator.Equal, QueryOperand.Column("o", "CustomerId")),
            rightTableAlias: "o");
        builder.Project("c", "CustomerName");
        builder.Project("o", "Total");
        builder.Pop();

        var linq = Form(builder.Build(), ConversionContentType.CSharpLinqQuery, builder);

        Assert.Contains(".GroupJoin(session.Query<SalesOrder>(), c => c.CustomerId, o => o.CustomerId, (c, oGroup) => new { c, oGroup })", linq);
        Assert.Contains(".SelectMany(pair => pair.oGroup.DefaultIfEmpty(), (pair, o) => new { pair.c, o })", linq);
        Assert.Contains(".Select(t => new { CustomerName = t.c.CustomerName, Total = t.o.Total })", linq);
    }
}
