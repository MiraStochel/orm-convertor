using System.Text;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using JavaEntityParsing;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace JakartaPersistence;

/// <summary>
/// Emits a Java entity class with jakarta.persistence annotations (decision 077) - the
/// part of the output that Jakarta Persistence 3.2 fixes and that Hibernate and
/// EclipseLink share verbatim (decision 076). What differs between them arrives through
/// the hooks: the profile of the implementation, the spelling of national character data
/// and the imports its annotations need.
///
/// The style is "explicit JPA" on purpose (decision 076): every table, column, join column
/// and generation strategy is written out, and GenerationType.AUTO is never emitted,
/// because the same silence means a sequence under one implementation and a table under
/// the other. A value the source never stated and the artifact writes anyway is a
/// convention record.
/// </summary>
public abstract class AbstractJpaEntityBuilder : AbstractEntityBuilder
{
    private const string Jakarta = "jakarta.persistence";

    private readonly SortedSet<string> imports = new(StringComparer.Ordinal);

    private readonly List<(string Type, string Name, LangType LangType)> accessors = [];

    /// <summary>The profile of the target implementation: the key to its defaults (decision 077).</summary>
    protected abstract JpaImplementationProfile Profile { get; }

    /// <summary>
    /// The second hook of decision 076: how the implementation spells national character
    /// data on a column the model marks unicode. Hibernate adds an annotation; an
    /// implementation without one answers with a column definition instead.
    /// </summary>
    protected abstract void AppendNationalization(EntityMap entityMap, PropertyMap propertyMap, StringBuilder code);

    /// <summary>Adds an import the artifact needs; called by the hooks as well.</summary>
    protected void Import(string qualifiedName) => imports.Add(qualifiedName);

    protected override void BuildImports(EntityMap entityMap, EntityArtifact artifact)
    {
        // The imports follow from what the steps below emit, so they are collected while
        // emitting and rendered ahead of the class in FinalizeBuild.
        imports.Clear();
        accessors.Clear();
    }

    protected override void BuildTableSchema(EntityMap entityMap, EntityArtifact artifact)
    {
        Import($"{Jakarta}.Entity");
        Import($"{Jakarta}.Table");

        artifact.Code.AppendLine("@Entity");

        // The table name is written even where the source stated none (decision 076); the
        // value is then the JPA default, which is a convention of the target (decision 010).
        var table = entityMap.Table;
        if (table is null)
        {
            table = entityMap.Entity.Name;
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Convention,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.JavaEntity,
                Entity = entityMap.Entity.Name,
                Category = MappingFactCategory.TableName,
                Reason = $"No table name was stated; the artifact writes @Table(name = \"{table}\"), the entity name, "
                    + "which is the JPA default made explicit (decision 077).",
            });
        }

        var tableArguments = new List<string> { $"name = \"{table}\"" };
        if (entityMap.Schema is not null)
        {
            tableArguments.Add($"schema = \"{entityMap.Schema}\"");
        }

        var constraints = RenderUniqueConstraints(entityMap);
        if (constraints is not null)
        {
            tableArguments.Add($"uniqueConstraints = {constraints}");
        }

        artifact.Code.AppendLine($"@Table({string.Join(", ", tableArguments)})");

        if (entityMap.PrimaryKey is { Parts.Count: > 1 })
        {
            Import($"{Jakarta}.IdClass");
            artifact.Code.AppendLine($"@IdClass({entityMap.Entity.Name}.{KeyClassName(entityMap)}.class)");
        }

        artifact.Code.AppendLine($"{ClassModifier(entityMap.Entity.AccessModifier)}class {entityMap.Entity.Name} {{");
        artifact.ClassOpened = true;
    }

    /// <summary>
    /// A top-level Java class is public or package-private; the other values of the model
    /// have no top-level spelling and fall to package-private.
    /// </summary>
    private static string ClassModifier(AccessModifier? modifier)
        => modifier == AccessModifier.Public ? "public " : string.Empty;

    /// <summary>
    /// @UniqueConstraint names columns, so each property translates to its column
    /// (decision 055). A constraint over a property the entity does not declare is a gap
    /// of the model and is reported; one over a property the source states is not
    /// persisted has no column to constrain and is dropped with a loss (decision 072).
    /// </summary>
    private string? RenderUniqueConstraints(EntityMap entityMap)
    {
        var rendered = new List<string>();

        foreach (var constraint in entityMap.UniqueConstraints)
        {
            var maps = constraint.PropertyNames
                .Select(name => entityMap.PropertyMaps.FirstOrDefault(pm => pm.Property.Name == name))
                .ToList();

            if (maps.Any(pm => pm is null))
            {
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Incompleteness,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.JavaEntity,
                    Entity = entityMap.Entity.Name,
                    Category = MappingFactCategory.UniqueConstraint,
                    Reason = $"The unique constraint over ({string.Join(", ", constraint.PropertyNames)}) names a property "
                        + "the entity does not declare; it is not written.",
                });
                continue;
            }

            if (maps.Any(pm => pm!.IsTransient))
            {
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Loss,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.JavaEntity,
                    Entity = entityMap.Entity.Name,
                    Property = constraint.PropertyNames.Count == 1 ? constraint.PropertyNames[0] : null,
                    Category = MappingFactCategory.UniqueConstraint,
                    Reason = $"The unique constraint over ({string.Join(", ", constraint.PropertyNames)}) covers a property "
                        + "the source states is not persisted; there is no column to constrain, so it is dropped (decision 072).",
                });
                continue;
            }

            Import($"{Jakarta}.UniqueConstraint");
            var columns = string.Join(", ", maps.Select(pm => $"\"{ColumnOf(pm!)}\""));
            var named = constraint.Name is null ? string.Empty : $"name = \"{constraint.Name}\", ";
            rendered.Add($"@UniqueConstraint({named}columnNames = {{ {columns} }})");
        }

        return rendered.Count == 0 ? null : $"{{ {string.Join(", ", rendered)} }}";
    }

    protected override void BuildPrimaryKey(EntityMap entityMap, EntityArtifact artifact)
    {
        if (entityMap.PrimaryKey is null)
        {
            return;
        }

        Import($"{Jakarta}.Id");

        foreach (var part in entityMap.PrimaryKey.Parts)
        {
            var propertyMap = part.PropertyMap;
            var code = artifact.Code;

            code.AppendLine();
            code.AppendLine("    @Id");
            AppendGeneration(entityMap, part, code);
            AppendColumn(entityMap, propertyMap, code, isKey: true);

            if (propertyMap.IsUnicode == true)
            {
                AppendNationalization(entityMap, propertyMap, code);
            }

            // An identifier is always a wrapper type: the unassigned state has to be
            // representable (decision 077), and the flat key of decision 006 never carries
            // the source's language nullability anyway - here nothing is lost by it.
            AppendField(entityMap, propertyMap.Property, code, forceWrapper: true);
        }
    }

    protected override void BuildProperties(EntityMap entityMap, EntityArtifact artifact)
    {
        foreach (var propertyMap in entityMap.PropertyMaps)
        {
            if (entityMap.PrimaryKey?.Parts.Any(p => p.PropertyMap == propertyMap) == true
                || entityMap.Relations.Any(r => r.SourceNavigationProperty == propertyMap.Property.Name))
            {
                continue;
            }

            var code = artifact.Code;
            code.AppendLine();

            if (propertyMap.IsTransient)
            {
                // @Transient is the whole statement (decision 072); a column annotation beside
                // it would claim a mapping the provider does not read.
                Import($"{Jakarta}.Transient");
                code.AppendLine("    @Transient");
                AppendField(entityMap, propertyMap.Property, code);
                continue;
            }

            if (propertyMap.IsVersion)
            {
                Import($"{Jakarta}.Version");
                code.AppendLine("    @Version");
            }

            // A scalar over a column a relation of the entity also maps is read-only here,
            // so that the column is written once, by the relation - the rule NHibernate
            // has for the same shape (decision 012).
            AppendColumn(entityMap, propertyMap, code, isKey: false, readOnly: IsForeignKeyColumn(entityMap, propertyMap));

            if (propertyMap.IsUnicode == true)
            {
                AppendNationalization(entityMap, propertyMap, code);
            }

            AppendField(entityMap, propertyMap.Property, code);
        }
    }

    private static bool IsForeignKeyColumn(EntityMap entityMap, PropertyMap propertyMap)
        => entityMap.Relations.Any(r =>
            r.Role == RelationRole.Owning
            && r.ColumnPairs.Any(pair => pair.Source == propertyMap
                || string.Equals(pair.Source.ColumnName ?? pair.Source.Property.Name, ColumnOf(propertyMap), StringComparison.Ordinal)));

    protected override void BuildForeignKey(EntityMap entityMap, EntityArtifact artifact)
    {
        foreach (var relation in entityMap.Relations)
        {
            var propertyMap = relation.SourceNavigationProperty is null
                ? null
                : entityMap.PropertyMaps.FirstOrDefault(pm => pm.Property.Name == relation.SourceNavigationProperty);

            if (propertyMap is null)
            {
                continue; // a relation without a navigation property has no member to annotate
            }

            var code = artifact.Code;
            code.AppendLine();

            switch (relation.Cardinality, relation.Role)
            {
                case (Cardinality.ManyToOne, _):
                    AppendOwningReference(entityMap, relation, propertyMap, code, "ManyToOne");
                    break;

                case (Cardinality.OneToOne, RelationRole.Owning):
                    AppendOwningReference(entityMap, relation, propertyMap, code, "OneToOne");
                    break;

                case (Cardinality.OneToOne, RelationRole.Inverse):
                    AppendInverseReference(entityMap, relation, propertyMap, code);
                    break;

                case (Cardinality.OneToMany, _):
                    AppendCollection(entityMap, relation, propertyMap, code);
                    break;

                case (Cardinality.ManyToMany, _):
                    AppendManyToMany(entityMap, relation, propertyMap, code);
                    break;
            }

            AppendField(entityMap, propertyMap.Property, code);
        }
    }

    /// <summary>
    /// The owning side of a reference: @ManyToOne or @OneToOne with the join columns of
    /// the resolved pairs. A shared primary key (decision 012) is @MapsId: the key part
    /// carries no generation and the relation supplies the value. Where no column is known
    /// the JPA default is written out with a record, or left to the target with a record
    /// when even the referenced key is unknown (decision 076).
    /// </summary>
    private void AppendOwningReference(EntityMap entityMap, Relation relation, PropertyMap propertyMap, StringBuilder code, string annotation)
    {
        Import($"{Jakarta}.{annotation}");

        var optional = propertyMap.IsNullable == false ? "(optional = false)" : string.Empty;
        code.AppendLine($"    @{annotation}{optional}");

        if (SharesPrimaryKey(entityMap, relation))
        {
            Import($"{Jakarta}.MapsId");
            code.AppendLine("    @MapsId");
        }

        var target = FindEntityMap(relation.TargetEntity);
        var unique = annotation == "OneToOne";

        if (relation.ColumnPairs.Count > 0)
        {
            var keyColumns = entityMap.PrimaryKey?.Parts.Select(p => ColumnOf(p.PropertyMap)).ToHashSet(StringComparer.Ordinal) ?? [];
            var joinColumns = relation.ColumnPairs.Select(pair =>
            {
                var column = pair.Source.ColumnName ?? pair.Source.Property.Name;
                return RenderJoinColumn(
                    column,
                    ColumnOf(pair.Target),
                    propertyMap.IsNullable,
                    unique,
                    // A column that is part of the key is written by the identifier; the
                    // relation over it is read-only (the NHibernate rule of decision 012).
                    readOnly: keyColumns.Contains(column));
            }).ToList();

            AppendJoinColumns(code, joinColumns);
            return;
        }

        if (target?.PrimaryKey is { } key)
        {
            var derived = key.Parts.Select(p => RenderJoinColumn(
                $"{propertyMap.Property.Name}_{ColumnOf(p.PropertyMap)}",
                ColumnOf(p.PropertyMap),
                propertyMap.IsNullable,
                unique,
                readOnly: false)).ToList();

            AppendJoinColumns(code, derived);
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Convention,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.JavaEntity,
                Entity = entityMap.Entity.Name,
                Property = propertyMap.Property.Name,
                Category = MappingFactCategory.ForeignKeyColumns,
                Reason = $"No foreign key columns are known for the relation to '{relation.TargetEntity}'; the artifact writes "
                    + "the JPA default name, attribute_referencedColumn, made explicit (decision 077).",
            });
            return;
        }

        Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Convention,
            Framework = Descriptor.Framework,
            Artifact = ConversionContentType.JavaEntity,
            Entity = entityMap.Entity.Name,
            Property = propertyMap.Property.Name,
            Category = MappingFactCategory.ForeignKeyColumns,
            Reason = $"No foreign key columns are known for the relation to '{relation.TargetEntity}' and the referenced key is not "
                + "part of the conversion; @JoinColumn is left out and the provider derives the column by its own convention (decision 012).",
        });
    }

    private void AppendJoinColumns(StringBuilder code, List<string> joinColumns)
    {
        Import($"{Jakarta}.JoinColumn");

        if (joinColumns.Count == 1)
        {
            code.AppendLine($"    {joinColumns[0]}");
            return;
        }

        Import($"{Jakarta}.JoinColumns");
        code.AppendLine("    @JoinColumns({");
        for (var i = 0; i < joinColumns.Count; i++)
        {
            code.AppendLine($"        {joinColumns[i]}{(i < joinColumns.Count - 1 ? "," : string.Empty)}");
        }

        code.AppendLine("    })");
    }

    private static string RenderJoinColumn(string name, string referenced, bool? nullable, bool unique, bool readOnly)
    {
        var arguments = new List<string> { $"name = \"{name}\"", $"referencedColumnName = \"{referenced}\"" };

        if (nullable is { } n)
        {
            arguments.Add($"nullable = {(n ? "true" : "false")}");
        }

        if (unique)
        {
            arguments.Add("unique = true");
        }

        if (readOnly)
        {
            arguments.Add("insertable = false");
            arguments.Add("updatable = false");
        }

        return $"@JoinColumn({string.Join(", ", arguments)})";
    }

    /// <summary>
    /// The shared primary key of decision 012 in its JPA spelling: the entity's key part
    /// takes its value from the referenced entity (NHibernate's foreign generator naming
    /// this navigation), or the relation's columns are exactly the key's columns.
    /// </summary>
    private static bool SharesPrimaryKey(EntityMap entityMap, Relation relation)
    {
        if (entityMap.PrimaryKey is not { } key)
        {
            return false;
        }

        if (key.Parts.Any(p =>
            string.Equals(p.SourceStrategyName, "foreign", StringComparison.OrdinalIgnoreCase)
            && p.SourceStrategyParameters.TryGetValue("property", out var property)
            && property == relation.SourceNavigationProperty))
        {
            return true;
        }

        if (relation.Cardinality != Cardinality.OneToOne || relation.ColumnPairs.Count == 0)
        {
            return false;
        }

        var keyColumns = key.Parts.Select(p => ColumnOf(p.PropertyMap)).ToList();
        var relationColumns = relation.ColumnPairs.Select(pair => pair.Source.ColumnName ?? pair.Source.Property.Name).ToList();

        return keyColumns.Count == relationColumns.Count && keyColumns.SequenceEqual(relationColumns, StringComparer.Ordinal);
    }

    /// <summary>
    /// The inverse side of a one-to-one names the owning navigation through mappedBy; the
    /// model does not carry that name (decision 001), so it is derived from the counterpart
    /// when it takes part in the conversion. Without one the mapping cannot be written -
    /// a bare @OneToOne would claim an owning side and a column that does not exist - so
    /// the member stays on the class as @Transient and the gap is reported.
    /// </summary>
    private void AppendInverseReference(EntityMap entityMap, Relation relation, PropertyMap propertyMap, StringBuilder code)
    {
        var counterpart = OwningCounterpart(entityMap, relation);

        if (counterpart is null)
        {
            Import($"{Jakarta}.Transient");
            code.AppendLine("    @Transient");
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Incompleteness,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.JavaEntity,
                Entity = entityMap.Entity.Name,
                Property = propertyMap.Property.Name,
                Category = MappingFactCategory.ForeignKeyColumns,
                Reason = $"The inverse one-to-one to '{relation.TargetEntity}' needs mappedBy naming the owning navigation, and "
                    + "no owning counterpart takes part in the conversion; the member is written @Transient.",
            });
            return;
        }

        Import($"{Jakarta}.OneToOne");
        code.AppendLine($"    @OneToOne(mappedBy = \"{counterpart}\")");
    }

    /// <summary>
    /// A collection: @OneToMany(mappedBy) where the child's owning navigation is at hand,
    /// otherwise the unidirectional form with @JoinColumn naming the child's foreign key
    /// column - JPA's spelling of NHibernate's key element. Without any known column the
    /// JPA default is written out with a record; a bare @OneToMany would mean a join table.
    /// </summary>
    private void AppendCollection(EntityMap entityMap, Relation relation, PropertyMap propertyMap, StringBuilder code)
    {
        Import($"{Jakarta}.OneToMany");

        var counterpart = OwningCounterpart(entityMap, relation);
        if (counterpart is not null)
        {
            code.AppendLine($"    @OneToMany(mappedBy = \"{counterpart}\")");
            return;
        }

        code.AppendLine("    @OneToMany");

        var columns = relation.ColumnPairs.Count > 0
            ? relation.ColumnPairs.Select(pair => (pair.Source.ColumnName ?? pair.Source.Property.Name, ColumnOf(pair.Target))).ToList()
            : StatedForeignKeyColumns(relation) is { } stated && entityMap.PrimaryKey is { } key && key.Parts.Count == stated.Count
                ? stated.Select((column, index) => (column, ColumnOf(key.Parts[index].PropertyMap))).ToList()
                : null;

        if (columns is null)
        {
            if (entityMap.PrimaryKey is not { } ownKey)
            {
                return;
            }

            columns = ownKey.Parts.Select(p => ($"{entityMap.Entity.Name}_{ColumnOf(p.PropertyMap)}", ColumnOf(p.PropertyMap))).ToList();
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Convention,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.JavaEntity,
                Entity = entityMap.Entity.Name,
                Property = propertyMap.Property.Name,
                Category = MappingFactCategory.ForeignKeyColumns,
                Reason = $"No foreign key column is known for the collection of '{relation.TargetEntity}'; the artifact writes the "
                    + "JPA default name, entity_keyColumn, made explicit (decision 077).",
            });
        }

        AppendJoinColumns(code, columns.Select(c => RenderJoinColumn(c.Item1, c.Item2, null, false, false)).ToList());
    }

    /// <summary>
    /// A many-to-many the synthesis of decision 005 left as it was - the junction facts
    /// were incomplete, and the resolution phase has reported it - is written with what is
    /// known; the inverse side names the owning navigation through mappedBy where it can.
    /// </summary>
    private void AppendManyToMany(EntityMap entityMap, Relation relation, PropertyMap propertyMap, StringBuilder code)
    {
        Import($"{Jakarta}.ManyToMany");

        var counterpart = relation.Role == RelationRole.Inverse
            ? FindEntityMap(relation.TargetEntity)?.Relations
                .FirstOrDefault(r => r.Cardinality == Cardinality.ManyToMany && r.Role == RelationRole.Owning
                    && FindEntityMap(r.TargetEntity) == entityMap)?.SourceNavigationProperty
            : null;

        if (counterpart is not null)
        {
            code.AppendLine($"    @ManyToMany(mappedBy = \"{counterpart}\")");
            return;
        }

        code.AppendLine("    @ManyToMany");

        var facts = StatedJunctionFacts(relation);
        var own = StatedForeignKeyColumns(relation);
        if (facts?.Table is null && own is null)
        {
            return;
        }

        Import($"{Jakarta}.JoinTable");
        Import($"{Jakarta}.JoinColumn");

        var arguments = new List<string>();
        if (facts?.Table is not null)
        {
            arguments.Add($"name = \"{facts.Table}\"");
        }

        if (facts?.Schema is not null)
        {
            arguments.Add($"schema = \"{facts.Schema}\"");
        }

        if (own is not null)
        {
            arguments.Add($"joinColumns = {{ {string.Join(", ", own.Select(c => $"@JoinColumn(name = \"{c}\")"))} }}");
        }

        if (facts?.TargetColumns is not null)
        {
            arguments.Add($"inverseJoinColumns = {{ {string.Join(", ", facts.TargetColumns.Select(c => $"@JoinColumn(name = \"{c}\")"))} }}");
        }

        code.AppendLine($"    @JoinTable({string.Join(", ", arguments)})");
    }

    /// <summary>
    /// The navigation on the far side that owns the relation back to this entity, when the
    /// far side takes part in the conversion: what mappedBy names. The pairs decide where
    /// both sides carry them; otherwise the one owning relation towards this entity.
    /// </summary>
    private string? OwningCounterpart(EntityMap entityMap, Relation relation)
    {
        var target = FindEntityMap(relation.TargetEntity);
        if (target is null)
        {
            return null;
        }

        var candidates = target.Relations
            .Where(r => r.Role == RelationRole.Owning
                && r.Cardinality is Cardinality.ManyToOne or Cardinality.OneToOne
                && FindEntityMap(r.TargetEntity) == entityMap
                && r.SourceNavigationProperty is not null)
            .ToList();

        if (relation.ColumnPairs.Count > 0)
        {
            var columns = relation.ColumnPairs.Select(p => p.Source.ColumnName ?? p.Source.Property.Name).ToList();
            var matched = candidates.FirstOrDefault(r =>
                r.ColumnPairs.Count == columns.Count
                && r.ColumnPairs.Select(p => p.Source.ColumnName ?? p.Source.Property.Name).SequenceEqual(columns, StringComparer.Ordinal));

            if (matched is not null)
            {
                return matched.SourceNavigationProperty;
            }
        }

        return candidates.Count == 1 ? candidates[0].SourceNavigationProperty : null;
    }

    /// <summary>
    /// The strategy, always concrete (decision 076). Auto resolves through the profile of
    /// the implementation with its default generator written out; an unstated strategy is
    /// the JPA reading of a bare @Id - the application assigns - and is reported as the
    /// target's convention, like NHibernate's assigned; the named mechanisms map onto
    /// GenerationType with the generator's canonical parameters (decision 020).
    /// </summary>
    private void AppendGeneration(EntityMap entityMap, PrimaryKeyPart part, StringBuilder code)
    {
        var property = part.PropertyMap.Property.Name;

        if (part.SourceStrategyName is not null && !IsForeign(part))
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.JavaEntity,
                Entity = entityMap.Entity.Name,
                Property = property,
                Category = MappingFactCategory.PrimaryKeyStrategy,
                Reason = $"The source names the generator '{part.SourceStrategyName}', which the JPA annotations cannot express; "
                    + $"the strategy is written as {part.Strategy} and the name is dropped (decision 021).",
            });
        }

        if (IsForeign(part))
        {
            return; // the value comes from the relation carrying @MapsId
        }

        switch (part.Strategy)
        {
            case PrimaryKeyStrategy.Assigned:
                return;

            case PrimaryKeyStrategy.Unspecified:
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Convention,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.JavaEntity,
                    Entity = entityMap.Entity.Name,
                    Property = property,
                    Category = MappingFactCategory.PrimaryKeyStrategy,
                    Reason = "No generation strategy was stated; an @Id without @GeneratedValue is assigned by the application "
                        + "in JPA, which is a convention of the target, not a fact of the source.",
                });
                return;

            case PrimaryKeyStrategy.Identity:
                AppendGeneratedValue(code, "IDENTITY", null);
                return;

            case PrimaryKeyStrategy.Uuid:
                AppendGeneratedValue(code, "UUID", null);
                return;

            case PrimaryKeyStrategy.Increment:
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Loss,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.JavaEntity,
                    Entity = entityMap.Entity.Name,
                    Property = property,
                    Category = MappingFactCategory.PrimaryKeyStrategy,
                    Reason = "The Increment mechanism has no counterpart in JPA and Hibernate's @GenericGenerator for it is "
                        + "deprecated; the key is written without generation, assigned by the application.",
                });
                return;

            case PrimaryKeyStrategy.Auto:
                AppendResolvedAuto(entityMap, part, code);
                return;

            case PrimaryKeyStrategy.Sequence:
                AppendSequence(entityMap, part, code, convention: null);
                return;

            case PrimaryKeyStrategy.HiLo:
                if (part.StrategyParameters.ContainsKey(GeneratorParameter.SequenceName))
                {
                    AppendSequence(entityMap, part, code,
                        "The hi/lo mechanism over a sequence is written as SEQUENCE with the block size as allocationSize - "
                        + "the pooled optimizer of the implementation over a sequence is the same mechanism.");
                }
                else
                {
                    AppendTable(entityMap, part, code);
                }

                return;
        }
    }

    private static bool IsForeign(PrimaryKeyPart part)
        => string.Equals(part.SourceStrategyName, "foreign", StringComparison.OrdinalIgnoreCase);

    private void AppendGeneratedValue(StringBuilder code, string strategy, string? generator)
    {
        Import($"{Jakarta}.GeneratedValue");
        Import($"{Jakarta}.GenerationType");

        var named = generator is null ? string.Empty : $", generator = \"{generator}\"";
        code.AppendLine($"    @GeneratedValue(strategy = GenerationType.{strategy}{named})");
    }

    private void AppendResolvedAuto(EntityMap entityMap, PrimaryKeyPart part, StringBuilder code)
    {
        var mechanism = Profile.AutoStrategy;

        Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Convention,
            Framework = Descriptor.Framework,
            Artifact = ConversionContentType.JavaEntity,
            Entity = entityMap.Entity.Name,
            Property = part.PropertyMap.Property.Name,
            Category = MappingFactCategory.PrimaryKeyStrategy,
            Reason = $"The source leaves the mechanism to the framework (Auto); {Profile.Implementation} resolves it to {mechanism} "
                + "on the pinned dialect, and the artifact writes that mechanism out, because AUTO means something else "
                + "under the other implementation (decisions 076 and 077).",
        });

        switch (mechanism)
        {
            case PrimaryKeyStrategy.Sequence:
                AppendSequence(entityMap, part, code, convention: null, silent: true);
                break;
            case PrimaryKeyStrategy.Identity:
                AppendGeneratedValue(code, "IDENTITY", null);
                break;
            default:
                AppendTable(entityMap, part, code, silent: true);
                break;
        }
    }

    /// <summary>
    /// SEQUENCE with its generator written out: the sequence name from the canonical
    /// parameters, or the implementation's default name made explicit with a record, and
    /// the allocation size likewise (decisions 020 and 076).
    /// </summary>
    private void AppendSequence(EntityMap entityMap, PrimaryKeyPart part, StringBuilder code, string? convention, bool silent = false)
    {
        var property = part.PropertyMap.Property.Name;
        var parameters = part.StrategyParameters;
        var generatorName = $"{entityMap.Entity.Name}_{property}_gen";

        if (!parameters.TryGetValue(GeneratorParameter.SequenceName, out var sequence))
        {
            sequence = $"{entityMap.Entity.Name}_SEQ";
            if (!silent)
            {
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Convention,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.JavaEntity,
                    Entity = entityMap.Entity.Name,
                    Property = property,
                    Category = MappingFactCategory.PrimaryKeyStrategy,
                    Reason = $"No sequence name was stated; the artifact writes '{sequence}', the implementation's default made explicit.",
                });
            }
        }

        if (!parameters.TryGetValue(GeneratorParameter.BlockSize, out var blockSize))
        {
            blockSize = Profile.DefaultAllocationSize.ToString();
            if (!silent)
            {
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Convention,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.JavaEntity,
                    Entity = entityMap.Entity.Name,
                    Property = property,
                    Category = MappingFactCategory.PrimaryKeyStrategy,
                    Reason = $"No block size was stated; the artifact writes allocationSize = {blockSize}, the implementation's default made explicit.",
                });
            }
        }

        if (convention is not null)
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Convention,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.JavaEntity,
                Entity = entityMap.Entity.Name,
                Property = property,
                Category = MappingFactCategory.PrimaryKeyStrategy,
                Reason = convention,
            });
        }

        var arguments = new List<string> { $"name = \"{generatorName}\"", $"sequenceName = \"{sequence}\"" };
        if (parameters.TryGetValue(GeneratorParameter.Schema, out var schema))
        {
            arguments.Add($"schema = \"{schema}\"");
        }

        if (parameters.TryGetValue(GeneratorParameter.InitialValue, out var initial))
        {
            arguments.Add($"initialValue = {initial}");
        }

        arguments.Add($"allocationSize = {blockSize}");

        ReportUnusableParameters(entityMap, part, GeneratorParameter.CounterTable, GeneratorParameter.CounterValueColumn,
            GeneratorParameter.CounterKeyColumn, GeneratorParameter.CounterKeyValue);

        Import($"{Jakarta}.SequenceGenerator");
        AppendGeneratedValue(code, "SEQUENCE", generatorName);
        code.AppendLine($"    @SequenceGenerator({string.Join(", ", arguments)})");
    }

    /// <summary>
    /// TABLE with its generator: the counter's table and columns from the canonical
    /// parameters (decision 020 introduced the key column and key value for exactly this
    /// generator); what is unstated stays with the implementation's default and is reported.
    /// </summary>
    private void AppendTable(EntityMap entityMap, PrimaryKeyPart part, StringBuilder code, bool silent = false)
    {
        var property = part.PropertyMap.Property.Name;
        var parameters = part.StrategyParameters;
        var generatorName = $"{entityMap.Entity.Name}_{property}_gen";
        var arguments = new List<string> { $"name = \"{generatorName}\"" };

        if (parameters.TryGetValue(GeneratorParameter.CounterTable, out var table))
        {
            arguments.Add($"table = \"{table}\"");
        }

        if (parameters.TryGetValue(GeneratorParameter.Schema, out var schema))
        {
            arguments.Add($"schema = \"{schema}\"");
        }

        if (parameters.TryGetValue(GeneratorParameter.CounterKeyColumn, out var keyColumn))
        {
            arguments.Add($"pkColumnName = \"{keyColumn}\"");
        }

        if (parameters.TryGetValue(GeneratorParameter.CounterValueColumn, out var valueColumn))
        {
            arguments.Add($"valueColumnName = \"{valueColumn}\"");
        }

        if (parameters.TryGetValue(GeneratorParameter.CounterKeyValue, out var keyValue))
        {
            arguments.Add($"pkColumnValue = \"{keyValue}\"");
        }

        if (parameters.TryGetValue(GeneratorParameter.InitialValue, out var initial))
        {
            arguments.Add($"initialValue = {initial}");
        }

        if (!parameters.TryGetValue(GeneratorParameter.BlockSize, out var blockSize))
        {
            blockSize = Profile.DefaultAllocationSize.ToString();
        }

        arguments.Add($"allocationSize = {blockSize}");

        if (!silent)
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Convention,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.JavaEntity,
                Entity = entityMap.Entity.Name,
                Property = property,
                Category = MappingFactCategory.PrimaryKeyStrategy,
                Reason = "The hi/lo mechanism over a counter table is written as TABLE with the block size as allocationSize; "
                    + "parameters the source did not state stay with the implementation's default.",
            });
        }

        ReportUnusableParameters(entityMap, part, GeneratorParameter.SequenceName);

        Import($"{Jakarta}.TableGenerator");
        AppendGeneratedValue(code, "TABLE", generatorName);
        code.AppendLine($"    @TableGenerator({string.Join(", ", arguments)})");
    }

    private void ReportUnusableParameters(EntityMap entityMap, PrimaryKeyPart part, params GeneratorParameter[] unusable)
    {
        foreach (var parameter in unusable.Where(part.StrategyParameters.ContainsKey))
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.JavaEntity,
                Entity = entityMap.Entity.Name,
                Property = part.PropertyMap.Property.Name,
                Category = MappingFactCategory.PrimaryKeyStrategy,
                Reason = $"The generator parameter {parameter} has no place in the generator the artifact writes; it is dropped (decision 020).",
            });
        }

        foreach (var literal in part.SourceStrategyParameters.Where(p => p.Key != "property"))
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.JavaEntity,
                Entity = entityMap.Entity.Name,
                Property = part.PropertyMap.Property.Name,
                Category = MappingFactCategory.PrimaryKeyStrategy,
                Reason = $"The generator parameter '{literal.Key}' is local to the source's generator and has no place in the JPA annotations; it is dropped (decision 020).",
            });
        }
    }

    /// <summary>
    /// @Column with every fact the map states, the column name always (decision 076): the
    /// name restates the universal default where the source stated none, so it carries no
    /// record (decision 067). The literal SQL type reaches columnDefinition (decision 052);
    /// the precision facet reaches the attribute its column family gives it (decision 079).
    /// </summary>
    private void AppendColumn(EntityMap entityMap, PropertyMap propertyMap, StringBuilder code, bool isKey, bool readOnly = false)
    {
        Import($"{Jakarta}.Column");

        var arguments = new List<string> { $"name = \"{ColumnOf(propertyMap)}\"" };

        if (propertyMap.Length is { } length)
        {
            arguments.Add($"length = {length}");
        }

        AppendPrecision(entityMap, propertyMap, arguments);

        // A key column is never nullable, so the claim is not restated on it.
        if (!isKey && propertyMap.IsNullable is { } nullable)
        {
            arguments.Add($"nullable = {(nullable ? "true" : "false")}");
        }

        if (propertyMap.SourceSqlType is not null)
        {
            arguments.Add($"columnDefinition = \"{propertyMap.SourceSqlType}\"");
        }

        if (readOnly)
        {
            arguments.Add("insertable = false");
            arguments.Add("updatable = false");
        }

        code.AppendLine($"    @Column({string.Join(", ", arguments)})");
    }

    /// <summary>
    /// The precision facet under the spelling its column family gives it (decision 079):
    /// secondPrecision on a time or timestamp column, precision on a decimal one. Both stand
    /// beside columnDefinition exactly as length does - facets are not dropped because a
    /// literal type might already contain them (decision 052). Renaming the spelling of a
    /// fact the source stated carries no record; what does not reach the column at all is a
    /// loss: a date column has no fractional seconds, and a scale has no place on a temporal
    /// one.
    /// </summary>
    private void AppendPrecision(EntityMap entityMap, PropertyMap propertyMap, List<string> arguments)
    {
        switch (JpaColumnPrecision.Classify(propertyMap.Type, propertyMap.Property.Type))
        {
            case JpaPrecisionKind.FractionalSeconds:
                if (propertyMap.Precision is { } seconds)
                {
                    arguments.Add($"secondPrecision = {seconds}");
                }

                if (propertyMap.Scale is not null)
                {
                    ReportPrecisionLoss(entityMap, propertyMap,
                        "A scale belongs to a decimal column; on a time or timestamp column @Column has no "
                        + "attribute for it, so it is dropped (decision 079).");
                }

                return;

            case JpaPrecisionKind.Date:
                if (propertyMap.Precision is not null || propertyMap.Scale is not null)
                {
                    ReportPrecisionLoss(entityMap, propertyMap,
                        "A date column has no fractional-second part: @Column gives precision to a decimal column "
                        + "and secondPrecision to a time or timestamp one, so the stated value reaches neither "
                        + "and is dropped (decision 079).");
                }

                return;

            default:
                if (propertyMap.Precision is { } precision)
                {
                    arguments.Add($"precision = {precision}");
                }

                if (propertyMap.Scale is { } scale)
                {
                    arguments.Add($"scale = {scale}");
                }

                return;
        }
    }

    private void ReportPrecisionLoss(EntityMap entityMap, PropertyMap propertyMap, string reason)
        => Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Loss,
            Framework = Descriptor.Framework,
            Artifact = ConversionContentType.JavaEntity,
            Entity = entityMap.Entity.Name,
            Property = propertyMap.Property.Name,
            Category = MappingFactCategory.PrecisionAndScale,
            Reason = reason,
        });

    /// <summary>
    /// The field itself, private, with the initializer the model carries where Java can
    /// spell it: an empty collection of the target's own kind (decision 035 carried over),
    /// a literal both languages share; anything else is a loss (decision 077). The
    /// accessors are collected here and written after the last member.
    /// </summary>
    private void AppendField(EntityMap entityMap, Property property, StringBuilder code, bool forceWrapper = false)
    {
        var langType = property.Type
            ?? throw new NotSupportedException($"Property '{property.Name}' has no language type.");

        var type = JavaTypeConvertor.ToString(langType, forceWrapper);
        var typeImport = JavaTypeConvertor.ImportFor(langType);
        if (typeImport is not null)
        {
            Import(typeImport);
        }

        if (langType.Category == LangTypeCategory.Collection && JavaTypeConvertor.ImportFor(langType.ElementType!) is { } elementImport)
        {
            Import(elementImport);
        }

        var initializer = RenderInitializer(entityMap, property, langType);
        code.AppendLine($"    private {type} {property.Name}{initializer};");

        // Modifiers translate rather than travel (decision 076): what Java means implicitly
        // or what is a compile-time device of C# - virtual, override, sealed, new, required -
        // drops without a word; anything else has no counterpart and is a loss.
        foreach (var modifier in property.OtherModifiers.Where(m => m is not ("virtual" or "override" or "sealed" or "new" or "required")))
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.JavaEntity,
                Entity = entityMap.Entity.Name,
                Property = property.Name,
                Reason = $"The modifier '{modifier}' has no counterpart in Java; it was dropped.",
            });
        }

        accessors.Add((type, property.Name, langType));
    }

    private string RenderInitializer(EntityMap entityMap, Property property, LangType langType)
    {
        if (langType.Category == LangTypeCategory.Collection)
        {
            var (expression, import) = JavaTypeConvertor.EmptyCollection(langType.CollectionKind ?? CollectionKind.Unspecified);
            Import(import);

            if (!string.IsNullOrWhiteSpace(property.DefaultValue) && !IsEmptyCollectionInitializer(property.DefaultValue))
            {
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Loss,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.JavaEntity,
                    Entity = entityMap.Entity.Name,
                    Property = property.Name,
                    Reason = $"The initializer '{property.DefaultValue}' of the collection is replaced by an empty collection of the target; its content is dropped.",
                });
            }

            return $" = {expression}";
        }

        if (string.IsNullOrWhiteSpace(property.DefaultValue))
        {
            return string.Empty;
        }

        var text = property.DefaultValue.Trim();
        var scalar = langType.Category == LangTypeCategory.Scalar ? langType.ScalarType : null;

        if (text is "null" or "true" or "false" || (text.Length >= 2 && text[0] == '"' && text[^1] == '"'))
        {
            return $" = {text}";
        }

        var number = text.TrimEnd('m', 'M', 'd', 'D', 'f', 'F', 'L', 'l');
        if (number.Length > 0 && decimal.TryParse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)
            && !number.Contains('e', StringComparison.OrdinalIgnoreCase))
        {
            return scalar switch
            {
                ScalarType.Decimal => $" = new BigDecimal(\"{number}\")",
                ScalarType.Float => $" = {number}f",
                ScalarType.Long => $" = {number}L",
                _ => $" = {number}",
            };
        }

        Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Loss,
            Framework = Descriptor.Framework,
            Artifact = ConversionContentType.JavaEntity,
            Entity = entityMap.Entity.Name,
            Property = property.Name,
            Reason = $"The initializer '{text}' is not a literal both languages spell alike; it was dropped.",
        });

        return string.Empty;
    }

    private static bool IsEmptyCollectionInitializer(string text)
    {
        var trimmed = text.Trim();
        return trimmed is "[]" or "new()"
            || (trimmed.StartsWith("new ", StringComparison.Ordinal) && trimmed.EndsWith("()", StringComparison.Ordinal));
    }

    protected override void BuildEnforcedMembers(EntityMap entityMap, EntityArtifact artifact)
    {
        if (entityMap.PrimaryKey is not { Parts.Count: > 1 } key)
        {
            return;
        }

        // The key class JPA demands even for the flat key (decision 006): nested and static,
        // named after the source's key class where recorded (decision 031), serializable,
        // with a no-arg constructor, an all-args one for lookups, equals and hashCode.
        Import("java.io.Serializable");
        Import("java.util.Objects");

        var name = KeyClassName(entityMap);
        var parts = key.Parts.Select(p => (Type: JavaTypeConvertor.ToString(p.PropertyMap.Property.Type!, forceWrapper: true), p.PropertyMap.Property.Name)).ToList();
        var code = artifact.Code;

        code.AppendLine();
        code.AppendLine($"    public static class {name} implements Serializable {{");

        foreach (var (type, partName) in parts)
        {
            code.AppendLine($"        private {type} {partName};");
        }

        code.AppendLine();
        code.AppendLine($"        public {name}() {{");
        code.AppendLine("        }");
        code.AppendLine();
        code.AppendLine($"        public {name}({string.Join(", ", parts.Select(p => $"{p.Type} {p.Name}"))}) {{");
        foreach (var (_, partName) in parts)
        {
            code.AppendLine($"            this.{partName} = {partName};");
        }

        code.AppendLine("        }");
        code.AppendLine();
        code.AppendLine("        @Override");
        code.AppendLine("        public boolean equals(Object obj) {");
        code.AppendLine($"            return obj instanceof {name} other");
        foreach (var (_, partName) in parts)
        {
            code.AppendLine($"                && Objects.equals({partName}, other.{partName})");
        }

        code.Length -= Environment.NewLine.Length;
        code.AppendLine(";");
        code.AppendLine("        }");
        code.AppendLine();
        code.AppendLine("        @Override");
        code.AppendLine("        public int hashCode() {");
        code.AppendLine($"            return Objects.hash({string.Join(", ", parts.Select(p => p.Name))});");
        code.AppendLine("        }");
        code.AppendLine("    }");
    }

    private string KeyClassName(EntityMap entityMap)
    {
        if (entityMap.PrimaryKey?.SourceKeyClass is { } recorded)
        {
            return recorded.ClassName;
        }

        var name = $"{entityMap.Entity.Name}Id";

        if (reportedKeyClassNames.Add(entityMap))
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Convention,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.JavaEntity,
                Entity = entityMap.Entity.Name,
                Category = MappingFactCategory.PrimaryKey,
                Reason = $"The source recorded no key class, so the key class of the composite key is named '{name}' by convention (decision 077).",
            });
        }

        return name;
    }

    private readonly HashSet<EntityMap> reportedKeyClassNames = [];

    protected override IEnumerable<ConversionSource> FinalizeBuild(EntityMap entityMap, EntityArtifact artifact)
    {
        var code = artifact.Code;

        // The accessors: Java's spelling of a property, written for every field after the
        // last member. What the model states about them is honored, and a field nobody
        // stated anything about gets both, because a private field without them is
        // unreachable.
        foreach (var (type, name, langType) in accessors)
        {
            var property = entityMap.Entity.Properties.First(p => p.Name == name);
            var getter = property.HasGetter || !property.HasSetter;
            var setter = property.HasSetter || !property.HasGetter;
            var capitalized = JavaClass.Capitalize(name);
            var prefix = type == "boolean" ? "is" : "get";

            if (getter)
            {
                code.AppendLine();
                code.AppendLine($"    public {type} {prefix}{capitalized}() {{");
                code.AppendLine($"        return {name};");
                code.AppendLine("    }");
            }

            if (setter)
            {
                code.AppendLine();
                code.AppendLine($"    public void set{capitalized}({type} value) {{");
                code.AppendLine($"        this.{name} = value;");
                code.AppendLine("    }");
            }
        }

        code.AppendLine("}");

        var header = new StringBuilder();
        if (entityMap.Entity.Namespace is not null)
        {
            header.AppendLine($"package {entityMap.Entity.Namespace};");
            header.AppendLine();
        }

        foreach (var import in imports)
        {
            header.AppendLine($"import {import};");
        }

        if (imports.Count > 0)
        {
            header.AppendLine();
        }

        yield return new ConversionSource
        {
            ContentType = ConversionContentType.JavaEntity,
            Content = header + code.ToString(),
        };
    }

    /// <summary>The column of a property: the stated name, or the property's own, which every framework defaults to (decision 067).</summary>
    protected static string ColumnOf(PropertyMap propertyMap) => propertyMap.ColumnName ?? propertyMap.Property.Name;
}
