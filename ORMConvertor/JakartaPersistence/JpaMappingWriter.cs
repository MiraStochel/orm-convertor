using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using JavaEntityParsing;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace JakartaPersistence;

/// <summary>
/// Carries <see cref="JpaEntityFacts"/> into the entity builder - the one place both
/// readers of a JPA source end at (decision 077). Every write goes through the builder's
/// fill-only paths, so the precedence between orm.xml and the annotations is the
/// builder's rule (decisions 017 and 068), not this writer's.
/// </summary>
public sealed class JpaMappingWriter(AbstractEntityBuilder entityBuilder, ConversionContentType artifact)
{
    /// <summary>Writes the facts of the current entity of the builder.</summary>
    public void Write(JpaEntityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (!string.IsNullOrWhiteSpace(facts.Table))
        {
            entityBuilder.AddTable(facts.Table);
        }

        if (!string.IsNullOrWhiteSpace(facts.Schema))
        {
            entityBuilder.AddSchema(facts.Schema);
        }

        foreach (var attribute in facts.Attributes.Where(a => a.Kind == JpaAttributeKind.Transient))
        {
            entityBuilder.MarkTransient(attribute.Name);
            ReportUnread(attribute);
        }

        var singles = new List<string>();

        foreach (var attribute in facts.Attributes.Where(a => a.Kind is JpaAttributeKind.Basic or JpaAttributeKind.Id or JpaAttributeKind.Version))
        {
            WriteColumn(attribute);

            if (attribute.Unique)
            {
                singles.Add(attribute.Name);
            }

            ReportUnread(attribute);
        }

        WriteKey(facts);

        foreach (var attribute in facts.Attributes.Where(a => a.Kind is JpaAttributeKind.ManyToOne or JpaAttributeKind.OneToOne or JpaAttributeKind.OneToMany or JpaAttributeKind.ManyToMany))
        {
            WriteRelation(facts, attribute);
            ReportUnread(attribute);
        }

        foreach (var property in singles)
        {
            entityBuilder.AddUniqueConstraint(null, [property]);
        }

        foreach (var constraint in facts.UniqueConstraints)
        {
            WriteUniqueConstraint(facts, constraint);
        }

        foreach (var unread in facts.Unread)
        {
            Report(ConversionRecordKind.Loss, null, null,
                $"The class annotation {unread} has no counterpart in the intermediate representation and was dropped.");
        }
    }

    private void WriteColumn(JpaAttributeFacts attribute)
    {
        var dbProps = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(attribute.ColumnName))
        {
            dbProps["column"] = attribute.ColumnName;
        }

        if (attribute.Length is { } length)
        {
            dbProps["length"] = length.ToString();
        }

        WritePrecision(attribute, dbProps);

        if (attribute.Scale is { } scale)
        {
            dbProps["scale"] = scale.ToString();
        }

        if (attribute.Nullable is { } nullable)
        {
            dbProps["nullable"] = nullable ? "true" : "false";
        }

        if (attribute.Kind == JpaAttributeKind.Version)
        {
            dbProps["version"] = "true";
        }

        // Called even with nothing to record: an attribute orm.xml names before the class
        // is read has to exist in the model before anything can attach to it.
        entityBuilder.SetPropertyDatabaseMapping(attribute.Name, dbProps);

        if (attribute.Nationalized is { } unicode)
        {
            entityBuilder.SetPropertyDatabaseType(attribute.Name, null, isUnicode: unicode);
        }

        if (!string.IsNullOrWhiteSpace(attribute.ColumnDefinition))
        {
            var reading = JpaSqlTypeReading.FromColumnDefinition(attribute.ColumnDefinition);

            entityBuilder.SetPropertyDatabaseType(
                attribute.Name,
                reading.Type,
                reading.IsUnicode,
                reading.KeepLiteral || reading.Type is null ? attribute.ColumnDefinition.Trim() : null,
                reading.Length,
                reading.Precision,
                reading.Scale);

            if (reading.Type is null)
            {
                Report(ConversionRecordKind.Incompleteness, attribute.Name, MappingFactCategory.DatabaseType,
                    $"The type '{attribute.ColumnDefinition.Trim()}' has no family in the neutral vocabulary; its literal "
                    + "spelling is kept on the escape path and no family is claimed (decision 019).");
            }
        }
    }

    /// <summary>
    /// Which of @Column's two precision attributes fills the one Precision facet of the
    /// model (decision 079): secondPrecision on a time or timestamp column, precision
    /// elsewhere. The other one is refused rather than read, because the value never
    /// reached the column in the source framework either and reading it would put a column
    /// in the model that the source never had. The refusal is asked only where the reader
    /// knows what the column is - orm.xml is read before the class (decision 068) and
    /// declares no Java type, so from there precision fills the facet as it always did.
    /// </summary>
    private void WritePrecision(JpaAttributeFacts attribute, Dictionary<string, string> dbProps)
    {
        var kind = JpaColumnPrecision.Classify(
            string.IsNullOrWhiteSpace(attribute.ColumnDefinition)
                ? null
                : JpaSqlTypeReading.FromColumnDefinition(attribute.ColumnDefinition).Type,
            attribute.TypeText);

        // secondPrecision exists for no column but a time or timestamp one, so spelling it
        // is itself a statement about the column where nothing else says what it is.
        var temporal = kind == JpaPrecisionKind.FractionalSeconds
            || (kind == JpaPrecisionKind.Unknown && attribute.SecondPrecision is not null);

        if (temporal)
        {
            if (attribute.SecondPrecision is { } seconds)
            {
                dbProps["precision"] = seconds.ToString();
            }

            if (attribute.Precision is { } ignored)
            {
                Report(ConversionRecordKind.Loss, attribute.Name, MappingFactCategory.PrecisionAndScale,
                    $"@Column(precision = {ignored}) stands on a time or timestamp attribute, where Jakarta "
                    + "Persistence 3.2 gives precision to a decimal column only; the value does not reach the "
                    + "column in the source framework either, so it is not read as the fractional-second "
                    + "precision - that is secondPrecision (decision 079).");
            }

            return;
        }

        if (attribute.Precision is { } precision)
        {
            dbProps["precision"] = precision.ToString();
        }

        if (kind != JpaPrecisionKind.Unknown && attribute.SecondPrecision is { } unusable)
        {
            Report(ConversionRecordKind.Loss, attribute.Name, MappingFactCategory.PrecisionAndScale,
                $"@Column(secondPrecision = {unusable}) stands on an attribute that is not a time or timestamp "
                + "column, where Jakarta Persistence 3.2 gives the attribute no meaning; it is dropped "
                + "(decision 079).");
        }
    }

    private void WriteKey(JpaEntityFacts facts)
    {
        var embedded = facts.Attributes.FirstOrDefault(a => a.Kind == JpaAttributeKind.EmbeddedId);
        if (embedded is not null)
        {
            var keyClass = embedded.TypeText is null ? null : JavaTypeConvertor.StripPackage(embedded.TypeText);
            if (string.IsNullOrWhiteSpace(keyClass))
            {
                Report(ConversionRecordKind.Incompleteness, embedded.Name, MappingFactCategory.PrimaryKey,
                    "The embedded id names no key class, so the key parts cannot be taken from anywhere.");
            }
            else
            {
                // The parts are the members of the class, which may lie in another unit;
                // the claim waits and the dissolution phase materializes it (decision 077).
                entityBuilder.AddEmbeddedPrimaryKey(embedded.Name, keyClass);
            }

            ReportUnread(embedded);
            return;
        }

        var ids = facts.Attributes.Where(a => a.Kind == JpaAttributeKind.Id).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var composite = ids.Count > 1;
        var parts = ids.Select((id, index) => (id.Name, index + 1, StrategyOf(id))).ToList();

        entityBuilder.AddPrimaryKey(
            parts,
            composite && facts.IdClass is not null ? new SourceKeyClass(facts.IdClass, KeyClassForm.Mirrored) : null);

        foreach (var id in ids)
        {
            WriteStrategyDetails(facts, id);
        }
    }

    /// <summary>
    /// @GeneratedValue as the vocabulary of decision 011 reads it: AUTO - and the bare
    /// annotation, which means AUTO - is the framework's choice for the dialect, and what
    /// Hibernate or EclipseLink make of it is their default, not the source's claim
    /// (decision 067). No @GeneratedValue is nobody saying how the value arises.
    /// </summary>
    private static PrimaryKeyStrategy StrategyOf(JpaAttributeFacts id) => id.Generated
        ? id.GenerationStrategy switch
        {
            null or "AUTO" => PrimaryKeyStrategy.Auto,
            "IDENTITY" => PrimaryKeyStrategy.Identity,
            "SEQUENCE" => PrimaryKeyStrategy.Sequence,
            "TABLE" => PrimaryKeyStrategy.HiLo,
            "UUID" => PrimaryKeyStrategy.Uuid,
            _ => PrimaryKeyStrategy.Unspecified,
        }
        : PrimaryKeyStrategy.Unspecified;

    /// <summary>
    /// The generator's parameters in the canonical vocabulary (decision 020): allocationSize
    /// is the block size unchanged, the table generator's four names are the counter's
    /// table, value column, key column and key value. A strategy the vocabulary narrowed
    /// keeps its own name beside the value.
    /// </summary>
    private void WriteStrategyDetails(JpaEntityFacts facts, JpaAttributeFacts id)
    {
        var generator = id.Generator
            ?? (id.GeneratorName is not null && facts.Generators.TryGetValue(id.GeneratorName, out var named) ? named : null);

        string? sourceName = id.GenerationStrategy switch
        {
            "TABLE" => "TABLE",
            not null and not ("AUTO" or "IDENTITY" or "SEQUENCE" or "UUID") => id.GenerationStrategy,
            _ => null,
        };

        var parameters = new Dictionary<GeneratorParameter, string>();
        if (generator is not null)
        {
            if (generator.SequenceName is not null)
            {
                parameters[GeneratorParameter.SequenceName] = generator.SequenceName;
            }

            if (generator.Schema is not null)
            {
                parameters[GeneratorParameter.Schema] = generator.Schema;
            }

            if (generator.AllocationSize is { } block)
            {
                parameters[GeneratorParameter.BlockSize] = block.ToString();
            }

            if (generator.InitialValue is { } initial)
            {
                parameters[GeneratorParameter.InitialValue] = initial.ToString();
            }

            if (generator.Table is not null)
            {
                parameters[GeneratorParameter.CounterTable] = generator.Table;
            }

            if (generator.ValueColumnName is not null)
            {
                parameters[GeneratorParameter.CounterValueColumn] = generator.ValueColumnName;
            }

            if (generator.PkColumnName is not null)
            {
                parameters[GeneratorParameter.CounterKeyColumn] = generator.PkColumnName;
            }

            if (generator.PkColumnValue is not null)
            {
                parameters[GeneratorParameter.CounterKeyValue] = generator.PkColumnValue;
            }
        }

        if (sourceName is not null || parameters.Count > 0)
        {
            entityBuilder.SetKeyStrategyDetails(id.Name, sourceName, parameters);
        }
    }

    private void WriteRelation(JpaEntityFacts facts, JpaAttributeFacts attribute)
    {
        var target = attribute.TargetEntity
            ?? (attribute.TypeText is null ? null : TargetOfType(attribute.TypeText));

        if (string.IsNullOrWhiteSpace(target))
        {
            Report(ConversionRecordKind.Incompleteness, attribute.Name, MappingFactCategory.ForeignKeyColumns,
                "The relation names no target entity and its type does not tell, so it was not read.");
            return;
        }

        var inverse = !string.IsNullOrWhiteSpace(attribute.MappedBy);
        var joinColumns = attribute.JoinColumns
            .Select(c => c.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToList();

        switch (attribute.Kind)
        {
            case JpaAttributeKind.ManyToOne:
                entityBuilder.AddForeignKey(Cardinality.ManyToOne, attribute.Name, target, RelationRole.Owning,
                    joinColumns.Count > 0 ? joinColumns : null);
                break;

            case JpaAttributeKind.OneToOne:
                if (attribute.MapsId)
                {
                    // The shared primary key of decision 012: the relation's columns are the
                    // entity's own key columns.
                    joinColumns = facts.Attributes
                        .Where(a => a.Kind == JpaAttributeKind.Id)
                        .Select(a => a.ColumnName ?? a.Name)
                        .ToList();
                }

                entityBuilder.AddForeignKey(Cardinality.OneToOne, attribute.Name, target,
                    inverse ? RelationRole.Inverse : RelationRole.Owning,
                    joinColumns.Count > 0 ? joinColumns : null);
                break;

            case JpaAttributeKind.OneToMany:
                // A unidirectional one-to-many with @JoinColumn names the child's columns,
                // which is what NHibernate's <key> states; mappedBy states none.
                entityBuilder.AddForeignKey(Cardinality.OneToMany, attribute.Name, target, RelationRole.Inverse,
                    !inverse && joinColumns.Count > 0 ? joinColumns : null);
                break;

            case JpaAttributeKind.ManyToMany:
                if (inverse || attribute.JoinTable is null)
                {
                    entityBuilder.AddForeignKey(Cardinality.ManyToMany, attribute.Name, target, RelationRole.Inverse);
                    break;
                }

                var table = attribute.JoinTable;
                var own = table.JoinColumns.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).ToList();
                var far = table.InverseJoinColumns.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).ToList();

                entityBuilder.AddForeignKey(
                    Cardinality.ManyToMany,
                    attribute.Name,
                    target,
                    RelationRole.Owning,
                    own.Count > 0 ? own : null,
                    new JunctionFacts(table.Name, table.Schema, far.Count > 0 ? far : null));
                break;
        }

        if (attribute.Nullable is { } nullable || attribute.Optional is { } optional)
        {
            // optional = false on the relation, or nullable on its join column, is a claim
            // about the foreign key column; it lands on the navigation's map the way the
            // EF Core parser stores a [Required] navigation.
            var value = attribute.Nullable ?? attribute.Optional;
            entityBuilder.SetPropertyDatabaseMapping(attribute.Name, new Dictionary<string, string>
            {
                ["nullable"] = value == true ? "true" : "false",
            });
        }

        if (attribute.JoinColumns.Count > 0 && attribute.JoinColumns.All(c => c.Nullable is { } n && !n)
            && attribute.Nullable is null && attribute.Optional is null)
        {
            entityBuilder.SetPropertyDatabaseMapping(attribute.Name, new Dictionary<string, string> { ["nullable"] = "false" });
        }
    }

    /// <summary>The entity a navigation's type names: the element of a collection, or the type itself.</summary>
    private static string? TargetOfType(string typeText)
    {
        var langType = JavaTypeConvertor.FromString(typeText);
        var element = langType.Category == LangTypeCategory.Collection ? langType.ElementType! : langType;

        return element.Category switch
        {
            LangTypeCategory.Unknown => JavaTypeConvertor.StripPackage(element.SourceName!),
            LangTypeCategory.Reference => element.TargetEntity,
            _ => null,
        };
    }

    /// <summary>
    /// @UniqueConstraint names columns; the model names properties (decision 055), so the
    /// columns are translated through the attributes' stated or default column names. A
    /// column no attribute carries cannot be named at all - inventing a member is what the
    /// catalog completion phase refuses too - and the constraint is dropped with a record.
    /// </summary>
    private void WriteUniqueConstraint(JpaEntityFacts facts, JpaUniqueConstraintFacts constraint)
    {
        var properties = new List<string>();
        var missing = new List<string>();

        foreach (var column in constraint.ColumnNames)
        {
            var attribute = facts.Attributes.FirstOrDefault(a =>
                string.Equals(a.ColumnName ?? a.Name, column, StringComparison.Ordinal))
                ?? facts.Attributes.FirstOrDefault(a =>
                    string.Equals(a.ColumnName ?? a.Name, column, StringComparison.OrdinalIgnoreCase));

            if (attribute is null)
            {
                missing.Add(column);
            }
            else
            {
                properties.Add(attribute.Name);
            }
        }

        if (missing.Count > 0)
        {
            Report(ConversionRecordKind.Incompleteness, null, MappingFactCategory.UniqueConstraint,
                $"The unique constraint over ({string.Join(", ", constraint.ColumnNames)}) names "
                + $"{(missing.Count == 1 ? "a column" : "columns")} no attribute of the class maps "
                + $"({string.Join(", ", missing)}); it was not read.");
            return;
        }

        entityBuilder.AddUniqueConstraint(constraint.Name, properties);
    }

    private void ReportUnread(JpaAttributeFacts attribute)
    {
        foreach (var unread in attribute.Unread)
        {
            Report(ConversionRecordKind.Loss, attribute.Name, null,
                $"The annotation {unread} has no counterpart in the intermediate representation and was dropped.");
        }

        // What the implementation's own reader had more to say than the sentence above
        // (decision 080); it arrives as a whole reason and is written out as it stands.
        foreach (var note in attribute.Notes)
        {
            Report(ConversionRecordKind.Loss, attribute.Name, null, note);
        }
    }

    private void Report(ConversionRecordKind kind, string? property, MappingFactCategory? category, string reason)
        => entityBuilder.Report(new ConversionRecord
        {
            Kind = kind,
            Framework = entityBuilder.Descriptor.Framework,
            Artifact = artifact,
            Entity = entityBuilder.EntityMap.Entity.Name,
            Property = property,
            Category = category,
            Reason = reason,
        });
}
