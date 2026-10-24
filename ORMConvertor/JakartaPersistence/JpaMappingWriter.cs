using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Sql;
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
/// <param name="declaredSourceDialect">
/// What the source declared about the dialect of its literal SQL (decision 088), taken from
/// the reading context both parsers share. It governs one attribute of one annotation -
/// columnDefinition, the only place a JPA source spells a database system's own type.
/// </param>
public sealed class JpaMappingWriter(
    AbstractEntityBuilder entityBuilder,
    ConversionContentType artifact,
    SourceSqlDialect? declaredSourceDialect = null)
{
    /// <summary>Writes the facts of the current entity of the builder.</summary>
    public void Write(JpaEntityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        // What a construct of excluded area 2 maps leaves the model with the one record the
        // builder writes for it (decision 177), so no step below writes a fact of it or
        // reports another annotation beside it.
        foreach (var attribute in facts.Attributes.Where(a => a.ExcludedConstruct is not null).ToList())
        {
            entityBuilder.ExcludeProperty(attribute.Name, attribute.ExcludedConstruct!, attribute.HoldsValue, artifact: artifact);
            facts.Attributes.Remove(attribute);
        }

        if (facts.IsEmbeddable)
        {
            entityBuilder.ExcludeValueType(artifact == ConversionContentType.XML ? "<embeddable>" : "@Embeddable");
        }

        // Every write below opens the builder's reading scope at the annotation or element
        // that stated the fact (decision 125), through the positions both readers recorded
        // under the annotation's name; where a reader recorded none, the scope is no scope.
        using (At(facts.Positions, "Table"))
        {
            if (!string.IsNullOrWhiteSpace(facts.Table))
            {
                entityBuilder.AddTable(facts.Table);
            }

            if (!string.IsNullOrWhiteSpace(facts.Schema))
            {
                entityBuilder.AddSchema(facts.Schema);
            }
        }

        foreach (var attribute in facts.Attributes.Where(a => a.Kind == JpaAttributeKind.Transient))
        {
            using (At(attribute.Positions, "Transient", "member"))
            {
                entityBuilder.MarkTransient(attribute.Name);
            }

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
            ReportReadOnlyFlags(facts, attribute);
        }

        WriteKey(facts);

        var joinColumnSingles = new List<(string Property, JpaAttributeFacts Relation)>();

        foreach (var attribute in facts.Attributes.Where(a => a.Kind is JpaAttributeKind.ManyToOne or JpaAttributeKind.OneToOne or JpaAttributeKind.OneToMany or JpaAttributeKind.ManyToMany))
        {
            WriteRelation(facts, attribute);
            ReadJoinColumnUniqueness(facts, attribute, joinColumnSingles);
            ReportUnread(attribute);
        }

        foreach (var property in singles)
        {
            using var scope = At(facts.Attributes.First(a => a.Name == property).Positions, "Column", "member");
            entityBuilder.AddUniqueConstraint(null, [property]);
        }

        foreach (var (property, relation) in joinColumnSingles)
        {
            // The statement stands on the join column of the relation, not on the scalar it
            // lands on (decision 125).
            using var scope = At(relation.Positions, "JoinColumn", "JoinColumns", "JoinTable", relation.Kind.ToString(), "member");
            entityBuilder.AddUniqueConstraint(null, [property]);
        }

        foreach (var constraint in facts.UniqueConstraints)
        {
            using var scope = At(facts.Positions, "Table");
            WriteUniqueConstraint(facts, constraint);
        }

        foreach (var unread in facts.Unread)
        {
            Report(ConversionRecordKind.Loss, null, null,
                $"The class annotation {unread} has no counterpart in the intermediate representation and was dropped.");
        }

        if (facts.IsMappedSuperclass)
        {
            // The companion of the record a stated base type gets (decision 048): that one
            // says the hierarchy is gone, this one says what the hierarchy was carrying.
            // A mapped superclass is not an entity, and the attributes it declares belong
            // to the tables of the entities extending it; the model has no place for
            // either fact, so the class leaves as an entity of its own - a table nobody
            // asked for - and the entities that extend it leave without its attributes.
            Report(ConversionRecordKind.Loss, null, null,
                $"The class annotation @MappedSuperclass says {facts.ClassName} is not an entity: it declares "
                + "mapped attributes that belong to the tables of the entities extending it. The intermediate "
                + "representation has no place for a hierarchy, so the class is translated as an entity of its "
                + "own and the entities extending it do not receive its attributes.");
        }
    }

    /* ---- reading scopes (decision 125) ------------------------------------------- */

    /// <summary>
    /// The builder's reading scope at the first of <paramref name="keys"/> the reader placed -
    /// an annotation's name, or "member" for the declaration - or no scope where it placed
    /// none. The keys are tried in order, so the caller names the most specific statement first.
    /// </summary>
    private IDisposable At(Dictionary<string, TextRange> positions, params string[] keys)
        => entityBuilder.At(First(positions, keys));

    /// <summary>The same, with finer ranges for particular categories where those statements were placed.</summary>
    private IDisposable At(
        Dictionary<string, TextRange> positions,
        string[] keys,
        params (MappingFactCategory Category, string? Property, string[] Keys)[] refinements)
        => entityBuilder.At(
            First(positions, keys),
            [.. refinements
                .Select(r => (r.Category, r.Property, Range: First(positions, r.Keys)))
                .Where(r => r.Range is not null)
                .Select(r => new FactRefinement(r.Category, r.Property, r.Range!.Value))]);

    private static TextRange? First(Dictionary<string, TextRange> positions, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (positions.TryGetValue(key, out var range))
            {
                return range;
            }
        }

        return null;
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

        // Each column fact stands at the statement that made it (decision 125): @Column for the
        // column's own facets, @Basic or the declaration for nullability stated nowhere else,
        // @Version for the version flag, the vendor annotation for the unicode facet.
        using var scope = At(
            attribute.Positions,
            ["member", "Column"],
            (MappingFactCategory.ColumnName, attribute.Name, ["Column"]),
            (MappingFactCategory.Length, attribute.Name, ["Column"]),
            (MappingFactCategory.PrecisionAndScale, attribute.Name, ["Column"]),
            (MappingFactCategory.Nullability, attribute.Name, ["Column", "Basic", "member"]),
            (MappingFactCategory.VersionColumn, attribute.Name, ["Version"]),
            (MappingFactCategory.DatabaseType, attribute.Name, ["Nationalized", "Column"]));

        // Called even with nothing to record: an attribute orm.xml names before the class
        // is read has to exist in the model before anything can attach to it.
        entityBuilder.SetPropertyDatabaseMapping(attribute.Name, dbProps);

        if (attribute.Nationalized is { } unicode)
        {
            entityBuilder.SetPropertyDatabaseType(attribute.Name, null, isUnicode: unicode);
        }

        if (!string.IsNullOrWhiteSpace(attribute.ColumnDefinition))
        {
            // The source said its SQL is another system's, so this column definition is not
            // read and its literal spelling does not travel either (decision 088): a
            // VARCHAR2(50) copied onto the escape path would end up in an artifact the
            // descriptor says is written for SQL Server 2022. The target derives the column
            // type from the Java type instead, or the catalog supplies it.
            if (SqlTypeSpelling.Read(attribute.ColumnDefinition, declaredSourceDialect) is not { } reading)
            {
                Report(ConversionRecordKind.Loss, attribute.Name, MappingFactCategory.DatabaseType,
                    ForeignDialect.Reason($"columnDefinition = \"{attribute.ColumnDefinition.Trim()}\""));

                return;
            }

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
    ///
    /// A columnDefinition the source declared foreign (decision 088) classifies as no column
    /// at all, exactly as a missing one does: the guard stops the reading of the name, and
    /// classifying it anyway would be the same reading under another word.
    /// </summary>
    private void WritePrecision(JpaAttributeFacts attribute, Dictionary<string, string> dbProps)
    {
        var kind = JpaColumnPrecision.Classify(
            string.IsNullOrWhiteSpace(attribute.ColumnDefinition)
                ? null
                : SqlTypeSpelling.Read(attribute.ColumnDefinition, declaredSourceDialect)?.Type,
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
                using var scope = At(embedded.Positions, "EmbeddedId", "member");
                entityBuilder.AddEmbeddedPrimaryKey(embedded.Name, keyClass);
            }

            ReportUnread(embedded);
            return;
        }

        var ids = facts.Attributes.Where(a => a.Kind == JpaAttributeKind.Id || a.IsDerivedIdentity).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        // Each @Id in declaration order; a derived identity contributes its join columns,
        // each as a part of its own, or nothing (DerivedIdentityParts).
        var parts = new List<(string PropertyName, int Order, PrimaryKeyStrategy Strategy, JpaAttributeFacts Source)>();
        foreach (var id in ids)
        {
            if (id.Kind == JpaAttributeKind.Id)
            {
                parts.Add((id.Name, parts.Count + 1, StrategyOf(id), id));
                continue;
            }

            foreach (var column in DerivedIdentityParts(id))
            {
                parts.Add((column, parts.Count + 1, PrimaryKeyStrategy.Assigned, id));
            }
        }

        if (parts.Count == 0)
        {
            return;
        }

        var composite = parts.Count > 1;

        // The key stands at each part's @Id, its strategy at the part's @GeneratedValue
        // (decision 125); the parts of a derived identity stand at the @Id of the relation.
        using (entityBuilder.At(
            First(ids[0].Positions, ["Id", "member"]),
            [.. parts.SelectMany(part => new (MappingFactCategory Category, TextRange? Range)[]
                {
                    (MappingFactCategory.PrimaryKey, First(part.Source.Positions, ["Id", "member"])),
                    (MappingFactCategory.PrimaryKeyStrategy, First(part.Source.Positions, ["GeneratedValue", "Id", "member"])),
                }
                .Where(r => r.Range is not null)
                .Select(r => new FactRefinement(r.Category, part.PropertyName, r.Range!.Value)))]))
        {
            entityBuilder.AddPrimaryKey(
                [.. parts.Select(p => (p.PropertyName, p.Order, p.Strategy))],
                composite && facts.IdClass is not null ? new SourceKeyClass(facts.IdClass, KeyClassForm.Mirrored) : null);
        }

        foreach (var id in ids.Where(id => id.Kind == JpaAttributeKind.Id))
        {
            using var scope = At(id.Positions, "GeneratedValue", "SequenceGenerator", "TableGenerator", "Id", "member");
            WriteStrategyDetails(facts, id);
        }
    }

    /// <summary>
    /// The key parts a derived identity stands for - @Id on a @ManyToOne or @OneToOne
    /// (Jakarta Persistence 3.2 §2.4.1). The flat key of decision 006 has no reference-typed
    /// part, so the relation is read the way the NHibernate parser reads &lt;key-many-to-one&gt;:
    /// its stated join columns become scalar parts named after the column, Assigned because
    /// the specification derives the value from the parent and admits no generator on it,
    /// and the relation stands beside them over the same columns, which the targets write
    /// read-only (decision 012); the language type of a part arrives from the referenced key
    /// when the pairs resolve. Both readings are reported (decision 010): the reference form
    /// is not restated, and a relation without stated columns has no column to name a part
    /// by - the default of @JoinColumn is derived only once the referenced key is known - so
    /// the key comes out shorter, which is said rather than left silent.
    /// </summary>
    private IReadOnlyList<string> DerivedIdentityParts(JpaAttributeFacts relation)
    {
        var spelled = relation.Positions.ContainsKey("Id") ? "@Id on" : "id=\"true\" on";

        if (relation.Kind is not (JpaAttributeKind.ManyToOne or JpaAttributeKind.OneToOne))
        {
            Report(ConversionRecordKind.Loss, relation.Name, MappingFactCategory.PrimaryKey,
                $"{spelled} the collection '{relation.Name}' has no meaning in Jakarta Persistence - a derived identity "
                + "stands only on a single-valued relation - so it is dropped and the relation is read as it is.");
            return [];
        }

        var columns = relation.JoinColumns
            .Select(c => c.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToList();

        if (columns.Count == 0)
        {
            Report(ConversionRecordKind.Incompleteness, relation.Name, MappingFactCategory.PrimaryKey,
                $"{spelled} the relation '{relation.Name}' makes its foreign key column a part of the key, but no "
                + "@JoinColumn names the column, so the flat key (decision 006) has no scalar part to stand in for "
                + "it; the key comes out with fewer parts than the source described.");
            return [];
        }

        Report(ConversionRecordKind.Loss, relation.Name, MappingFactCategory.PrimaryKey,
            $"{spelled} the relation '{relation.Name}' (derived identity) is read as its join column(s) "
            + $"{string.Join(", ", columns.Select(c => $"'{c}'"))} plus the relation: the key renders flat (decision 006), "
            + "so the key part is the column and the relation stands beside it read-only over the same column; the "
            + "reference form of the key part is not restated in the output.");

        return columns;
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

        StateReferencedColumns(attribute, inverse);
        ReportJoinColumnElements(facts, attribute);

        // The relation stands at its annotation, the columns at @JoinColumn or @JoinTable, the
        // nullability at the join column that stated it (decision 125). The enum's names are
        // the annotations' names, which is what both readers placed the statements under.
        var relation = attribute.Kind.ToString();
        using var scope = At(
            attribute.Positions,
            [relation, "member"],
            (MappingFactCategory.ForeignKeyColumns, attribute.Name, ["JoinColumn", "JoinColumns", "JoinTable", relation]),
            (MappingFactCategory.Nullability, attribute.Name, ["JoinColumn", relation, "member"]));

        switch (attribute.Kind)
        {
            case JpaAttributeKind.ManyToOne:
                entityBuilder.AddForeignKey(Cardinality.ManyToOne, attribute.Name, target, RelationRole.Owning,
                    joinColumns.Count > 0 ? joinColumns : null,
                    conventionalColumns: DefaultJoinColumn(attribute, joinColumns));
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
                    joinColumns.Count > 0 ? joinColumns : null,
                    conventionalColumns: inverse ? null : DefaultJoinColumn(attribute, joinColumns));
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

        if (attribute.IsDerivedIdentity && attribute.Nullable is null && attribute.Optional is null
            && attribute.JoinColumns.All(c => c.Nullable is null))
        {
            // A column of the primary key admits no NULL: the specification's statement about
            // a derived identity, not the source's silence (decision 067).
            entityBuilder.SetPropertyDatabaseMapping(attribute.Name, new Dictionary<string, string> { ["nullable"] = "false" });
        }
    }

    /// <summary>
    /// The join column Jakarta Persistence 3.2 derives for an owning reference that names
    /// none - the default of @JoinColumn's name: the attribute, an underscore and the
    /// referenced primary key column. It passes both tests of decision 067 - the inputs are
    /// the attribute the artifact declares and the key of the entity it references, and the
    /// gap travels elsewhere:
    /// NHibernate would name the column after the property, EF Core after the navigation
    /// and the key property, and the catalog would supply the schema's column without the
    /// source ever having said it. So the reading states it, as if @JoinColumn had.
    ///
    /// The key is the target's and may lie in another unit, so the derivation waits for the
    /// phase after reading (the convention navigations' one). It answers nothing where the
    /// specification gives no default - a composite key, where "the default only applies if
    /// a single join column is used" and the implementations part ways (a run of the pinned
    /// releases measured Hibernate writing attribute_column per part and EclipseLink the
    /// referenced column names bare) - nor over a join table, nor where the source's own
    /// referencedColumnName points at another column than the key, which is a relation the
    /// model pairs with the key regardless. EclipseLink writes the attribute part in upper
    /// case (CUSTOMER_CustomerID where Hibernate writes customer_CustomerID); the name is
    /// read in the specification's spelling, the same identifier under the case-insensitive
    /// matching every implicit EclipseLink name already relies on (decision 080).
    /// </summary>
    private static Func<EntityMap, EntityMap, IReadOnlyList<string>?>? DefaultJoinColumn(
        JpaAttributeFacts attribute, IReadOnlyList<string> statedColumns)
    {
        if (statedColumns.Count > 0 || attribute.MapsId || attribute.JoinTable is not null)
        {
            return null;
        }

        var referenced = attribute.JoinColumns
            .Select(c => c.ReferencedColumnName)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        return (_, target) =>
        {
            if (target.PrimaryKey is not { Parts.Count: 1 } key)
            {
                return null;
            }

            var keyColumn = key.Parts[0].PropertyMap.ColumnName ?? key.Parts[0].PropertyMap.Property.Name;

            return referenced is null || string.Equals(referenced, keyColumn, StringComparison.OrdinalIgnoreCase)
                ? [$"{attribute.Name}_{keyColumn}"]
                : null;
        };
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
    /// column a scalar maps lands on the scalar; a stated join column of an owning reference
    /// lands on its navigation, which stands for the whole foreign key (decision 130), so the
    /// constraint has to cover every join column of it. A column no attribute carries cannot
    /// be named at all - inventing a member is what the catalog completion phase refuses
    /// too - and neither can part of a composite foreign key: the constraint is dropped with
    /// a record.
    /// </summary>
    private void WriteUniqueConstraint(JpaEntityFacts facts, JpaUniqueConstraintFacts constraint)
    {
        var properties = new List<string>();
        var missing = new List<string>();
        var references = new List<JpaAttributeFacts>();

        foreach (var column in constraint.ColumnNames)
        {
            var attribute = FindScalar(facts, column, a => !IsRelation(a.Kind));

            if (attribute is not null)
            {
                properties.Add(attribute.Name);
                continue;
            }

            var reference = facts.Attributes.FirstOrDefault(a => StatedJoinColumns(a).Contains(column, StringComparer.Ordinal))
                ?? facts.Attributes.FirstOrDefault(a => StatedJoinColumns(a).Contains(column, StringComparer.OrdinalIgnoreCase));

            if (reference is null)
            {
                missing.Add(column);
            }
            else if (!references.Contains(reference))
            {
                references.Add(reference);
                properties.Add(reference.Name);
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

        var partial = references
            .Where(r => !StatedJoinColumns(r).All(c => constraint.ColumnNames.Contains(c, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        if (partial.Count > 0)
        {
            Report(ConversionRecordKind.Incompleteness, null, MappingFactCategory.UniqueConstraint,
                $"The unique constraint over ({string.Join(", ", constraint.ColumnNames)}) covers part of the foreign key of "
                + $"{string.Join(", ", partial.Select(r => $"'{r.Name}'"))}; the intermediate representation names a foreign key "
                + "column by its navigation, which stands for all of them, so it was not read (decision 130).");
            return;
        }

        entityBuilder.AddUniqueConstraint(constraint.Name, properties);
    }

    private static bool IsRelation(JpaAttributeKind kind)
        => kind is JpaAttributeKind.ManyToOne or JpaAttributeKind.OneToOne or JpaAttributeKind.OneToMany or JpaAttributeKind.ManyToMany;

    /// <summary>The attribute of the given kinds whose stated or default column is the one named, exact case first.</summary>
    private static JpaAttributeFacts? FindScalar(JpaEntityFacts facts, string column, Func<JpaAttributeFacts, bool> kind)
        => facts.Attributes.FirstOrDefault(a => kind(a) && string.Equals(a.ColumnName ?? a.Name, column, StringComparison.Ordinal))
            ?? facts.Attributes.FirstOrDefault(a => kind(a) && string.Equals(a.ColumnName ?? a.Name, column, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The join columns an owning reference states by name - the columns of this entity's
    /// table a navigation stands for in a unique constraint (decision 130). Empty for an
    /// inverse side, a join table, a shared primary key, whose columns are the key's, and
    /// a reference whose column the source left to the default, which is derived from the
    /// target's key only after reading.
    /// </summary>
    private static IReadOnlyList<string> StatedJoinColumns(JpaAttributeFacts attribute)
    {
        if (attribute.Kind is not (JpaAttributeKind.ManyToOne or JpaAttributeKind.OneToOne)
            || !string.IsNullOrWhiteSpace(attribute.MappedBy)
            || attribute.JoinTable is not null
            || attribute.MapsId)
        {
            return [];
        }

        var names = attribute.JoinColumns.Select(c => c.Name).ToList();

        return names.All(n => !string.IsNullOrWhiteSpace(n)) ? [.. names.Select(n => n!)] : [];
    }

    /// <summary>
    /// Hands the referenced column names of the join columns the writer uses to the builder
    /// to judge against the key they point at (decision 012): the target's for a reference,
    /// the entity's own for the join column of a unidirectional one-to-many, which sits in
    /// the target's table, and each side's own for the two halves of a join table. The join
    /// columns of an inverse side are not used by the provider and not judged here either.
    /// </summary>
    private void StateReferencedColumns(JpaAttributeFacts attribute, bool inverse)
    {
        if (inverse)
        {
            return;
        }

        static List<string> Referenced(IEnumerable<JpaJoinColumnFacts> columns)
            => [.. columns.Select(c => c.ReferencedColumnName).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!)];

        if (attribute.JoinTable is { } table)
        {
            if (attribute.Kind == JpaAttributeKind.ManyToMany)
            {
                if (Referenced(table.JoinColumns) is { Count: > 0 } own)
                {
                    entityBuilder.AddStatedReferencedColumns(attribute.Name, own, ownKey: true);
                }

                if (Referenced(table.InverseJoinColumns) is { Count: > 0 } far)
                {
                    entityBuilder.AddStatedReferencedColumns(attribute.Name, far);
                }
            }

            return;
        }

        if (Referenced(attribute.JoinColumns) is { Count: > 0 } referenced)
        {
            entityBuilder.AddStatedReferencedColumns(attribute.Name, referenced, ownKey: attribute.Kind == JpaAttributeKind.OneToMany);
        }
    }

    /// <summary>
    /// The elements of the join columns the model has no place for (decision 048): table and
    /// columnDefinition are reported outright; insertable = false and updatable = false only
    /// where no basic attribute of the class maps the same column, since beside one they are
    /// the read-only idiom of a relation over a scalar's column, which the model derives
    /// from the pair and the targets write again (decision 012) - the mirror image of
    /// <see cref="ReportReadOnlyFlags"/>.
    /// </summary>
    private void ReportJoinColumnElements(JpaEntityFacts facts, JpaAttributeFacts attribute)
    {
        var joinColumns = attribute.JoinColumns
            .Concat(attribute.JoinTable?.JoinColumns ?? [])
            .Concat(attribute.JoinTable?.InverseJoinColumns ?? []);

        foreach (var joinColumn in joinColumns)
        {
            foreach (var unread in joinColumn.Unread)
            {
                Report(ConversionRecordKind.Loss, attribute.Name, null,
                    $"The annotation {unread} has no counterpart in the intermediate representation and was dropped.");
            }

            if (joinColumn.ReadOnlyFlags.Count == 0)
            {
                continue;
            }

            var besideScalar = joinColumn.Name is { } column && facts.Attributes.Any(a =>
                a.Kind is JpaAttributeKind.Basic or JpaAttributeKind.Id or JpaAttributeKind.Version
                && string.Equals(a.ColumnName ?? a.Name, column, StringComparison.OrdinalIgnoreCase));

            if (besideScalar)
            {
                continue;
            }

            foreach (var flag in joinColumn.ReadOnlyFlags)
            {
                Report(ConversionRecordKind.Loss, attribute.Name, null,
                    $"The annotation {flag} leaves the column out of the framework's write statements; the intermediate "
                    + "representation has no place for it and it was dropped.");
            }
        }
    }

    /// <summary>
    /// unique = true on a join column is the specification's shortcut for @UniqueConstraint
    /// over that column, and is read as that annotation is (decision 130). On the own join
    /// column of a @OneToOne it states the uniqueness a 1:1 foreign key has anyway - the
    /// model carries it in the cardinality and every target writes it again from there -
    /// so nothing is added and nothing reported. Elsewhere it is a constraint of the
    /// schema: over a column a scalar of the same class also maps it lands on that scalar,
    /// as @Column(unique = true) there would; over the single join column of a @ManyToOne
    /// it lands on the navigation, which stands for that column. What the model cannot
    /// name - one column of a composite foreign key, a column of the target's table or of
    /// a join table - is reported as the incompleteness @UniqueConstraint over it gives.
    /// </summary>
    private void ReadJoinColumnUniqueness(
        JpaEntityFacts facts,
        JpaAttributeFacts attribute,
        List<(string Property, JpaAttributeFacts Relation)> singles)
    {
        var own = attribute.JoinColumns.Where(c => c.Unique);
        var junction = (attribute.JoinTable?.JoinColumns ?? [])
            .Concat(attribute.JoinTable?.InverseJoinColumns ?? [])
            .Where(c => c.Unique);

        if (attribute.Kind == JpaAttributeKind.OneToOne)
        {
            own = [];
        }

        foreach (var joinColumn in own.Concat(junction))
        {
            var scalar = joinColumn.Name is { } column
                ? FindScalar(facts, column, a => a.Kind is JpaAttributeKind.Basic or JpaAttributeKind.Id or JpaAttributeKind.Version)
                : null;

            var standsFor = scalar?.Name
                ?? (attribute.Kind == JpaAttributeKind.ManyToOne
                    && attribute.JoinTable is null
                    && !attribute.MapsId
                    && attribute.JoinColumns.Count == 1
                        ? attribute.Name
                        : null);

            if (standsFor is not null)
            {
                if (!singles.Any(s => s.Property == standsFor))
                {
                    singles.Add((standsFor, attribute));
                }

                continue;
            }

            var named = joinColumn.Name is null ? "the join column" : $"the join column '{joinColumn.Name}'";
            var why = attribute.Kind == JpaAttributeKind.ManyToOne && attribute.JoinTable is null
                ? "is one column of a composite foreign key, and the intermediate representation names a foreign key column by its "
                    + "navigation, which stands for all of them"
                : "lies in another table than the entity's, and the intermediate representation names a constraint by the members "
                    + "of the entity it belongs to";
            Report(ConversionRecordKind.Incompleteness, attribute.Name, MappingFactCategory.UniqueConstraint,
                $"unique = true on {named} of '{attribute.Name}' states a unique constraint over the column; the column {why}, "
                + "so it was not read (decision 130).");
        }
    }

    /// <summary>
    /// insertable = false and updatable = false on a column of the attribute's own are a
    /// statement about the framework's write statements the model has no place for, and are
    /// reported (decision 048). Over a column a relation of the same class maps through a join
    /// column they are the read-only idiom of a scalar beside its relation, which the model
    /// derives from the relation and every target writes again in its own words (decision
    /// 012), so there they state nothing the model loses.
    /// </summary>
    private void ReportReadOnlyFlags(JpaEntityFacts facts, JpaAttributeFacts attribute)
    {
        if (attribute.ReadOnlyFlags.Count == 0)
        {
            return;
        }

        var column = attribute.ColumnName ?? attribute.Name;

        var overRelation = facts.Attributes.Any(a => a != attribute
            && a.JoinColumns.Any(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase)));

        if (overRelation)
        {
            return;
        }

        foreach (var flag in attribute.ReadOnlyFlags)
        {
            Report(ConversionRecordKind.Loss, attribute.Name, null,
                $"The annotation {flag} leaves the column out of the framework's write statements; the intermediate "
                + "representation has no place for it and it was dropped.");
        }
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
