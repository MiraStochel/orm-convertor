using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Sql;
using EFCoreWrappers.Convertors;
using Model;

namespace EFCoreWrappers;

/// <summary>
/// The store type an EF Core source spells for a column - the TypeName of [Column] and the
/// argument of the fluent HasColumnType, two spellings of one fact (decision 132) - read
/// into the neutral vocabulary by one piece of code, so that the two cannot drift apart.
/// </summary>
internal static class EFCoreStoreType
{
    /// <summary>
    /// The type name read into the neutral vocabulary (decision 019): the family with the
    /// facets the name and its arguments claim, and the literal spelling on the escape path
    /// where the family is coarser or missing. A name outside the vocabulary is a record,
    /// not an exception - the family fact is missing rather than lost, and the catalog may
    /// still supply it (decision 010).
    ///
    /// Where the source declared the dialect of another database system, the name is not
    /// read at all and the record is a loss (decision 088). The literal spelling does not
    /// travel either: were it kept, a name that system knows and SQL Server does not would be
    /// copied into an artifact the descriptor says is written for SQL Server 2022. The target
    /// derives the column type from the language type instead (decision 014), or the catalog
    /// supplies it (F6) - the output is poorer, not different.
    /// </summary>
    /// <param name="spelling">How the source wrote it, for the record of a foreign dialect.</param>
    public static void Apply(
        AbstractEntityBuilder entityBuilder,
        string propertyName,
        string typeName,
        SourceSqlDialect? declaredSourceDialect,
        string spelling)
    {
        if (DatabaseTypeConvertor.FromEfCore(typeName, declaredSourceDialect) is not { } reading)
        {
            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = entityBuilder.Descriptor.Framework,
                Entity = entityBuilder.EntityMap.Entity.Name,
                Property = propertyName,
                Category = MappingFactCategory.DatabaseType,
                Reason = ForeignDialect.Reason(spelling),
            });

            return;
        }

        entityBuilder.SetPropertyDatabaseType(
            propertyName,
            reading.Type,
            reading.IsUnicode,
            reading.KeepLiteral || reading.Type is null ? typeName.Trim() : null,
            reading.Length,
            reading.Precision,
            reading.Scale);

        if (reading.Type is null)
        {
            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Incompleteness,
                Framework = entityBuilder.Descriptor.Framework,
                Entity = entityBuilder.EntityMap.Entity.Name,
                Property = propertyName,
                Category = MappingFactCategory.DatabaseType,
                Reason = $"The type '{typeName.Trim()}' has no family in the neutral vocabulary; its literal "
                    + "spelling is kept on the escape path and no family is claimed (decision 019).",
            });
        }
    }
}
