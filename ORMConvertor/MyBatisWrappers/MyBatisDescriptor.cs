using AbstractWrappers.Descriptors;
using Model;

namespace MyBatisWrappers;

/// <summary>
/// MyBatis as a target (decision 084). Unlike both JPA wrappers this descriptor carries no
/// profile of an implementation, and that is an answer rather than an omission: a profile
/// tells two implementations of one specification apart, and MyBatis implements nothing.
/// The four settings that would otherwise live in one - mapUnderscoreToCamelCase,
/// autoMappingBehavior, callSettersOnNulls and the global useGeneratedKeys - are facts of
/// the consumer's configuration (decision 040), and the artifact is made independent of
/// them instead of describing them: autoMapping="false" with an explicit column on every
/// property takes the first two their effect, useGeneratedKeys concerns the
/// &lt;insert&gt; the tool does not emit, and the third has nothing to act on, because a
/// nullable property is written with a wrapper type.
/// </summary>
public static class MyBatisDescriptor
{
    public static TargetFrameworkDescriptor Instance { get; } = new()
    {
        Framework = ORMEnum.MyBatis,

        // Pinned by decision 013; the canonical table is in docs/architecture.md, and the
        // Java test suite of decision 076 binds this value to the dependency of its pom.xml.
        Version = "3.5.19",

        // The database system these artifacts are written for (decision 086); the
        // only dialect this version targets, declared rather than assumed.
        Dialect = DatabaseDialect.SqlServer2022,

        EnforcedMembers =
        [
            new EnforcedMember
            {
                Name = "no-arg constructor",
                Condition = EnforcedMemberCondition.Always,
                ForbiddenMarker = "public {ClassName}(",
                Reason = "MyBatis builds the result object through its ObjectFactory, whose default "
                       + "implementation reaches for the no-arg constructor. Nothing in the framework "
                       + "demands a base class, an interface or an annotation - this is the one member "
                       + "it needs - and declaring any constructor removes the implicit one, so the "
                       + "artifact must declare none (decisions 009 and 084).",
            },
        ],

        // The support table is Dapper's plus exactly what <resultMap> holds: the pairs of
        // column and property, the identity of the result, the property with no column, and
        // as much of the type as jdbcType carries. Everything a mapper has nowhere to put -
        // the table and its schema, the length, the precision, the nullability, the key
        // mechanism, the foreign key columns, the version column, the unique constraint -
        // is inexpressible, and the mechanical loss records of decision 010 say so.
        Support = new Dictionary<MappingFactCategory, FactSupport>
        {
            // MyBatis has no notion of a table at all: the name lives inside the SQL of a
            // statement, and the entity mapper this tool writes carries no statement.
            [MappingFactCategory.TableName] = FactSupport.NotExpressible,
            [MappingFactCategory.SchemaName] = FactSupport.NotExpressible,

            [MappingFactCategory.ColumnName] = FactSupport.Expressible,   // <result column>

            // jdbcType names a JDBC family, so the claim survives as far as the family and
            // its unicode facet reach (decision 019); the length and the precision beside it
            // do not, and are reported under their own categories.
            [MappingFactCategory.DatabaseType] = FactSupport.Expressible,

            [MappingFactCategory.Length] = FactSupport.NotExpressible,
            [MappingFactCategory.PrecisionAndScale] = FactSupport.NotExpressible,
            [MappingFactCategory.Nullability] = FactSupport.NotExpressible,

            // <id> writes the key's columns in the key's order, so writing the key is exact.
            // Reading one back is not - MyBatis marks the identity of a result with the same
            // element - and decision 084 says that asymmetry out loud: a MyBatis round trip
            // does not keep the key without a catalog, and the record that says so is the
            // one the reading side emits.
            [MappingFactCategory.PrimaryKey] = FactSupport.Expressible,

            [MappingFactCategory.PrimaryKeyStrategy] = FactSupport.NotExpressible,
            [MappingFactCategory.ForeignKeyColumns] = FactSupport.NotExpressible,
            [MappingFactCategory.VersionColumn] = FactSupport.NotExpressible,
            [MappingFactCategory.UniqueConstraint] = FactSupport.NotExpressible,

            // Leaving a <result> out of a <resultMap autoMapping="false"> is a statement,
            // which is what makes the category expressible here and not in Dapper's POCO.
            [MappingFactCategory.TransientProperty] = FactSupport.Expressible,
        },

        // The same as Dapper's, and for the same reason: the query language is SQL, which
        // expresses every category the model carries.
        QuerySupport = new Dictionary<QueryFeature, FactSupport>
        {
            [QueryFeature.Projection] = FactSupport.Expressible,
            [QueryFeature.Filtering] = FactSupport.Expressible,
            [QueryFeature.Join] = FactSupport.Expressible,
            [QueryFeature.JoinKind] = FactSupport.Expressible,
            [QueryFeature.Aggregation] = FactSupport.Expressible,
            [QueryFeature.Grouping] = FactSupport.Expressible,
            [QueryFeature.PostAggregationFiltering] = FactSupport.Expressible,
            [QueryFeature.Ordering] = FactSupport.Expressible,
            [QueryFeature.Pagination] = FactSupport.Expressible,
            [QueryFeature.Subquery] = FactSupport.Expressible,
            [QueryFeature.SetOperation] = FactSupport.Expressible,
            [QueryFeature.QueryParameter] = FactSupport.Expressible,
        },
    };
}
