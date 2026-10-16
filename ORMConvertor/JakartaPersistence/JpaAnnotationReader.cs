using AbstractWrappers;
using JavaEntityParsing;

namespace JakartaPersistence;

/// <summary>
/// Reads the jakarta.persistence annotations of a Java class into <see cref="JpaEntityFacts"/>
/// (decision 077). The subset is the table of that decision; every annotation outside it
/// lands in the Unread lists, so that the writer can say what was dropped instead of the
/// annotation vanishing without a trace (decision 048). Vendor annotations go through
/// <see cref="ReadVendorAnnotation"/>, which an implementation's reader overrides - the
/// third hook of decision 076.
/// </summary>
public class JpaAnnotationReader
{
    private const string Jakarta = "jakarta.persistence";

    /// <summary>
    /// Reads a class. The attributes come with the annotations the access type selected
    /// (field or getter), so the reader itself never asks where an annotation sits.
    /// </summary>
    public JpaEntityFacts Read(JavaClass cls, IReadOnlyList<JavaAttributeReading> attributes)
    {
        ArgumentNullException.ThrowIfNull(cls);
        ArgumentNullException.ThrowIfNull(attributes);

        var facts = new JpaEntityFacts { ClassName = cls.Name };

        foreach (var annotation in cls.Annotations)
        {
            ReadClassAnnotation(annotation, facts);
        }

        foreach (var attribute in attributes)
        {
            var attributeFacts = new JpaAttributeFacts { Name = attribute.Name, TypeText = attribute.Type };

            foreach (var annotation in attribute.Annotations)
            {
                ReadAttributeAnnotation(annotation, attributeFacts);
            }

            if (attribute.HasTransientModifier)
            {
                attributeFacts.Kind = JpaAttributeKind.Transient;
            }

            facts.Attributes.Add(attributeFacts);
        }

        return facts;
    }

    /// <summary>
    /// Where an annotation stands, as the facts carry it (decision 125): recorded under the
    /// annotation's simple name before the annotation is interpreted, so that a vendor's
    /// annotation and an unread one are placed as well as the specification's.
    /// </summary>
    private static void Place(JavaAnnotation annotation, Dictionary<string, TextRange> positions)
    {
        if (annotation.End > annotation.Start)
        {
            positions.TryAdd(annotation.SimpleName, TextRange.Between(annotation.Start, annotation.End));
        }
    }

    private void ReadClassAnnotation(JavaAnnotation annotation, JpaEntityFacts facts)
    {
        Place(annotation, facts.Positions);

        if (!IsJakarta(annotation))
        {
            if (!ReadVendorAnnotation(annotation, facts, null))
            {
                facts.Unread.Add(Spelling(annotation));
            }

            return;
        }

        switch (annotation.SimpleName)
        {
            case "Entity":
            case "Embeddable":
            case "Access":
                // @Entity names the entity for JPQL only and the IR names entities by class;
                // @Embeddable marks a class the key dissolution takes care of; @Access is
                // read by the parser before the attributes are collected.
                break;

            case "Table":
                facts.Table = annotation.String("name") ?? facts.Table;
                facts.Schema = annotation.String("schema") ?? facts.Schema;

                // A catalog is a level of naming the model does not have, and a non-unique
                // index is a performance artifact outside the mapping ([Index] without
                // IsUnique has the same record on the EF Core side, decision 055).
                if (annotation.String("catalog") is not null)
                {
                    facts.Unread.Add("Table(catalog)");
                }

                if (annotation["indexes"] is not null)
                {
                    facts.Unread.Add("Table(indexes)");
                }

                foreach (var constraint in (annotation["uniqueConstraints"]?.AsList ?? []))
                {
                    if (constraint.Annotation is { } unique)
                    {
                        var columns = (unique["columnNames"]?.AsList ?? [])
                            .Where(v => v.Kind == JavaAnnotationValueKind.String)
                            .Select(v => v.Text)
                            .ToList();

                        if (columns.Count > 0)
                        {
                            facts.UniqueConstraints.Add(new JpaUniqueConstraintFacts(unique.String("name"), columns));
                        }
                    }
                }

                break;

            case "MappedSuperclass":
                // Not an Unread entry: the class is not an entity at all, which is a
                // heavier statement than an annotation the model cannot keep, and the
                // writer says so in its own words.
                facts.IsMappedSuperclass = true;
                break;

            case "IdClass":
                facts.IdClass = annotation["value"] is { Kind: JavaAnnotationValueKind.ClassLiteral } literal
                    ? literal.SimpleName
                    : null;
                break;

            case "SequenceGenerator":
            case "TableGenerator":
                if (ReadGenerator(annotation) is { } generator)
                {
                    facts.Generators[generator.Name] = generator;
                }

                break;

            case "SequenceGenerators":
            case "TableGenerators":
                foreach (var nested in (annotation["value"]?.AsList ?? []).Select(v => v.Annotation).OfType<JavaAnnotation>())
                {
                    if (ReadGenerator(nested) is { } each)
                    {
                        facts.Generators[each.Name] = each;
                    }
                }

                break;

            default:
                // A bare name the specification does not have may be a vendor's, imported.
                if (annotation.Qualifier is not null || !ReadVendorAnnotation(annotation, facts, null))
                {
                    facts.Unread.Add(Spelling(annotation));
                }

                break;
        }
    }

    private void ReadAttributeAnnotation(JavaAnnotation annotation, JpaAttributeFacts facts)
    {
        Place(annotation, facts.Positions);

        if (!IsJakarta(annotation))
        {
            if (!ReadVendorAnnotation(annotation, null, facts))
            {
                facts.Unread.Add(Spelling(annotation));
            }

            return;
        }

        switch (annotation.SimpleName)
        {
            case "Id":
                // On a relation attribute the annotation is the derived identity (3.2 §2.4.1):
                // the attribute stays a relation and the key is told apart by the flag,
                // whichever of the two annotations the source wrote first.
                if (IsRelation(facts.Kind))
                {
                    facts.IsDerivedIdentity = true;
                }
                else
                {
                    facts.Kind = JpaAttributeKind.Id;
                }

                break;

            case "EmbeddedId":
                facts.Kind = JpaAttributeKind.EmbeddedId;
                break;

            case "Version":
                facts.Kind = JpaAttributeKind.Version;
                break;

            case "Transient":
                facts.Kind = JpaAttributeKind.Transient;
                break;

            case "Basic":
                facts.Nullable = annotation.Boolean("optional") ?? facts.Nullable;
                break;

            case "Column":
                facts.ColumnName = annotation.String("name") ?? facts.ColumnName;
                facts.Length = annotation.Int("length") ?? facts.Length;
                facts.Precision = annotation.Int("precision") ?? facts.Precision;
                facts.SecondPrecision = annotation.Int("secondPrecision") ?? facts.SecondPrecision;
                facts.Scale = annotation.Int("scale") ?? facts.Scale;
                facts.Nullable = annotation.Boolean("nullable") ?? facts.Nullable;
                facts.Unique |= annotation.Boolean("unique") == true;
                facts.ColumnDefinition = annotation.String("columnDefinition") ?? facts.ColumnDefinition;

                // A column in a secondary table is excluded area 2; the two write flags are
                // judged by the writer against the relations of the class (ReadOnlyFlags).
                if (annotation.String("table") is not null)
                {
                    facts.Unread.Add("Column(table)");
                }

                if (annotation.Boolean("insertable") == false)
                {
                    facts.ReadOnlyFlags.Add("Column(insertable = false)");
                }

                if (annotation.Boolean("updatable") == false)
                {
                    facts.ReadOnlyFlags.Add("Column(updatable = false)");
                }

                break;

            case "GeneratedValue":
                facts.Generated = true;
                facts.GenerationStrategy = annotation["strategy"]?.SimpleName;
                facts.GeneratorName = annotation.String("generator");
                break;

            case "SequenceGenerator":
            case "TableGenerator":
                facts.Generator = ReadGenerator(annotation);
                break;

            case "ManyToOne":
            case "OneToOne":
            case "OneToMany":
            case "ManyToMany":
                if (facts.Kind == JpaAttributeKind.Id)
                {
                    facts.IsDerivedIdentity = true;
                }

                facts.Kind = annotation.SimpleName switch
                {
                    "ManyToOne" => JpaAttributeKind.ManyToOne,
                    "OneToOne" => JpaAttributeKind.OneToOne,
                    "OneToMany" => JpaAttributeKind.OneToMany,
                    _ => JpaAttributeKind.ManyToMany,
                };
                facts.TargetEntity = annotation["targetEntity"] is { Kind: JavaAnnotationValueKind.ClassLiteral } target
                    ? target.SimpleName
                    : facts.TargetEntity;
                facts.MappedBy = annotation.String("mappedBy") ?? facts.MappedBy;
                facts.Optional = annotation.Boolean("optional") ?? facts.Optional;

                // Cascading and orphan removal have no home in the model (the NHibernate
                // parser reports the cascade attribute the same way); fetching is a loading
                // strategy the paper keeps out of the representation (§5.4) and is not reported.
                if (annotation["cascade"] is not null)
                {
                    facts.Unread.Add($"{annotation.SimpleName}(cascade)");
                }

                if (annotation.Boolean("orphanRemoval") == true)
                {
                    facts.Unread.Add($"{annotation.SimpleName}(orphanRemoval)");
                }

                if (facts.Kind is JpaAttributeKind.ManyToOne or JpaAttributeKind.OneToOne && IsLazy(annotation))
                {
                    ReadLazyReference(annotation, facts);
                }

                break;

            case "JoinColumn":
                facts.JoinColumns.Add(ReadJoinColumn(annotation));
                break;

            case "JoinColumns":
                foreach (var nested in (annotation["value"]?.AsList ?? []).Select(v => v.Annotation).OfType<JavaAnnotation>())
                {
                    facts.JoinColumns.Add(ReadJoinColumn(nested));
                }

                break;

            case "JoinTable":
                facts.JoinTable = new JpaJoinTableFacts(
                    annotation.String("name"),
                    annotation.String("schema"),
                    [.. (annotation["joinColumns"]?.AsList ?? []).Select(v => v.Annotation).OfType<JavaAnnotation>().Select(ReadJoinColumn)],
                    [.. (annotation["inverseJoinColumns"]?.AsList ?? []).Select(v => v.Annotation).OfType<JavaAnnotation>().Select(ReadJoinColumn)]);
                break;

            case "MapsId":
            case "PrimaryKeyJoinColumn":
                facts.MapsId = true;
                break;

            default:
                if (annotation.Qualifier is not null || !ReadVendorAnnotation(annotation, null, facts))
                {
                    facts.Unread.Add(Spelling(annotation));
                }

                break;
        }
    }

    /// <summary>
    /// An annotation outside jakarta.persistence. The base knows none: it returns false
    /// and the annotation is reported as unread. An implementation's reader recognizes its
    /// own - Hibernate's @Nationalized - and fills the facts.
    /// </summary>
    protected virtual bool ReadVendorAnnotation(JavaAnnotation annotation, JpaEntityFacts? entity, JpaAttributeFacts? attribute)
        => false;

    /// <summary>
    /// fetch = LAZY on a single-valued reference. The loading strategy is kept out of the
    /// representation by the paper (§5.4), so the base says nothing at all, which is what
    /// decision 077 settled for Hibernate: there the annotation does what it promises and
    /// leaving it behind is the ordinary price of the pivot. An implementation where the
    /// same line is inert unless the consumer project weaves its bytecode says so here
    /// (decision 080) - it is a fact about the source, not a strategy entering the model.
    /// </summary>
    protected virtual void ReadLazyReference(JavaAnnotation annotation, JpaAttributeFacts facts)
    {
    }

    /// <summary>Whether the relation annotation states fetch = LAZY, however it qualifies the constant.</summary>
    private static bool IsLazy(JavaAnnotation annotation)
        => annotation["fetch"] is { Kind: JavaAnnotationValueKind.Name } fetch
            && string.Equals(fetch.SimpleName, "LAZY", StringComparison.Ordinal);

    private static JpaGeneratorFacts? ReadGenerator(JavaAnnotation annotation)
    {
        var name = annotation.String("name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new JpaGeneratorFacts(
            IsTable: annotation.SimpleName == "TableGenerator",
            name,
            SequenceName: annotation.String("sequenceName"),
            Schema: annotation.String("schema"),
            AllocationSize: annotation.Int("allocationSize"),
            InitialValue: annotation.Int("initialValue"),
            Table: annotation.String("table"),
            PkColumnName: annotation.String("pkColumnName"),
            ValueColumnName: annotation.String("valueColumnName"),
            PkColumnValue: annotation.String("pkColumnValue"));
    }

    private static JpaJoinColumnFacts ReadJoinColumn(JavaAnnotation annotation)
    {
        var unread = new List<string>();
        var readOnly = new List<string>();

        if (annotation.String("table") is not null)
        {
            unread.Add("JoinColumn(table)");
        }

        if (annotation.String("columnDefinition") is not null)
        {
            unread.Add("JoinColumn(columnDefinition)");
        }

        if (annotation.Boolean("insertable") == false)
        {
            readOnly.Add("JoinColumn(insertable = false)");
        }

        if (annotation.Boolean("updatable") == false)
        {
            readOnly.Add("JoinColumn(updatable = false)");
        }

        return new(
            annotation.String("name"),
            annotation.String("referencedColumnName"),
            annotation.Boolean("nullable"),
            annotation.Boolean("unique") == true,
            unread,
            readOnly);
    }

    /// <summary>
    /// Whether the annotation is one of the specification's: written bare (imported) or
    /// with the jakarta.persistence qualifier. A bare name shared with a vendor package
    /// is taken for the specification's - the import list would settle it, and does not
    /// need to for the subset read here.
    /// </summary>
    private static bool IsJakarta(JavaAnnotation annotation)
        => annotation.Qualifier is null || annotation.Qualifier == Jakarta;

    private static string Spelling(JavaAnnotation annotation) => "@" + annotation.Name;

    private static bool IsRelation(JpaAttributeKind kind)
        => kind is JpaAttributeKind.ManyToOne or JpaAttributeKind.OneToOne or JpaAttributeKind.OneToMany or JpaAttributeKind.ManyToMany;
}
