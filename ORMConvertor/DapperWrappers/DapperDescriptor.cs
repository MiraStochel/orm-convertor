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
        },
    };
}