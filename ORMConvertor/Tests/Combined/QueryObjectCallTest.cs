using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// What a Java unit calls on the JPA query object beside the slice (decisions 048 and 109).
/// The reading used to take setParameter, setFirstResult and setMaxResults and drop every
/// other call without a word, while the reading of a CreateQuery in NHibernate named each.
/// Now every call is answered by what it states: a binding and a call that runs the query
/// say nothing the artifact would lose; a lock and a result mapping are losses with reasons
/// of their own; a call or hint that changes which rows the query returns refuses it
/// (decision 070) - which ones do is the implementation's to say, Hibernate's calls and
/// EclipseLink's hints -; and any other call is a loss that names it.
/// </summary>
public class QueryObjectCallTest
{
    private const string Unit = "queries";

    private const string Ordered = "select c from Customer c order by c.CustomerName asc";

    private static ConversionResult Convert(ORMEnum source, string content, ORMEnum target = ORMEnum.EFCore) =>
        ConversionHandler.Convert(
            source,
            target,
            [
                .. CrossFrameworkInputs.MappingUnits(source),
                new() { ContentType = ConversionContentType.Java, Content = content, Name = Unit },
            ]);

    private static string Method(string chain, string create = "createQuery") =>
        $$"""
        public List<Customer> load(EntityManager em, Page page) {
            return em.{{create}}("{{Ordered}}", Customer.class){{chain}};
        }
        """;

    private static List<ConversionRecord> Records(ConversionResult result, ConversionRecordKind kind) =>
        [.. result.Records.Where(r => r.Kind == kind && r.Unit == Unit)];

    private static List<ConversionSource> Queries(ConversionResult result) =>
        [.. result.Sources.Where(s => s.ContentType is ConversionContentType.CSharpQuery or ConversionContentType.JavaQuery)];

    /// <summary>Asserts that the query was translated with exactly one loss, and returns its reason.</summary>
    private static string AssertOneLoss(ConversionResult result)
    {
        Assert.Single(Queries(result));
        Assert.Empty(Records(result, ConversionRecordKind.Failure));
        return Assert.Single(Records(result, ConversionRecordKind.Loss)).Reason;
    }

    /// <summary>Asserts that the query was refused for one reason, and returns it.</summary>
    private static ConversionRecord AssertRefused(ConversionResult result)
    {
        Assert.Empty(Queries(result));
        return Assert.Single(Records(result, ConversionRecordKind.Failure));
    }

    /// <summary>
    /// The setters of the specification that say how the query runs - and a hint, which the
    /// implementation either runs the query differently under or ignores - are each a loss
    /// that names the call, in both implementations.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Hibernate, ".setHint(\"jakarta.persistence.query.timeout\", 5000)", "setHint(\"jakarta.persistence.query.timeout\")")]
    [InlineData(ORMEnum.EclipseLink, ".setHint(QueryHints.READ_ONLY, HintValues.TRUE)", "setHint(QueryHints.READ_ONLY)")]
    [InlineData(ORMEnum.Hibernate, ".setFlushMode(FlushModeType.COMMIT)", "setFlushMode()")]
    [InlineData(ORMEnum.EclipseLink, ".setCacheStoreMode(CacheStoreMode.BYPASS)", "setCacheStoreMode()")]
    [InlineData(ORMEnum.Hibernate, ".setTimeout(5)", "setTimeout()")]
    [InlineData(ORMEnum.Hibernate, ".setReadOnly(true)", "setReadOnly()")]
    public void AnOptionOfTheQueryObjectIsALossThatNamesIt(ORMEnum source, string option, string named)
    {
        var reason = AssertOneLoss(Convert(source, Method(option + ".getResultList()")));

        Assert.StartsWith($"{named} on the query object of createQuery", reason, StringComparison.Ordinal);
    }

    /// <summary>A lock leaves the rows as they are and changes what the query does with them: a loss that says so.</summary>
    [Theory]
    [InlineData(ORMEnum.Hibernate, ".setLockMode(LockModeType.PESSIMISTIC_WRITE)", "setLockMode()")]
    [InlineData(ORMEnum.EclipseLink, ".setLockMode(LockModeType.PESSIMISTIC_READ)", "setLockMode()")]
    [InlineData(ORMEnum.Hibernate, ".setHibernateLockMode(LockMode.UPGRADE_NOWAIT)", "setHibernateLockMode()")]
    [InlineData(ORMEnum.EclipseLink, ".setHint(QueryHints.PESSIMISTIC_LOCK, PessimisticLock.Lock)", "setHint(QueryHints.PESSIMISTIC_LOCK)")]
    public void ALockIsALossWithAReasonOfItsOwn(ORMEnum source, string option, string named)
    {
        var reason = AssertOneLoss(Convert(source, Method(option + ".getResultList()")));

        Assert.StartsWith(named, reason, StringComparison.Ordinal);
        Assert.Contains("lock the rows it reads", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A call that runs the query runs it as it stands - the generated method hands the query
    /// object to the caller, who runs it as the source did -, a getter reads a value off the
    /// object, and what the code then does with the rows or the value is no call on the query
    /// object at all. None of it says anything the artifact would lose.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Hibernate, ".getSingleResult()")]
    [InlineData(ORMEnum.EclipseLink, ".getSingleResultOrNull()")]
    [InlineData(ORMEnum.EclipseLink, ".getResultStream().filter(c -> c.getCreditLimit() != null).toList()")]
    [InlineData(ORMEnum.Hibernate, ".getResultList().subList(0, 1)")]
    [InlineData(ORMEnum.Hibernate, ".list()")]
    [InlineData(ORMEnum.Hibernate, ".uniqueResultOptional().stream().toList()")]
    [InlineData(ORMEnum.Hibernate, ".setParameterList(\"unused\", ids).setProperties(bean).getResultList()")]
    [InlineData(ORMEnum.EclipseLink, ".unwrap(JpaQuery.class).getResultList()")]
    [InlineData(ORMEnum.EclipseLink, ".getParameters().size()")]
    public void ACallThatRunsTheQueryOrReadsAValueSaysNothing(ORMEnum source, string chain)
    {
        var result = Convert(source, Method(chain));

        Assert.Single(Queries(result));
        Assert.Empty(Records(result, ConversionRecordKind.Loss));
        Assert.Empty(Records(result, ConversionRecordKind.Failure));
    }

    /// <summary>
    /// Hibernate's own calls that change which rows the query returns - a page, a keyed page,
    /// a count in place of the rows - refuse the query, naming the call.
    /// </summary>
    [Theory]
    [InlineData(".setPage(page).getResultList()", "setPage()", QueryFeature.Pagination)]
    [InlineData(".getKeyedResultList(page.keyedBy(Customer_.customerName))", "getKeyedResultList()", QueryFeature.Pagination)]
    [InlineData(".getResultCount()", "getResultCount()", QueryFeature.Projection)]
    public void ACallOfHibernateThatChangesTheRowsRefusesTheQuery(string chain, string named, QueryFeature feature)
    {
        var failure = AssertRefused(Convert(ORMEnum.Hibernate, Method(chain, "createSelectionQuery")));

        Assert.StartsWith($"{named} on the query object of createSelectionQuery", failure.Reason, StringComparison.Ordinal);
        Assert.Contains("would change which rows the query returns", failure.Reason, StringComparison.Ordinal);
        Assert.Equal(feature, failure.Feature);
    }

    /// <summary>
    /// EclipseLink's hints that change the rows refuse the query, under the key and under the
    /// constant that spells it. Hibernate does not read them - it runs the query as it would
    /// without them -, so under Hibernate the same hint is a loss like any other.
    /// </summary>
    [Theory]
    [InlineData(".setHint(\"eclipselink.jdbc.max-rows\", 10)", "caps the rows")]
    [InlineData(".setHint(QueryHints.JDBC_FIRST_RESULT, 20)", "skips the first rows")]
    [InlineData(".setHint(QueryHints.AS_OF, \"2026/01/01 00:00:00\")", "as they stood at a point in the past")]
    [InlineData(".setHint(\"eclipselink.query-type\", QueryType.ReadObject)", "another kind of EclipseLink query")]
    public void AHintOfEclipseLinkThatChangesTheRowsRefusesTheQuery(string hint, string effect)
    {
        var failure = AssertRefused(Convert(ORMEnum.EclipseLink, Method(hint + ".getResultList()")));
        Assert.StartsWith("setHint(", failure.Reason, StringComparison.Ordinal);
        Assert.Contains(effect, failure.Reason, StringComparison.Ordinal);

        var reason = AssertOneLoss(Convert(ORMEnum.Hibernate, Method(hint + ".getResultList()")));
        Assert.Contains("is a hint the reading does not take", reason, StringComparison.Ordinal);
    }

    /// <summary>The transformers and EclipseLink's result type say how rows are materialized: one loss that names them all.</summary>
    [Theory]
    [InlineData(ORMEnum.Hibernate, ".setTupleTransformer((tuple, aliases) -> tuple[0]).setResultListTransformer(list -> list)", "(setTupleTransformer(), setResultListTransformer())")]
    [InlineData(ORMEnum.EclipseLink, ".setHint(QueryHints.RESULT_TYPE, ResultType.Map)", "(setHint(QueryHints.RESULT_TYPE))")]
    public void TheResultMappingIsOneLoss(ORMEnum source, string chain, string named)
    {
        var result = Convert(source, Method(chain + ".getResultList()"));

        var reason = AssertOneLoss(result);
        Assert.Contains($"materialized {named}", reason, StringComparison.Ordinal);
        Assert.Equal(QueryFeature.Projection, Records(result, ConversionRecordKind.Loss)[0].Feature);
    }

    /// <summary>
    /// The calls made on the variable that keeps the query object, in later statements, are
    /// answered as the chained ones are; a variable that keeps the rows instead is not
    /// followed, since what is called on it is called on the rows.
    /// </summary>
    [Fact]
    public void ACallOnTheVariableInALaterStatementIsAnsweredToo()
    {
        var result = Convert(
            ORMEnum.Hibernate,
            $$"""
            public List<Customer> load(EntityManager em, boolean locked) {
                TypedQuery<Customer> q = em.createQuery("{{Ordered}}", Customer.class);
                q.setHint("org.hibernate.readOnly", true);
                if (locked) { q.setLockMode(LockModeType.PESSIMISTIC_WRITE); }
                return q.getResultList();
            }
            """);

        Assert.Single(Queries(result));
        var losses = Records(result, ConversionRecordKind.Loss);
        Assert.Equal(2, losses.Count);
        Assert.Contains(losses, l => l.Reason.StartsWith("setHint(\"org.hibernate.readOnly\")", StringComparison.Ordinal));
        Assert.Contains(losses, l => l.Reason.StartsWith("setLockMode()", StringComparison.Ordinal));

        var rows = Convert(
            ORMEnum.Hibernate,
            $$"""
            public Customer first(EntityManager em) {
                List<Customer> rows = em.createQuery("{{Ordered}}", Customer.class).getResultList();
                return rows.isEmpty() ? null : rows.get(0);
            }
            """);

        Assert.Single(Queries(rows));
        Assert.Empty(Records(rows, ConversionRecordKind.Loss));
    }

    /// <summary>A call that changes the rows refuses the query in a later statement too, where it may run on a condition.</summary>
    [Fact]
    public void ACallThatChangesTheRowsInALaterStatementRefusesTheQuery()
    {
        var failure = AssertRefused(Convert(
            ORMEnum.Hibernate,
            $$"""
            public List<Customer> load(Session session, Page page, boolean paged) {
                SelectionQuery<Customer> q = session.createSelectionQuery("{{Ordered}}", Customer.class);
                if (paged) { q.setPage(page); }
                return q.getResultList();
            }
            """));

        Assert.StartsWith("setPage()", failure.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// setParameterList binds a list, which is the one place a native query says so, in a
    /// later statement as much as chained onto the call. It used to fall with the rest of
    /// the calls the reading dropped, and the parameter went out as a single value.
    /// </summary>
    [Fact]
    public void ANativeParameterBoundAsAListIsAListWhereverItIsBound()
    {
        var result = Convert(
            ORMEnum.Hibernate,
            """
            public List<Customer> some(Session session, Set<Integer> wanted) {
                NativeQuery<Customer> q = session.createNativeQuery("SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerId IN (:ids)", Customer.class);
                q.setParameterList("ids", wanted);
                return q.getResultList();
            }
            """,
            ORMEnum.Dapper);

        var output = string.Join("\n", result.Sources.Select(s => s.Content));
        Assert.Contains("c.CustomerId IN @ids", output, StringComparison.Ordinal);
        Assert.Contains("IEnumerable<int> ids", output, StringComparison.Ordinal);
        Assert.Empty(Records(result, ConversionRecordKind.Failure));
    }
}
