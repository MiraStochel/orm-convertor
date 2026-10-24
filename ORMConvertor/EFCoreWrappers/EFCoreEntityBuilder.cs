using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Convertors;
using Common.Sql;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using System.Text;

namespace EFCoreWrappers;

public class EFCoreEntityBuilder : AbstractEntityBuilder
{
    public override TargetFrameworkDescriptor Descriptor => EFCoreDescriptor.Instance;

    protected override void BuildImports(EntityMap entityMap, EntityArtifact artifact)
    {
        if (entityMap.Entity.Namespace != null)
        {
            artifact.Code.AppendLine($"namespace {entityMap.Entity.Namespace};");
            artifact.Code.AppendLine();
        }

        // [PrimaryKey], [Keyless], [Precision] and [Index] all live in this namespace and
        // each is emitted under its own condition, so the import restates all four: it
        // follows from the elements that are generated, not from the framework being EF Core.
        bool keyAttribute = entityMap.PrimaryKey is null || entityMap.PrimaryKey.Parts.Count > 1;
        bool precisionAttribute = entityMap.PropertyMaps.Any(pm => pm.Precision != null);
        bool indexAttribute = entityMap.UniqueConstraints.Count > 0;
        bool unicodeAttribute = entityMap.PropertyMaps.Any(NeedsUnicodeAttribute);

        if (keyAttribute || precisionAttribute || indexAttribute || unicodeAttribute)
        {
            artifact.Code.AppendLine("using Microsoft.EntityFrameworkCore;");
        }

        artifact.Code.AppendLine("using System.ComponentModel.DataAnnotations;");
        artifact.Code.AppendLine("using System.ComponentModel.DataAnnotations.Schema;");

        artifact.Code.AppendLine();
    }

    protected override void BuildTableSchema(EntityMap entityMap, EntityArtifact artifact)
    {
        if (entityMap.Table != null)
        {
            var schemaIfPresent = entityMap.Schema != null
                ? $", Schema = \"{entityMap.Schema}\""
                : string.Empty;

            artifact.Code.AppendLine($"[Table(\"{entityMap.Table}\"{schemaIfPresent})]");

            // Where the table and the schema stand in the artifact (decision 125).
            artifact.Mark(artifact.Code, MappingFactCategory.TableName, null, $"\"{entityMap.Table}\"");
            if (entityMap.Schema != null)
            {
                artifact.Mark(artifact.Code, MappingFactCategory.SchemaName, null, schemaIfPresent.TrimStart(',', ' '));
            }
        }

        if (entityMap.PrimaryKey is null)
        {
            // EF Core would otherwise derive a key by convention from a property named
            // Id or {TypeName}Id. The model holds no key, so none may appear in the output.
            artifact.Code.AppendLine("[Keyless]");
        }
        else if (entityMap.PrimaryKey.Parts.Count > 1)
        {
            var keyNames = string.Join(", ", entityMap.PrimaryKey.Parts.Select(p => $"nameof({p.PropertyMap.Property.Name})"));
            artifact.Code.AppendLine($"[PrimaryKey({keyNames})]");

            foreach (var part in entityMap.PrimaryKey.Parts)
            {
                artifact.Mark(artifact.Code, MappingFactCategory.PrimaryKey, part.PropertyMap.Property.Name, $"nameof({part.PropertyMap.Property.Name})");
            }
        }

        AppendUniqueConstraints(entityMap, artifact);

        var modifier = AccessModifierConvertor.ToModifierString(entityMap.Entity.AccessModifier);

        artifact.Code.AppendLine($"{modifier} class {entityMap.Entity.Name}");
        artifact.Code.AppendLine("{");
    }

    /// <summary>
    /// Unique constraints as class-level [Index(…, IsUnique = true)] annotations (decision
    /// 055) - EF Core's only annotation for the fact, and a class annotation is exactly
    /// what this step writes. The parts are emitted through nameof so that the artifact
    /// stops compiling if a property is later renamed, the way the composite key does.
    ///
    /// A constraint naming a property the entity does not declare cannot be written at all:
    /// nameof would not compile and a string would name nothing. That is a gap in the model
    /// rather than a narrowing of EF Core, so it is reported as incompleteness and the
    /// remaining constraints are still emitted. A constraint over a property the source
    /// states as not persisted (decision 072) has no column to constrain either - EF Core
    /// refuses an index over an ignored property - and is dropped with a loss record.
    /// </summary>
    private void AppendUniqueConstraints(EntityMap entityMap, EntityArtifact artifact)
    {
        foreach (var constraint in entityMap.UniqueConstraints)
        {
            var transient = constraint.PropertyNames
                .Where(name => entityMap.PropertyMaps.Any(pm => pm.Property.Name == name && pm.IsTransient))
                .ToList();

            if (transient.Count > 0)
            {
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Loss,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.CSharpEntity,
                    Entity = entityMap.Entity.Name,
                    Property = constraint.PropertyNames.Count == 1 ? constraint.PropertyNames[0] : null,
                    Category = MappingFactCategory.UniqueConstraint,
                    Reason = $"The unique constraint over ({string.Join(", ", constraint.PropertyNames)}) covers "
                        + $"({string.Join(", ", transient)}), which the source states {(transient.Count == 1 ? "is" : "are")} not persisted; "
                        + "EF Core cannot index an ignored property, so the constraint is dropped (decision 072).",
                });
                continue;
            }

            var missing = constraint.PropertyNames
                .Where(name => !entityMap.PropertyMaps.Any(pm => pm.Property.Name == name))
                .ToList();

            if (missing.Count > 0)
            {
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Incompleteness,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.CSharpEntity,
                    Entity = entityMap.Entity.Name,
                    Category = MappingFactCategory.UniqueConstraint,
                    Reason = $"The unique constraint over ({string.Join(", ", constraint.PropertyNames)}) names "
                        + $"{(missing.Count == 1 ? "a property" : "properties")} the entity does not declare "
                        + $"({string.Join(", ", missing)}); it is not written.",
                });
                continue;
            }

            var indexed = IndexedProperties(entityMap, constraint);

            if (indexed is null)
            {
                continue;
            }

            var parts = string.Join(", ", indexed.Select(name => $"nameof({name})"));
            var named = constraint.Name is null ? string.Empty : $", Name = \"{constraint.Name}\"";

            var rendered = $"[Index({parts}, IsUnique = true{named})]";
            artifact.Code.AppendLine(rendered);
            artifact.Mark(
                artifact.Code,
                MappingFactCategory.UniqueConstraint,
                constraint.PropertyNames.Count == 1 ? constraint.PropertyNames[0] : null,
                rendered);
        }
    }

    /// <summary>
    /// The properties an index over the constraint names. [Index] cannot name a navigation,
    /// so a member that is one is spelled by the properties of its foreign key - the very
    /// names [ForeignKey] on the navigation gives, an existing scalar or the property the
    /// builder supplies (decisions 012 and 130). Where those names do not exist - the
    /// columns are unknown, the type of the key they reference is, or the navigation holds
    /// no foreign key in this table - the constraint is reported and null returned.
    /// </summary>
    private List<string>? IndexedProperties(EntityMap entityMap, UniqueConstraint constraint)
    {
        var indexed = new List<string>();

        foreach (var name in constraint.PropertyNames)
        {
            if (!entityMap.Relations.Any(r => r.SourceNavigationProperty == name))
            {
                indexed.Add(name);
                continue;
            }

            var relation = entityMap.ForeignKeyNamedBy(name);
            var foreignKey = relation is null ? null : ForeignKeyPropertyNames(entityMap, relation, name);

            if (foreignKey is null)
            {
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Incompleteness,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.CSharpEntity,
                    Entity = entityMap.Entity.Name,
                    Property = constraint.PropertyNames.Count == 1 ? name : null,
                    Category = MappingFactCategory.UniqueConstraint,
                    Reason = $"The unique constraint over ({string.Join(", ", constraint.PropertyNames)}) covers the navigation '{name}', "
                        + (relation is null
                            ? "which holds no foreign key in this entity's table"
                            : "whose foreign key properties cannot be named - its columns or the type of the key they reference are unknown")
                        + "; [Index] names properties only, so the constraint is not written (decision 130).",
                });
                return null;
            }

            indexed.AddRange(foreignKey.Where(p => !indexed.Contains(p)));
        }

        return indexed;
    }

    /// <summary>
    /// The names of the scalar properties a foreign key consists of, in the order of the key
    /// it points at: the entity's own where it declares one over the column, else the name
    /// the builder supplies (decision 012). Null where the columns are unknown or a referenced
    /// key part has no language type, so no property can be declared.
    /// </summary>
    private static List<string>? ForeignKeyPropertyNames(EntityMap entityMap, Relation relation, string navigationProperty)
    {
        if (relation.ColumnPairs.Count == 0)
        {
            return null;
        }

        var names = new List<string>();

        foreach (var pair in relation.ColumnPairs)
        {
            // A column is not a property until it has a language type, so a property map carrying
            // only the column does not count as one the entity already has.
            var existing = entityMap.PropertyMaps.FirstOrDefault(pm =>
                pm.Property.Name == pair.Source.Property.Name && pm.Property.Type is not null);

            if (existing is not null)
            {
                names.Add(existing.Property.Name);
            }
            else if (pair.Target.Property.Type is null)
            {
                return null;
            }
            else
            {
                names.Add(EFCoreColumnMember.NameFor(navigationProperty, pair.Target));
            }
        }

        return names;
    }

    protected override void BuildPrimaryKey(EntityMap entityMap, EntityArtifact artifact)
    {
        if (entityMap.PrimaryKey is null)
        {
            return;
        }

        bool composite = entityMap.PrimaryKey.Parts.Count > 1;

        foreach (var part in entityMap.PrimaryKey.Parts)
        {
            var propertyMap = part.PropertyMap;

            ReportNullableKeyPartLoss(entityMap, propertyMap.Property, ConversionContentType.CSharpEntity);
            ReportVersionIncrementLoss(entityMap, propertyMap);

            // For a simple key [Key] goes on the property; for a composite key the class-level
            // [PrimaryKey(...)] attribute (see BuildTableSchema) defines it and [Key] is not emitted.
            AppendPropertyAttributes(artifact, propertyMap, isPrimaryKey: !composite);
            AppendKeyStrategyAttribute(artifact, entityMap, part, composite);
            AppendSignature(artifact, entityMap, propertyMap, BuildPropertySignature(propertyMap.Property, isPrimaryKey: true, NeedsPrivateSetter(entityMap, propertyMap)));
        }
    }

    /// <summary>
    /// The declaration line of a property, and the place every fact of the property that no
    /// annotation stated shows itself (decision 125): the nullability a type carries, the key
    /// generation EF Core applies by convention, the unicode default.
    /// </summary>
    private static void AppendSignature(EntityArtifact artifact, EntityMap entityMap, PropertyMap propertyMap, string signature)
    {
        artifact.Code.AppendLine($"    {signature}");
        artifact.MarkRest(artifact.Code, entityMap, propertyMap.Property.Name, signature);
        artifact.Code.AppendLine();
    }

    protected override void BuildProperties(EntityMap entityMap, EntityArtifact artifact)
    {
        foreach (var propertyMap in entityMap.PropertyMaps)
        {
            if (entityMap.PrimaryKey?.Parts.Any(p => p.PropertyMap.Property.Name == propertyMap.Property.Name) == true)
            {
                continue; // handled in BuildPrimaryKey
            }

            if (entityMap.Relations.Any(r => r.SourceNavigationProperty == propertyMap.Property.Name))
            {
                continue; // navigation property - handled in BuildForeignKey
            }

            if (propertyMap.IsTransient)
            {
                // The source states there is no column behind the property (decision 072).
                // [NotMapped] is the whole statement: a column annotation beside it would
                // claim a mapping EF Core does not read on an ignored property.
                artifact.Code.AppendLine("    [NotMapped]");
                artifact.Mark(artifact.Code, MappingFactCategory.TransientProperty, propertyMap.Property.Name, "[NotMapped]");
                AppendSignature(artifact, entityMap, propertyMap, BuildPropertySignature(propertyMap.Property));
                continue;
            }

            if (KeylessEndOf(entityMap, propertyMap, relation: null) is { } keyless)
            {
                // A navigation nobody stated a relation for, which EF Core's convention would
                // make one of - over an entity without a key, where it cannot (decision 177).
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Incompleteness,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.CSharpEntity,
                    Entity = entityMap.Entity.Name,
                    Property = propertyMap.Property.Name,
                    Reason = KeylessReason(keyless),
                });
                artifact.Code.AppendLine("    [NotMapped]");
                AppendSignature(artifact, entityMap, propertyMap, BuildPropertySignature(propertyMap.Property));
                continue;
            }

            ReportNullableColumnLoss(entityMap, propertyMap);
            ReportVersionIncrementLoss(entityMap, propertyMap);

            AppendPropertyAttributes(artifact, propertyMap);
            AppendSignature(artifact, entityMap, propertyMap, BuildPropertySignature(propertyMap.Property, privateSetter: NeedsPrivateSetter(entityMap, propertyMap)));
        }
    }

    protected override void BuildForeignKey(EntityMap entityMap, EntityArtifact artifact)
    {
        foreach (var relation in entityMap.Relations)
        {
            var propertyMap = relation.SourceNavigationProperty is null
                ? null
                : entityMap.PropertyMaps.FirstOrDefault(pm => pm.Property.Name == relation.SourceNavigationProperty);

            if (propertyMap is null)
            {
                continue; // a relation without a navigation property is not emitted into C# code
            }

            // The generated key property's nullability follows the navigation's stated
            // nullability (decision 012): an unstated one counts as optional.
            bool nullable = propertyMap.IsNullable ?? true;

            if (KeylessEndOf(entityMap, propertyMap, relation) is { } keyless)
            {
                // EF Core maps no relationship without a principal key (decision 177): the
                // foreign key columns stay properties, the navigation is ignored, and the
                // relation is a fact of the model the artifact does not carry.
                if (relation.Role == RelationRole.Owning)
                {
                    AppendForeignKeyProperties(artifact, entityMap, relation, propertyMap.Property.Name, nullable);
                }

                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Loss,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.CSharpEntity,
                    Entity = entityMap.Entity.Name,
                    Property = propertyMap.Property.Name,
                    Category = MappingFactCategory.ForeignKeyColumns,
                    Reason = KeylessReason(keyless),
                });
                artifact.Code.AppendLine("    [NotMapped]");
                AppendSignature(artifact, entityMap, propertyMap, BuildPropertySignature(propertyMap.Property));
                continue;
            }

            ReportNullableColumnLoss(entityMap, propertyMap);
            ReportVersionIncrementLoss(entityMap, propertyMap);

            // The key properties come first: [ForeignKey] on the navigation names them, so a reader
            // meets them before the annotation that refers to them (decision 012).
            List<string> foreignKeyProperties = relation.Role == RelationRole.Owning
                ? AppendForeignKeyProperties(artifact, entityMap, relation, propertyMap.Property.Name, nullable)
                : [];

            if (foreignKeyProperties.Count > 0)
            {
                var rendered = $"[ForeignKey(\"{string.Join(',', foreignKeyProperties)}\")]";
                artifact.Code.AppendLine($"    {rendered}");
                artifact.Mark(artifact.Code, MappingFactCategory.ForeignKeyColumns, propertyMap.Property.Name, rendered);
            }
            else if (relation.Role == RelationRole.Owning && relation.ColumnPairs.Count == 0)
            {
                // Leaving [ForeignKey] out hands the derivation to the target's own
                // convention - allowed, because it fills in the same thing we would have
                // written, but still its convention and therefore recorded (decision 012).
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Convention,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.CSharpEntity,
                    Entity = entityMap.Entity.Name,
                    Property = propertyMap.Property.Name,
                    Category = MappingFactCategory.ForeignKeyColumns,
                    Reason = $"No foreign key columns are known for the relation to '{relation.TargetEntity}'; [ForeignKey] is left out and EF Core derives the key by its own convention (decision 012).",
                });
            }

            AppendPropertyAttributes(artifact, propertyMap);
            AppendSignature(artifact, entityMap, propertyMap, BuildPropertySignature(propertyMap.Property, privateSetter: NeedsPrivateSetter(entityMap, propertyMap)));
        }
    }

    /// <summary>
    /// Writes the scalar properties a foreign key consists of and returns their names in the order
    /// of the key they point at. [ForeignKey] names properties of the class, not columns, so where
    /// the model carries only a column the property has to be supplied here - the same division of
    /// labor as with the members a composite key forces on NHibernate (decisions 006 and 012).
    /// </summary>
    private List<string> AppendForeignKeyProperties(
        EntityArtifact artifact,
        EntityMap entityMap,
        Relation relation,
        string navigationProperty,
        bool nullable)
    {
        var code = artifact.Code;
        var names = new List<string>();
        var missing = new List<(string Name, LangType Type, string Column)>();

        foreach (var pair in relation.ColumnPairs)
        {
            // A column is not a property until it has a language type, so a property map carrying
            // only the column does not count as one the entity already has.
            var existing = entityMap.PropertyMaps.FirstOrDefault(pm =>
                pm.Property.Name == pair.Source.Property.Name && pm.Property.Type is not null);

            if (existing is not null)
            {
                names.Add(existing.Property.Name);
                continue;
            }

            if (pair.Target.Property.Type is null)
            {
                // Without the language type of the key part it points at there is nothing to
                // declare, and an annotation naming properties that do not exist would not compile.
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Loss,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.CSharpEntity,
                    Entity = entityMap.Entity.Name,
                    Property = navigationProperty,
                    Category = MappingFactCategory.ForeignKeyColumns,
                    Reason = $"[ForeignKey] cannot be written for the relation to '{relation.TargetEntity}': the language type of the referenced key part '{pair.Target.Property.Name}' is unknown, so the foreign key properties cannot be declared.",
                });
                return [];
            }

            var name = EFCoreColumnMember.NameFor(navigationProperty, pair.Target);
            missing.Add((name, pair.Target.Property.Type, pair.Source.ColumnName ?? pair.Source.Property.Name));
            names.Add(name);
        }

        foreach (var (name, type, column) in missing)
        {
            // The generated key property is where the foreign key's columns show themselves
            // beside [ForeignKey] (decision 125): the column attribute where the name differs,
            // the declaration itself otherwise.
            if (column != name)
            {
                var columnAttribute = $"[Column(\"{column}\")]";
                code.AppendLine($"    {columnAttribute}");
                artifact.Mark(code, MappingFactCategory.ForeignKeyColumns, navigationProperty, columnAttribute);
            }

            var declaration = $"public {CSharpTypeConvertor.ToString(type)}{(nullable ? "?" : string.Empty)} {name} {{ get; set; }}";
            code.AppendLine($"    {declaration}");
            artifact.Mark(code, MappingFactCategory.ForeignKeyColumns, navigationProperty, declaration);
            code.AppendLine();
        }

        return names;
    }

    /// <summary>
    /// The entity without a key that keeps EF Core from mapping the navigation, or null where
    /// it maps it (decision 177, measured in EFCoreKeylessNavigationProviderTest): a relationship
    /// needs a principal key, so EF Core 10 refuses the whole model over a navigation to a
    /// keyless entity, ignores a collection on one and invents a foreign key column on it for
    /// an inverse reference. What it maps is a reference the keyless entity holds as the
    /// dependent - the owning side, or a reference nobody stated a relation for, which its
    /// convention reads the same way. "Without a key" is the condition [Keyless] is written
    /// under. A member that names no entity of the conversion is no navigation here.
    /// </summary>
    private string? KeylessEndOf(EntityMap entityMap, PropertyMap propertyMap, Relation? relation)
    {
        var type = propertyMap.Property.Type;
        var isCollection = type?.Category == LangTypeCategory.Collection;
        var element = isCollection ? type!.ElementType : type;

        var targetName = relation?.TargetEntity
            ?? (element is { Category: LangTypeCategory.Reference } reference ? reference.TargetEntity : null);

        if (targetName is null || FindEntityMap(targetName) is not { } target)
        {
            return null;
        }

        if (target.PrimaryKey is null)
        {
            return target.Entity.Name;
        }

        var heldAsDependent = !isCollection && relation?.Role != RelationRole.Inverse;

        return entityMap.PrimaryKey is null && !heldAsDependent ? entityMap.Entity.Name : null;
    }

    private static string KeylessReason(string keyless)
        => $"'{keyless}' has no key, and EF Core maps no relationship without the key of its principal: over this "
            + "navigation it would refuse the model, ignore the navigation or invent a foreign key column. The navigation is "
            + "written [NotMapped]; the key, and with it the navigation, is what the catalog supplies (decisions 015 and 177).";

    /// <summary>
    /// EF Core forces nothing onto the body of the class; its only enforced element is the
    /// keyless marker, which precedes the class header and is emitted in BuildTableSchema.
    /// </summary>
    protected override void BuildEnforcedMembers(EntityMap entityMap, EntityArtifact artifact)
    {
    }

    /// <summary>
    /// The disagreement [Required] cannot carry: a nullable column behind a non-nullable
    /// property. EF Core derives NOT NULL from the type and the annotation form has
    /// nothing to override it with - IsRequired(false) is fluent-only - so the claim is
    /// dropped and the drop is recorded (decision 004).
    /// </summary>
    private void ReportNullableColumnLoss(EntityMap entityMap, PropertyMap propertyMap)
    {
        if (propertyMap.IsNullable != true || propertyMap.Property.Type is not { IsNullable: false })
        {
            return;
        }

        Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Loss,
            Framework = Descriptor.Framework,
            Artifact = ConversionContentType.CSharpEntity,
            Entity = entityMap.Entity.Name,
            Property = propertyMap.Property.Name,
            Category = MappingFactCategory.Nullability,
            Reason = "The source states a nullable column behind a non-nullable property; EF Core reads "
                + "NOT NULL from the type and the annotation form has nothing to override it with, so the "
                + "claim is dropped.",
        });
    }

    protected override IEnumerable<ConversionSource> FinalizeBuild(EntityMap entityMap, EntityArtifact artifact)
    {
        artifact.Code.AppendLine("}");

        yield return new ConversionSource
        {
            ContentType = ConversionContentType.CSharpEntity,
            Content = artifact.Code.ToString()
        };
    }

    /// <summary>
    /// Whether the property needs a setter the source does not give it (decision 134): EF Core
    /// maps by convention only a property it can write, so a persisted property with a getter
    /// alone gets the narrowest setter EF Core accepts. A collection a relation navigates is
    /// the exception - EF Core fills it through the backing field -, and a property the source
    /// keeps out of the model needs none.
    /// </summary>
    private static bool NeedsPrivateSetter(EntityMap entityMap, PropertyMap propertyMap)
        => propertyMap.Property is { HasGetter: true, HasSetter: false }
           && !propertyMap.IsTransient
           && !(propertyMap.Property.Type is { Category: LangTypeCategory.Collection }
                && entityMap.Relations.Any(r => r.SourceNavigationProperty == propertyMap.Property.Name));

    /// <summary>
    /// Builds the property signature for C# code.
    /// Adds modifiers, type, name, getter/setter, and default value.
    /// </summary>
    private static string BuildPropertySignature(Property prop, bool isPrimaryKey = false, bool privateSetter = false)
    {
        var otherMods = new List<string>(prop.OtherModifiers ?? []);

        var langType = prop.Type
            ?? throw new NotSupportedException($"Property '{prop.Name}' has no language type.");

        // The required modifier is a language device - a non-nullable property without an
        // initializer needs it to compile clean - so the language type decides it. What
        // the database says about the column travels through [Required], not through here.
        // A property without a setter cannot take it (CS9034), nor one with a setter narrower
        // than the class (CS9032): a Java field the class only exposes through a getter is
        // such a property, and so is one written with a private setter below.
        if (!langType.IsNullable && prop.HasSetter && !otherMods.Contains("required") && string.IsNullOrEmpty(prop.DefaultValue))
        {
            otherMods.Add("required");
        }

        var access = AccessModifierConvertor.ToModifierString(prop.AccessModifier);
        var modifiers = $"{access} {string.Join(' ', otherMods)}".Trim();

        var typeName = CSharpTypeConvertor.ToString(langType);
        var type = (!isPrimaryKey && langType.IsNullable) ? $"{typeName}?" : typeName;

        var setter = prop.HasSetter ? " set;" : privateSetter ? " private set;" : "";
        var getterSetter = (prop.HasGetter || prop.HasSetter)
            ? $" {{ {(prop.HasGetter ? "get;" : "")}{setter} }}"
            : "";
        var defaultVal = string.IsNullOrWhiteSpace(prop.DefaultValue)
            ? ""
            : $" = {prop.DefaultValue};";

        return $"{modifiers} {type} {prop.Name}{getterSetter}{defaultVal}";
    }

    /// <summary>
    /// The version a framework increments itself, which EF Core's annotations can compare but
    /// not increment. [Timestamp] would be the wrong carrier: EF Core reads it as a value the
    /// database produces on insert and update, so the column would be neither written nor
    /// changed, and optimistic concurrency would stop protecting anything. [ConcurrencyCheck]
    /// keeps the comparison on write and leaves the increment to the application - a
    /// narrowing of what the source stated, so it is recorded (decision 004). A version the
    /// application keeps already (decision 116) is [ConcurrencyCheck] by definition, so
    /// nothing is narrowed and nothing is recorded.
    /// </summary>
    private void ReportVersionIncrementLoss(EntityMap entityMap, PropertyMap propertyMap)
    {
        if (!propertyMap.IsVersion || propertyMap.IsApplicationManagedVersion || IsStoreGeneratedVersion(propertyMap))
        {
            return;
        }

        Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Loss,
            Framework = Descriptor.Framework,
            Artifact = ConversionContentType.CSharpEntity,
            Entity = entityMap.Entity.Name,
            Property = propertyMap.Property.Name,
            Category = MappingFactCategory.VersionColumn,
            Reason = "The source states a version its framework increments itself; EF Core's annotations "
                + "cannot state the increment, so [ConcurrencyCheck] compares the value on write and the "
                + "application has to increment it ([Timestamp] would claim the database produces it).",
        });
    }

    /// <summary>
    /// Whether the version's value is produced by the database rather than by the framework
    /// that maps it. A binary version is the shape of a SQL Server rowversion column, which
    /// the store fills on every write, and where no type family arrived a byte array is the
    /// only language type such a column has; a numeric or date-time version is one the
    /// source's framework increments itself - JPA's @Version int, NHibernate's version
    /// element over Int32. The NHibernate builder reads the family the same way when it
    /// decides on generated="always".
    /// </summary>
    private static bool IsStoreGeneratedVersion(PropertyMap propMap)
        => propMap.Type is DatabaseType.Binary or DatabaseType.VarBinary or DatabaseType.Blob
           || (propMap.Type is null
               && propMap.Property.Type is { Category: LangTypeCategory.Scalar, ScalarType: ScalarType.ByteArray });

    /// <summary>
    /// Whether the property is written as [Timestamp]: a version the store produces, which a
    /// stated application-managed version never is (decision 116).
    /// </summary>
    private static bool WritesTimestamp(PropertyMap propMap)
        => propMap.IsVersion && !propMap.IsApplicationManagedVersion && IsStoreGeneratedVersion(propMap);

    /// <summary>
    /// Builds the property attributes for EF Core.
    /// </summary>
    private static void AppendPropertyAttributes(EntityArtifact artifact, PropertyMap propMap, bool isPrimaryKey = false)
    {
        var code = artifact.Code;
        var property = propMap.Property.Name;

        // One attribute line, marked as where the facts it states stand (decision 125).
        void Line(string attribute, params MappingFactCategory[] categories)
        {
            code.AppendLine($"    {attribute}");

            foreach (var category in categories)
            {
                artifact.Mark(code, category, property, attribute);
            }
        }

        if (isPrimaryKey)
        {
            Line("[Key]", MappingFactCategory.PrimaryKey);
        }

        // A stated NOT NULL over a nullable property: EF Core reads the column's
        // nullability from the language type, so [Required] is the only carrier of the
        // claim. Where the type already agrees, the annotation would restate the target's
        // own reading (rule E4: a non-nullable property implies a non-nullable column);
        // a key column is never nullable, so the key needs no claim either.
        if (!isPrimaryKey && propMap.IsNullable == false && propMap.Property.Type is { IsNullable: true })
        {
            Line("[Required]", MappingFactCategory.Nullability);
        }

        // [Timestamp] states the type and the length of a rowversion column itself, so
        // neither travels beside it - see TypeNameFor.
        var typeCarriedByTimestamp = WritesTimestamp(propMap) && propMap.Type is not null;

        // A store-generated version is EF Core's rowversion; any other version is a
        // concurrency token the application keeps - see ReportVersionIncrementLoss. A
        // version the source already left to the application (decision 116) is that token
        // whatever its type.
        if (propMap.IsVersion)
        {
            Line(
                WritesTimestamp(propMap) ? "[Timestamp]" : "[ConcurrencyCheck]",
                typeCarriedByTimestamp
                    ? [MappingFactCategory.VersionColumn, MappingFactCategory.DatabaseType, MappingFactCategory.Length]
                    : [MappingFactCategory.VersionColumn]);
        }

        var typeText = TypeNameFor(propMap);

        if (propMap.ColumnName != null || typeText != null)
        {
            var parts = new List<string>();
            var categories = new List<MappingFactCategory>();

            if (propMap.ColumnName != null)
            {
                parts.Add($"\"{propMap.ColumnName}\"");
                categories.Add(MappingFactCategory.ColumnName);
            }

            if (typeText != null)
            {
                parts.Add($"TypeName=\"{typeText}\"");
                categories.Add(MappingFactCategory.DatabaseType);
            }

            Line($"[Column({string.Join(", ", parts)})]", [.. categories]);
        }

        if (NeedsUnicodeAttribute(propMap))
        {
            Line("[Unicode(false)]", MappingFactCategory.DatabaseType);
        }

        if (propMap.Length != null && !typeCarriedByTimestamp)
        {
            Line($"[MaxLength({propMap.Length})]", MappingFactCategory.Length);
        }

        if (propMap.Precision != null)
        {
            var args = propMap.Scale != null
                ? $"{propMap.Precision}, {propMap.Scale}"
                : $"{propMap.Precision}";

            Line($"[Precision({args})]", MappingFactCategory.PrecisionAndScale);
        }
    }

    /// <summary>
    /// The TypeName the [Column] annotation carries, or null where none goes out.
    ///
    /// The literal spelling of the source wins over the name derived from the family: it is
    /// what the source actually claimed, and the escape path of decision 019 exists precisely
    /// because the family is missing or coarser. The same rule the NHibernate builder applies
    /// to sql-type, so both .NET targets answer one model the same way - and a type with no
    /// family at all reaches the annotation instead of vanishing (decision 052). The derived
    /// name is the declared dialect's, and bare: the facets travel in [MaxLength] and
    /// [Precision] beside it (decision 086).
    ///
    /// [Timestamp] itself makes a binary column a rowversion on the target, so the type and
    /// length of such a column are already stated; a TypeName would override the rowversion
    /// mapping with plain varbinary and change the column.
    /// </summary>
    private static string? TypeNameFor(PropertyMap propMap)
    {
        if (WritesTimestamp(propMap) && propMap.Type is not null)
        {
            return null;
        }

        return propMap.SourceSqlType
            ?? (propMap.Type.HasValue
                ? SqlTypeSpelling.Name(EFCoreDescriptor.Instance.Dialect, propMap.Type.Value, propMap.IsUnicode)
                : null);
    }

    /// <summary>
    /// Whether [Unicode] has to be written for the property. The facet is part of the type
    /// claim (decision 019) and a type name states it on its own - nvarchar against varchar -
    /// so the annotation is due only where no type name goes out and the facet would
    /// otherwise be lost: EF Core's own convention maps a string to national character data,
    /// which is exactly what a source stating the opposite ruled out. A stated unicode is
    /// that same convention restated and is not written, the rule the key strategy follows
    /// below.
    /// </summary>
    private static bool NeedsUnicodeAttribute(PropertyMap propMap)
        => propMap.IsUnicode == false && TypeNameFor(propMap) is null;

    /// <summary>
    /// The strategy as an annotation. [DatabaseGenerated] can say two things: that the store
    /// produces the value on insert, and that nothing generates it. The named mechanisms -
    /// identity column, sequence, hi/lo - are fluent-only, so the model holds them and the
    /// annotation cannot; that narrowing is reported as a loss (decisions 010 and 011).
    ///
    /// It is emitted only where it changes what EF Core would do anyway. Restating the target's
    /// own convention adds noise, while leaving it out where the convention disagrees flips the
    /// claim - a string key marked Auto would silently stop being generated.
    ///
    /// An unspecified strategy over a key the convention generates is the one case where the
    /// silence itself makes a claim: nothing is written, yet the output behaves as generated.
    /// That claim is reported as a convention of the target - the mirror of the record
    /// NHibernate emits for its written 'assigned' fallback (decision 064).
    /// </summary>
    private void AppendKeyStrategyAttribute(EntityArtifact artifact, EntityMap entityMap, PrimaryKeyPart part, bool composite)
    {
        var code = artifact.Code;
        if (part.Strategy is PrimaryKeyStrategy.Identity or PrimaryKeyStrategy.Sequence
            or PrimaryKeyStrategy.HiLo or PrimaryKeyStrategy.Uuid or PrimaryKeyStrategy.Increment)
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.CSharpEntity,
                Entity = entityMap.Entity.Name,
                Property = part.PropertyMap.Property.Name,
                Category = MappingFactCategory.PrimaryKeyStrategy,
                Reason = $"The {part.Strategy} mechanism is fluent-only in EF Core; the annotation form cannot express it, so the strategy is dropped (decision 011).",
            });
        }

        bool generatedByConvention = IsGeneratedByConvention(part, composite);

        if (part.Strategy is PrimaryKeyStrategy.Unspecified && generatedByConvention)
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Convention,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.CSharpEntity,
                Entity = entityMap.Entity.Name,
                Property = part.PropertyMap.Property.Name,
                Category = MappingFactCategory.PrimaryKeyStrategy,
                Reason = "No generation strategy was stated; EF Core generates the value of a single-property "
                    + "integer or Guid key on its own, which is a convention of the target, not a fact of the source.",
            });
        }

        var option = part.Strategy switch
        {
            PrimaryKeyStrategy.Assigned when generatedByConvention => "None",
            PrimaryKeyStrategy.Auto when !generatedByConvention => "Identity",
            _ => null,
        };

        if (option is not null)
        {
            var rendered = $"[DatabaseGenerated(DatabaseGeneratedOption.{option})]";
            code.AppendLine($"    {rendered}");
            artifact.Mark(code, MappingFactCategory.PrimaryKeyStrategy, part.PropertyMap.Property.Name, rendered);
        }
    }

    /// <summary>
    /// EF Core generates a value on its own for a single-property key of an integer type
    /// or of Guid.
    /// </summary>
    private static bool IsGeneratedByConvention(PrimaryKeyPart part, bool composite)
        => !composite
        && part.PropertyMap.Property.Type is
        {
            Category: LangTypeCategory.Scalar,
            ScalarType: ScalarType.Byte or ScalarType.Short or ScalarType.Int
                or ScalarType.Long or ScalarType.Guid,
        };
}
