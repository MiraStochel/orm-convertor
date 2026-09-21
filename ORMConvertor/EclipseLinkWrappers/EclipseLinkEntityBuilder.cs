using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using JakartaPersistence;
using Model;
using Model.AbstractRepresentation;

namespace EclipseLinkWrappers;

/// <summary>
/// Emits an EclipseLink entity: the shared JPA output with EclipseLink's profile
/// (decision 080). The class states only what is EclipseLink's own, which is the second
/// hook of decision 076 - national character data. EclipseLink has no annotation for it
/// at any level, so the fact goes where the only place is: the literal type of the column.
/// Everything else the artifact needs is already explicit in the shared builder, and that
/// is what makes this wrapper thin - the implicit upper-case name of this implementation
/// never reaches the output, because no name is ever left implicit.
/// </summary>
public sealed class EclipseLinkEntityBuilder : AbstractJpaEntityBuilder
{
    public override TargetFrameworkDescriptor Descriptor => EclipseLinkDescriptor.Instance;

    protected override JpaImplementationProfile Profile => EclipseLinkDescriptor.Profile;

    protected override void AppendNationalization(
        EntityMap entityMap, PropertyMap propertyMap, List<string> arguments, List<string> annotations)
    {
        // A literal type the source stated is already in the arguments and wins over any
        // name derived from the family (decision 052): it is what the source claimed, and
        // it is also where the unicode facet was read from in the first place.
        if (propertyMap.SourceSqlType is not null)
        {
            return;
        }

        var definition = JpaSqlTypeWriting.NationalizedColumnDefinition(
            Descriptor.Dialect, propertyMap, out var familyFromLanguageType, out var lengthFromDefault);

        if (definition is null)
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.JavaEntity,
                Entity = entityMap.Entity.Name,
                Property = propertyMap.Property.Name,
                Category = MappingFactCategory.DatabaseType,
                Reason = "The source states national character data, which EclipseLink can express only as a literal column "
                    + $"type, and {(propertyMap.Type is { } family ? $"the family {family}" : "a property whose language type is not character data")} "
                    + "has no national variant to write; the facet is dropped (decision 080).",
            });

            return;
        }

        arguments.Add($"columnDefinition = \"{definition}\"");

        // The commonest source of the facet - @Nationalized on a String - states no database
        // family at all, so the artifact names a type the source never spelled. That is a
        // statement of the target and is recorded as one; where the family was stated, the
        // name is only its one spelling here and carries nothing, as it does for EF Core.
        if (familyFromLanguageType)
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Convention,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.JavaEntity,
                Entity = entityMap.Entity.Name,
                Property = propertyMap.Property.Name,
                Category = MappingFactCategory.DatabaseType,
                Reason = $"The source states national character data over a column with no stated type; the artifact writes "
                    + $"columnDefinition = \"{definition}\", the character type its language type implies, because EclipseLink "
                    + "has no annotation for the facet and its own default would be the non-national column the source ruled "
                    + "out (decision 080).",
            });
        }

        // The type name itself carries no record: it is the one spelling this target has
        // for a fact the source stated, the same way the EF Core builder writes a type name
        // derived from the family. The length inside it is another matter - a literal type
        // overrides the length beside it, so an unstated length becomes a claim.
        if (lengthFromDefault)
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Convention,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.JavaEntity,
                Entity = entityMap.Entity.Name,
                Property = propertyMap.Property.Name,
                Category = MappingFactCategory.Length,
                Reason = $"No length was stated and the literal type that carries the unicode facet needs one, so the "
                    + $"artifact writes {JpaSqlTypeWriting.DefaultLength}, the length Jakarta Persistence gives @Column "
                    + "when nobody states it (decision 080).",
            });
        }
    }
}
