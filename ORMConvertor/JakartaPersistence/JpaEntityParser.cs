using AbstractWrappers;
using JavaEntityParsing;
using Model;

namespace JakartaPersistence;

/// <summary>
/// Reads a JPA entity class: the shared Java structure plus the jakarta.persistence
/// annotations (decision 077). The access type follows the specification - @Access, or
/// where @Id sits - and decides which member the annotations are read from; the writer
/// then carries the facts into the builder through the same fill-only paths orm.xml uses,
/// which is how orm.xml keeps its precedence over the annotations (decision 068).
///
/// The language axis of decision 076 is applied here, before a type enters the model: a
/// wrapper type on @Id is the framework's idiom for an unassigned identifier and reads as
/// non-nullable, and a reference type reads as nullable unless the mapping states a NOT
/// NULL column - Java has one axis of nullability and JPA spells it in the mapping.
/// </summary>
public abstract class JpaEntityParser(
    AbstractEntityBuilder entityBuilder,
    JpaReadingContext context,
    JpaAnnotationReader reader) : JavaEntityParser(entityBuilder)
{
    private readonly JpaMappingWriter writer = new(entityBuilder, ConversionContentType.JavaEntity, context.DeclaredSourceDialect);

    protected override void ParseClassBody(JavaClass cls)
    {
        var access = AccessTypeOf(cls);
        var attributes = ReadAttributes(cls, access);

        var embeddedId = context.EmbeddedIdOf(cls.Name);

        if (context.IsMetadataComplete(cls.Name))
        {
            // orm.xml declared the class complete: its annotations are switched off, so only
            // the language facts travel (decision 068) - the NOT NULL a primitive implies
            // among them, being a rule of the language's types, not an annotation - and the
            // key class of an embedded id the descriptor declared, which only the class can name.
            foreach (var reading in attributes)
            {
                EmitProperty(reading, reading.Name == embeddedId || (reading.IsCollection && reading.HasInitializer) ? false : null);
                MaterializePrimitiveNotNull(reading);

                if (reading.Name == embeddedId)
                {
                    entityBuilder.AddEmbeddedPrimaryKey(reading.Name, JavaTypeConvertor.StripPackage(reading.Type));
                }
            }

            return;
        }

        var facts = reader.Read(cls, attributes);

        if (embeddedId is not null && facts.Attributes.FirstOrDefault(a => a.Name == embeddedId) is { } declared
            && declared.Kind is JpaAttributeKind.Basic or JpaAttributeKind.EmbeddedId)
        {
            declared.Kind = JpaAttributeKind.EmbeddedId;
        }

        foreach (var reading in attributes)
        {
            var attribute = facts.Attributes.First(a => a.Name == reading.Name);
            EmitProperty(reading, LanguageNullability(reading, attribute));

            // Hibernate documents that a primitive attribute yields a NOT NULL column - a
            // derivation from the stated type whose gap would travel elsewhere, so it is
            // materialized as a claim of the source (decision 067).
            if (!reading.IsNullable && attribute.Nullable is null
                && attribute.Kind is JpaAttributeKind.Basic or JpaAttributeKind.Version)
            {
                attribute.Nullable = false;
            }
        }

        writer.Write(facts);
    }

    private void MaterializePrimitiveNotNull(JavaAttributeReading reading)
    {
        if (!reading.IsNullable)
        {
            entityBuilder.SetPropertyDatabaseMapping(reading.Name, new Dictionary<string, string> { ["nullable"] = "false" });
        }
    }

    /// <summary>
    /// The nullability the model records for the property's language type (decision 077):
    /// never for a primitive, never for an identifier, never for a collection the class
    /// initializes, and for a reference type only where the mapping does not state a NOT
    /// NULL column.
    /// </summary>
    private static bool LanguageNullability(JavaAttributeReading reading, JpaAttributeFacts attribute)
    {
        if (!reading.IsNullable || attribute.Kind is JpaAttributeKind.Id or JpaAttributeKind.EmbeddedId)
        {
            return false;
        }

        if (reading.IsCollection && reading.HasInitializer)
        {
            return false;
        }

        var statedNotNull = attribute.Nullable == false || attribute.Optional == false
            || (attribute.JoinColumns.Count > 0 && attribute.JoinColumns.All(c => c.Nullable == false));

        return !statedNotNull;
    }

    /// <summary>
    /// Field access unless @Access says PROPERTY or the identifier annotation sits on a
    /// getter (Jakarta Persistence 3.2 §2.3.1). The whole class follows the one choice, and
    /// an annotation on the other member is ignored by the provider - and therefore here.
    /// </summary>
    private static JavaMemberAccess AccessTypeOf(JavaClass cls)
    {
        var access = cls.Annotations.FirstOrDefault(a => a.SimpleName == "Access")?["value"]?.SimpleName;
        if (access is not null)
        {
            return access == "PROPERTY" ? JavaMemberAccess.Property : JavaMemberAccess.Field;
        }

        bool onField = cls.Fields.Any(f => f.Annotations.Any(IsIdentifierAnnotation));
        bool onGetter = cls.Methods.Any(m => m.Annotations.Any(IsIdentifierAnnotation));

        return onGetter && !onField ? JavaMemberAccess.Property : JavaMemberAccess.Field;
    }

    private static bool IsIdentifierAnnotation(JavaAnnotation annotation)
        => annotation.SimpleName is "Id" or "EmbeddedId";
}
