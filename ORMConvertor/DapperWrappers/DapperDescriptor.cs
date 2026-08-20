using AbstractWrappers.Descriptors;
using Model;

namespace DapperWrappers;

/// <summary>
/// Dapper executes SQL and materialises results; it carries no mapping metadata of its
/// own. Every category is therefore inexpressible and the demand it places on database
/// metadata is empty — as a target. As a source it is the opposite case: nothing arrives
/// from the input, so everything has to come from elsewhere (F6).
/// </summary>
public static class DapperDescriptor
{
    public static TargetFrameworkDescriptor Instance { get; } = new()
    {
        Framework = ORMEnum.Dapper,

        // Pinned by decision 013; the canonical table is in docs/architecture.md and a
        // test binds this value to the package the verification level loads.
        Version = "2.1.79",

        // Dapper imposes nothing on the generated class. An empty list is a statement,
        // not an omission.
        EnforcedMembers = [],

        Support = new Dictionary<MappingFactCategory, FactSupport>
        {
            [MappingFactCategory.TableName] = FactSupport.NotExpressible,
            [MappingFactCategory.SchemaName] = FactSupport.NotExpressible,
            [MappingFactCategory.ColumnName] = FactSupport.NotExpressible,
            [MappingFactCategory.DatabaseType] = FactSupport.NotExpressible,
            [MappingFactCategory.Length] = FactSupport.NotExpressible,
            [MappingFactCategory.PrecisionAndScale] = FactSupport.NotExpressible,

            // The generated class does carry C# nullability, but that comes from the
            // language type of the property, not from the nullability of the column.
            // The mapping fact itself has nowhere to go.
            [MappingFactCategory.Nullability] = FactSupport.NotExpressible,

            [MappingFactCategory.PrimaryKey] = FactSupport.NotExpressible,
            [MappingFactCategory.PrimaryKeyStrategy] = FactSupport.NotExpressible,
            [MappingFactCategory.ForeignKeyColumns] = FactSupport.NotExpressible,

            // Optimistic concurrency in Dapper is a hand-written WHERE clause, not
            // mapping metadata; the record this produces is a statement about Dapper,
            // not about the tool (decision 030).
            [MappingFactCategory.VersionColumn] = FactSupport.NotExpressible,
        },

        // SQL expresses every query category, so as a query target Dapper is the opposite of
        // what it is as a mapping target: nothing is beyond it.
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