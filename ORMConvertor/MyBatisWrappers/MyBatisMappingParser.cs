using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using JavaEntityParsing;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace MyBatisWrappers;

/// <summary>
/// What the two mapping parsers of MyBatis share: the writing of one mapping fact into the
/// builder. The facts arrive in two spellings - the elements of a &lt;resultMap&gt; and the
/// @Results of a mapper method - and mean the same thing, so they are written in one place,
/// the way the JPA layer writes one set of facts for annotations and orm.xml alike
/// (decision 077).
///
/// What is written is decided by decision 084 and is narrower than what MyBatis says: a
/// &lt;resultMap&gt; is the pairs of column and property, and its &lt;collection&gt; and
/// &lt;association&gt; are navigation <em>without</em> columns. The key is not read at all -
/// &lt;id&gt; marks the identity of a result row, not the key of a table, and materializing
/// it would turn a silent supplement from the catalog into a conflict against the real key -
/// and neither is the foreign key, which lives in a join condition, which is a query.
/// </summary>
public abstract class MyBatisMappingParser(AbstractEntityBuilder entityBuilder) : IEntityParser
{
    protected readonly AbstractEntityBuilder entityBuilder = entityBuilder;

    /// <summary>
    /// The limits this parser reads its input under (decision 092). The orchestration sets
    /// them on every parser it creates; one constructed by hand - in a test - runs under the
    /// default, which is the cap the application uses unless its operator moved it.
    /// </summary>
    public ParseLimits Limits { get; set; } = ParseLimits.Default;

    public abstract bool CanParse(ConversionContentType contentType);

    public abstract IReadOnlyCollection<EntityMap> Parse(string source);

    /// <summary>The artifact the records of this parser name, which its subclass states.</summary>
    protected abstract ConversionContentType Artifact { get; }

    /// <summary>
    /// The entity a type name denotes, created where no unit has declared it yet. The name
    /// is what identifies it (decision 001), so a MyBatis alias - type="Author" resolved
    /// through the &lt;typeAliases&gt; of a configuration the tool does not read - and a
    /// fully qualified name reach the same entity; the qualified form brings its package
    /// along, the alias takes the one the domain class declared.
    /// </summary>
    protected EntityMap Entity(string typeName)
    {
        var simple = MyBatisTypeNames.SimpleName(typeName);
        var package = MyBatisTypeNames.PackageOf(typeName);

        var existing = entityBuilder.EntityMaps.FirstOrDefault(em =>
            string.Equals(em.Entity.Name, simple, StringComparison.Ordinal));

        if (existing is not null)
        {
            entityBuilder.EntityMap = existing;
        }
        else
        {
            entityBuilder.BeginEntity();
            entityBuilder.AddClassHeader("public", simple);
        }

        if (package is not null && string.IsNullOrEmpty(entityBuilder.EntityMap.Entity.Namespace))
        {
            entityBuilder.AddNamespace(package);
        }

        return entityBuilder.EntityMap;
    }

    /// <summary>
    /// One pair of column and property, with the two facts a mapper may state beside it:
    /// the Java type of the property, and the JDBC family of the column. Whether the source
    /// marked it &lt;id&gt; makes no difference to what is written - that is the whole point
    /// of decision 084's reading of &lt;id&gt; - and is reported by the caller instead.
    /// </summary>
    protected void WriteColumn(EntityMap entityMap, string property, string? column, string? javaType, string? jdbcType)
    {
        entityBuilder.EntityMap = entityMap;

        if (string.IsNullOrWhiteSpace(property))
        {
            Report(ConversionRecordKind.Loss, entityMap, null, MappingFactCategory.ColumnName,
                $"A result mapping of '{entityMap.Entity.Name}' names the column '{column}' and no property; it was dropped.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(column))
        {
            entityBuilder.SetPropertyDatabaseMapping(property, new Dictionary<string, string> { ["columnname"] = column });
        }

        if (!string.IsNullOrWhiteSpace(jdbcType))
        {
            var (type, isUnicode) = MyBatisJdbcType.Read(jdbcType);

            if (type is not null)
            {
                entityBuilder.SetPropertyDatabaseType(property, type, isUnicode);
            }
            else
            {
                Report(ConversionRecordKind.Loss, entityMap, property, MappingFactCategory.DatabaseType,
                    $"The mapping states jdbcType '{jdbcType}', which has no family in the neutral type vocabulary; it was dropped.");
            }
        }

        if (!string.IsNullOrWhiteSpace(javaType))
        {
            WriteJavaType(entityMap, property, javaType);
        }
    }

    /// <summary>
    /// The language type a mapping states for a property. The domain class is read first
    /// (decision 017), so this usually finds the type already there and only compares; where
    /// no class declared the property, the mapping is the only source of it.
    /// </summary>
    private void WriteJavaType(EntityMap entityMap, string property, string javaType)
    {
        var declared = entityMap.Entity.Properties.FirstOrDefault(p => p.Name == property);
        if (declared is null)
        {
            return;
        }

        var stated = JavaTypeConvertor.FromString(javaType);

        if (declared.Type is null)
        {
            declared.Type = stated;
            return;
        }

        // A navigation is claimed by the relation, whose reference type is richer than the
        // written name; comparing the two would report a conflict about one fact.
        if (declared.Type.Category is LangTypeCategory.Reference
            || (declared.Type.Category is LangTypeCategory.Collection
                && declared.Type.ElementType?.Category is LangTypeCategory.Reference))
        {
            return;
        }

        if (JavaTypeConvertor.ToString(declared.Type) != JavaTypeConvertor.ToString(stated))
        {
            Report(ConversionRecordKind.Conflict, entityMap, property, null,
                $"An earlier source declares the property as '{JavaTypeConvertor.ToString(declared.Type)}', the mapping states "
                + $"javaType '{javaType}'. A fact read earlier is never overwritten by a later input source (decision 017), so the "
                + "first value is kept.");
        }
    }

    /// <summary>
    /// A navigation the mapper states, without the columns behind it. The cardinality and
    /// the role follow from the shape of the navigation and from the relational model, not
    /// from any convention of MyBatis, which knows nothing of ownership: a collection is
    /// one-to-many and the side holding a collection never holds the physical foreign key,
    /// so it is Inverse; a reference is many-to-one and is Owning. The columns are left to
    /// the catalog completion phase, which can fill them into an existing relation (F6,
    /// decision 015).
    /// </summary>
    protected void WriteNavigation(EntityMap entityMap, string property, string target, bool isCollection)
    {
        entityBuilder.EntityMap = entityMap;

        if (entityMap.Relations.Any(r =>
                string.Equals(r.SourceNavigationProperty, property, StringComparison.Ordinal)
                && string.Equals(r.TargetEntity, target, StringComparison.Ordinal)))
        {
            // A second result mapping over the same class states the same navigation again;
            // the same statement twice is not an event (decision 017).
            return;
        }

        entityBuilder.AddForeignKey(
            isCollection ? Cardinality.OneToMany : Cardinality.ManyToOne,
            property,
            target,
            isCollection ? RelationRole.Inverse : RelationRole.Owning);
    }

    /// <summary>
    /// The entity a navigation points at when the mapper does not name it. The annotated
    /// form never does - @Many and @One take the element type from the declaration of the
    /// property - so it is read out of the domain class, which the entity pass read first.
    /// </summary>
    protected string? TargetOfNavigation(EntityMap entityMap, string property)
    {
        var declared = entityMap.Entity.Properties.FirstOrDefault(p => p.Name == property)?.Type;

        return declared?.Category switch
        {
            LangTypeCategory.Collection => Named(declared.ElementType),
            _ => Named(declared),
        };

        static string? Named(LangType? type) => type?.Category switch
        {
            LangTypeCategory.Reference => type.TargetEntity,
            LangTypeCategory.Unknown => MyBatisTypeNames.SimpleName(type.SourceName!),
            _ => null,
        };
    }

    /// <summary>
    /// The identity record of decision 084. MyBatis marks with &lt;id&gt; the column by which
    /// two rows are recognized as one object, which need be no key at all - the table may
    /// have none and MyBatis never asks - so the key is left to the catalog and the user is
    /// told where to get it (F11). No record is written where the source marked nothing: a
    /// record every MyBatis conversion would carry states nothing (decisions 010 and 028).
    /// </summary>
    protected void ReportIdentity(EntityMap entityMap, IReadOnlyList<string> columns)
    {
        if (columns.Count == 0)
        {
            return;
        }

        Report(ConversionRecordKind.Incompleteness, entityMap, null, MappingFactCategory.PrimaryKey,
            $"The source marks {string.Join(", ", columns)} as the identity of a result row, which is what MyBatis means by <id> - "
            + "not the primary key of the table, which MyBatis never states; the key was therefore not read and a database catalog "
            + "can supply it (F6).");
    }

    protected void Report(
        ConversionRecordKind kind,
        EntityMap? entityMap,
        string? property,
        MappingFactCategory? category,
        string reason)
        => entityBuilder.Report(new ConversionRecord
        {
            Kind = kind,
            Framework = entityBuilder.Descriptor.Framework,
            Artifact = Artifact,
            Entity = entityMap?.Entity.Name,
            Property = property,
            Category = category,
            Reason = reason,
        });
}
