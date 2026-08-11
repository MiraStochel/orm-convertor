using AbstractWrappers.Descriptors;
using Model;

namespace EFCoreWrappers;

/// <summary>
/// EF Core expresses mapping through data annotations. The one thing it forces onto the
/// artifact is the keyless marker: without it a property named Id or {TypeName}Id becomes
/// the primary key by convention, so an entity the model holds without a key would
/// silently acquire one.
/// </summary>
public static class EFCoreDescriptor
{
    public static TargetFrameworkDescriptor Instance { get; } = new()
    {
        Framework = ORMEnum.EFCore,

        EnforcedMembers =
        [
            new EnforcedMember
            {
                Name = "keyless marker on an entity without a key",
                Condition = EnforcedMemberCondition.NoPrimaryKey,
                Marker = "[Keyless]",
                Reason = "EF Core derives a primary key by convention from a property named "
                       + "Id or {TypeName}Id. Without [Keyless] an entity the model holds "
                       + "without a key would gain one that nobody stated.",
            },
        ],

        Support = new Dictionary<MappingFactCategory, FactSupport>
        {
            [MappingFactCategory.TableName] = FactSupport.Expressible,      // [Table]
            [MappingFactCategory.SchemaName] = FactSupport.Expressible,     // [Table(Schema = …)]
            [MappingFactCategory.ColumnName] = FactSupport.Expressible,     // [Column]
            [MappingFactCategory.DatabaseType] = FactSupport.Expressible,   // [Column(TypeName = …)]
            [MappingFactCategory.Length] = FactSupport.Expressible,         // [MaxLength]
            [MappingFactCategory.PrecisionAndScale] = FactSupport.Expressible, // [Precision]
            [MappingFactCategory.Nullability] = FactSupport.Expressible,    // [Required]

            // Expressible rather than Required: an entity without a key still produces a
            // usable artifact, as a keyless type. NHibernate has no such fallback, which
            // is where the two frameworks part company.
            [MappingFactCategory.PrimaryKey] = FactSupport.Expressible,

            // [DatabaseGenerated] covers Identity, None and Computed. Sequence, HiLo and
            // the rest are fluent-only, so those individual values fall to diagnostics —
            // the category as a whole is still expressible.
            [MappingFactCategory.PrimaryKeyStrategy] = FactSupport.Expressible,

            [MappingFactCategory.ForeignKeyColumns] = FactSupport.Expressible, // [ForeignKey]
        },
    };
}