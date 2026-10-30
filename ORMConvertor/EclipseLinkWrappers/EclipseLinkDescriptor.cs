using AbstractWrappers.Descriptors;
using JakartaPersistence;
using Model;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions.Conditions;

namespace EclipseLinkWrappers;

/// <summary>
/// EclipseLink as a target (decision 080): the enforced members and the support of the
/// shared Jakarta Persistence layer, the pinned release, and beside the descriptor the
/// profile of the implementation - what the release number cannot say (decision 076),
/// and what the second implementation makes visible, because every value below differs
/// from Hibernate's behind the very same annotations.
/// </summary>
public static class EclipseLinkDescriptor
{
    public static TargetFrameworkDescriptor Instance { get; } = new()
    {
        Framework = ORMEnum.EclipseLink,
        Ecosystem = Ecosystem.Java,

        // Pinned by decision 013; the canonical table is in docs/architecture.md. The Java
        // test suite of decision 076 binds this value to the dependency of its pom.xml.
        Version = "5.0.0",

        // The database system these artifacts are written for (decision 086); the
        // only dialect this version targets, declared rather than assumed.
        Dialect = DatabaseDialect.SqlServer2022,

        // EclipseLink adds no enforced member of its own to the specification's: its
        // indirection rewrites the entity rather than subclassing it, so it does not even
        // need the non-final class §2.1 demands anyway (decision 080).
        EnforcedMembers = JakartaPersistenceDescriptor.EnforcedMembers,
        Support = JakartaPersistenceDescriptor.Support,

        // EclipseLink groups by an expression beyond the specification's path (decision 113,
        // verified against 5.0.0 over SQL Server: group by extract(year from …)); a key with a
        // literal in it the builder writes under the hint that stops EclipseLink binding the
        // literal (the profile's BindsLiterals and InlineLiteralsCall). No window, no list
        // aggregate, no intermediate result.
        QuerySupport = JakartaPersistenceDescriptor.QuerySupportWith(QueryFeature.ComputedGrouping),

        // The specification's functions less cast: EclipseLink 5.0.0 passes the type name of
        // cast(x as Integer) through to SQL Server, which knows no type Integer, Long or
        // String and refuses the query (decision 113, measured).
        Functions = QueryFunctionVocabulary.AllBut(QueryFunction.DateAdd, QueryFunction.DateDiff, QueryFunction.Cast),
        NativeSqlApi = JakartaPersistenceDescriptor.NativeSqlApi,

        // Under its default session EclipseLink 5.0.0 cuts the trailing characters up to
        // U+0020 off every result column of type CHAR or NCHAR, in JPQL and native SQL alike
        // (decision 169, measured by the Java suite's CharTrimmingClaimsTest).
        TrimsFixedWidthOnRead = true,
    };

    /// <summary>
    /// Measured in the EclipseLink tutorial, in the DDL its 5.0.0 release generated from
    /// annotations that state nothing: AUTO is a counter table named SEQUENCE with the row
    /// SEQ_GEN, an implicit name is written in upper case, and a String is VARCHAR with no
    /// switch anywhere to make it national - only a literal columnDefinition will. To that
    /// the ninth step of the same tutorial added the fact no artifact shows: fetch = LAZY on
    /// a reference is honoured only where the consumer project weaves its bytecode. SEQUENCE
    /// without a generator is the sequence SEQ_GEN_SEQUENCE with a step of 50 - over a
    /// connection only: without one EclipseLink cannot read the server's version and writes
    /// an identity column instead, so the Java suite's eclipselink/KeyGenerationClaimsTest
    /// measures it connected, and AUTO there once more (decision 187).
    /// </summary>
    public static JpaImplementationProfile Profile { get; } = new(
        Implementation: ORMEnum.EclipseLink,
        SpecificationLevel: JakartaPersistenceDescriptor.SpecificationLevel,
        AutoStrategy: PrimaryKeyStrategy.HiLo,
        DefaultAllocationSize: 50,
        DefaultSequenceName: "SEQ_GEN_SEQUENCE",
        NamingStrategy: "JPA-compliant implicit names, written upper case (eclipselink.jpa.uppercase-column-names)",
        NationalizedByDefault: false,
        UppercaseImplicitNames: true,
        VendorAnnotationPackage: "org.eclipse.persistence.annotations",
        LazyReferenceNeedsWeaving: true,
        DefaultCounterTable: new JpaCounterTable("SEQUENCE", "SEQ_NAME", "SEQ_COUNT", "SEQ_GEN"),

        // Measured against 5.0.0 when the escape path was written (decision 113): a List
        // bound to ?1 of a native query reaches the JDBC driver as one value, which the
        // SQL Server driver rejects - the native query does not expand it, so the generated
        // method expands the collection itself, one placeholder per element (decision 123).
        NativeQueryExpandsCollection: false,

        // Measured against 5.0.0 when grouping by an expression was written (decision 113):
        // group by case when o.quantity > 2 then 1 else 0 end reaches SQL Server with six
        // bound parameters, three in the select list and three in GROUP BY, and SQL Server
        // refuses the column inside as neither grouped nor aggregated (error 8120). Binding
        // is a setting of the query: under the hint eclipselink.jdbc.bind-parameters = false
        // the query writes its literals - and its parameters - inline, so the builder adds the
        // hint to a query whose grouping key carries a literal, and that query alone (the
        // Java suite measures it over the category GroupingByAnExpression).
        BindsLiterals: true,
        InlineLiteralsCall: ".setHint(QueryHints.BIND_PARAMETERS, HintValues.FALSE)",

        // Measured against 5.0.0 when a foreign key column without an attribute began to be
        // written through its reference: p.customer.id joins Customers - in the select list
        // it drops the purchase whose customer_CustomerID is NULL, and inside the on of a
        // left join the joined table stands after the outer join and SQL Server refuses
        // "t2.CustomerID could not be bound" (error 4104).
        KeyThroughReferenceJoins: true,

        // Measured against 5.0.0 when the validation set of LDBC Interactive v1 judged the LDBC
        // catalog (decision 117) and EclipseLink alone answered other rows: a join inside a
        // subquery - in, exists, a scalar subquery, of another entity or of the same one - is
        // left out of the SQL with its condition, and a column of the joined entity is read
        // from the subquery's own entity ("select m.CreatorPersonId from Person_knows_Person k1
        // join Message m on ..." became SELECT t1.CreatorPersonId FROM Person_knows_Person t1).
        // The range variables of "from Person_knows_Person k1, Message m where ..." it writes
        // as a cross join with the condition in WHERE, so an inner join goes out that way and
        // only an outer join in a subquery goes out in native SQL.
        DropsJoinsInSubqueries: true,

        // Measured by the Java suite over the category InOverASetOperation (decision 120): the
        // Hermes parser of 5.0.0 reads a union at the top of a query and refuses the same union
        // inside the parentheses of an IN with "Syntax error parsing", so the query goes out in
        // native SQL.
        RefusesSetOperationsInSubqueries: true,

        // Measured the same way: the inner entity joins stand in the list of tables after every
        // outer join ("FROM Message t2 LEFT OUTER JOIN Person_knows_Person t3 ON (... t0 ...),
        // Person t1, Message t0"), so the on of an outer join naming an inner join's alias is
        // refused by SQL Server (error 4104).
        InnerJoinsFollowOuterJoins: true,

        // Measured against 5.0.0 by the first run of the FIBEN catalog (decision 149): a union
        // whose members join other entities answered the rows of the members' own entities
        // alone (q127: 49 870 rows for the 189 of the text; q129, q131, q132 alike), the way
        // a join inside a subquery is left out above, so a set operation with a joined member
        // goes out in native SQL.
        DropsJoinsInSetOperations: true,

        // Measured against 5.0.0 by the same run and pinned by the Java suite (eclipselink/
        // JoinedHavingClaimsTest): when the first path of the having clause, read from the left,
        // is over another variable than the root, the whole clause is rebuilt on the root -
        // "having sum(l.quantity) > 10" over "from HvOrder o join HvLine l" fails with "Invalid
        // query key [quantity]" (FIBEN q4, q176, q135-140, q147-149, q163-169), and "having
        // max(l.id) > 3" becomes HAVING (MAX(t0.id) > ?), the order's key for the line's, other
        // rows without an error. "having count(o.id) > 0 and max(l.id) > 3" it writes as it
        // stands, and so a clause opening with a CASE (LDBC IC 3, BI 16) or a subquery; the
        // select list, group by and order by are never rebuilt. Such a query goes out in native SQL.
        RebuildsHavingOnTheRoot: true,

        // Measured against 5.0.0 with every identifier of its JPQL grammar as an entity name
        // - at the head of a from clause, as the target of an inner and a left entity join
        // and in a subquery: these it refuses as a syntax error wherever an entity is named
        // (in a subquery's from clause it does take "in"), and on "set" its parser does not
        // return at all but runs out of memory; Order and Group it reads.
        EntityNamesRefused: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "as", "except", "from", "having", "in", "inner", "intersect", "join", "left", "outer", "set", "table", "union", "where",
        },

        // ... and these only as the target of an entity join.
        EntityNamesRefusedAsJoinTarget: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "and", "between", "case", "current_date", "current_time", "current_timestamp", "date", "datetime", "delete",
            "false", "is", "like", "local", "member", "new", "not", "null", "on", "or", "regexp", "select", "time", "true",
            "update", "when",
        },

        // Measured against 5.0.0 over SQL Server (decision 161, eclipselink/AverageClaimsTest):
        // AVG(x) is SQL's AVG handed over as a Double. The type name of cast(… as T) reaches
        // SQL Server as written, so the names SQL Server knows convert it back - INTEGER and
        // BIGINT into Integer and Long, DECIMAL(38,6) into a BigDecimal of six places -, in the
        // select list, a subquery and having alike; LONG and DOUBLE it refuses.
        AverageCasts: new JpaAverageCasts("INTEGER", "BIGINT", "DECIMAL({0},{1})"),

        // Measured against 5.0.0 by the experiment run of 2026-10-24 (decision 179) and pinned
        // by eclipselink/BinaryVersionClaimsTest: a unit with @Version over byte[] - what
        // EF Core's [Timestamp] becomes - is refused at deployment with EclipseLink-7168, the
        // version may be int, Integer, short, Short, long, Long or Timestamp only. The builder
        // writes such a column read-only, without @Version, and reports the loss.
        RefusesBinaryVersion: true,

        // Measured the same way (decision 180) and pinned by eclipselink/FullJoinClaimsTest: the
        // Hermes parser of 5.0.0 refuses "full join" with a syntax error - Jakarta Persistence
        // has the inner and the left join only -, so the query goes out in native SQL
        // (decision 113). The right join, the other extension, no run has measured.
        RefusesFullJoin: true,

        // Measured against 5.0.0 by eclipselink/CrossJoinClaimsTest (decision 196): an entity
        // join written after a comma-separated range variable may name any root of the from
        // clause in its condition, before the comma and after it alike, and answers the rows
        // the same T-SQL answers.
        JoinBindsToTheLastRangeVariable: false);
}
