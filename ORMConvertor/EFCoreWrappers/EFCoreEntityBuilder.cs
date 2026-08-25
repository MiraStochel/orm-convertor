using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Convertors;
using EFCoreWrappers.Convertors;
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

        if (keyAttribute || precisionAttribute || indexAttribute)
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
    /// remaining constraints are still emitted.
    /// </summary>
    private void AppendUniqueConstraints(EntityMap entityMap, EntityArtifact artifact)
    {
        foreach (var constraint in entityMap.UniqueConstraints)
        {
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

            var parts = string.Join(", ", constraint.PropertyNames.Select(name => $"nameof({name})"));
            var named = constraint.Name is null ? string.Empty : $", Name = \"{constraint.Name}\"";

            artifact.Code.AppendLine($"[Index({parts}, IsUnique = true{named})]");
        }
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

            // For a simple key [Key] goes on the property; for a composite key the class-level
            // [PrimaryKey(...)] attribute (see BuildTableSchema) defines it and [Key] is not emitted.
            artifact.Code.Append(BuildPropertyAttributes(propertyMap, isPrimaryKey: !composite));
            AppendKeyStrategyAttribute(artifact.Code, entityMap, part, composite);
            artifact.Code.AppendLine($"    {BuildPropertySignature(propertyMap.Property, isPrimaryKey: true)}");
            artifact.Code.AppendLine();
        }
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

            ReportNullableColumnLoss(entityMap, propertyMap);

            artifact.Code.Append(BuildPropertyAttributes(propertyMap));
            artifact.Code.AppendLine($"    {BuildPropertySignature(propertyMap.Property)}");
            artifact.Code.AppendLine();
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

            ReportNullableColumnLoss(entityMap, propertyMap);

            // The key properties come first: [ForeignKey] on the navigation names them, so a reader
            // meets them before the annotation that refers to them (decision 012).
            List<string> foreignKeyProperties = relation.Role == RelationRole.Owning
                ? AppendForeignKeyProperties(artifact.Code, entityMap, relation, propertyMap.Property.Name, nullable)
                : [];

            if (foreignKeyProperties.Count > 0)
            {
                artifact.Code.AppendLine($"    [ForeignKey(\"{string.Join(',', foreignKeyProperties)}\")]");
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

            artifact.Code.Append(BuildPropertyAttributes(propertyMap));
            artifact.Code.AppendLine($"    {BuildPropertySignature(propertyMap.Property)}");
            artifact.Code.AppendLine();
        }
    }

    /// <summary>
    /// Writes the scalar properties a foreign key consists of and returns their names in the order
    /// of the key they point at. [ForeignKey] names properties of the class, not columns, so where
    /// the model carries only a column the property has to be supplied here - the same division of
    /// labor as with the members a composite key forces on NHibernate (decisions 006 and 012).
    /// </summary>
    private List<string> AppendForeignKeyProperties(
        StringBuilder code,
        EntityMap entityMap,
        Relation relation,
        string navigationProperty,
        bool nullable)
    {
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

            var name = navigationProperty + pair.Target.Property.Name;
            missing.Add((name, pair.Target.Property.Type, pair.Source.ColumnName ?? pair.Source.Property.Name));
            names.Add(name);
        }

        foreach (var (name, type, column) in missing)
        {
            if (column != name)
            {
                code.AppendLine($"    [Column(\"{column}\")]");
            }

            code.AppendLine($"    public {CSharpTypeConvertor.ToString(type)}{(nullable ? "?" : string.Empty)} {name} {{ get; set; }}");
            code.AppendLine();
        }

        return names;
    }

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
    /// Builds the property signature for C# code.
    /// Adds modifiers, type, name, getter/setter, and default value.
    /// </summary>
    private static string BuildPropertySignature(Property prop, bool isPrimaryKey = false)
    {
        var otherMods = new List<string>(prop.OtherModifiers ?? []);

        var langType = prop.Type
            ?? throw new NotSupportedException($"Property '{prop.Name}' has no language type.");

        // The required modifier is a language device - a non-nullable property without an
        // initializer needs it to compile clean - so the language type decides it. What
        // the database says about the column travels through [Required], not through here.
        if (!langType.IsNullable && !otherMods.Contains("required") && string.IsNullOrEmpty(prop.DefaultValue))
        {
            otherMods.Add("required");
        }

        var access = AccessModifierConvertor.ToModifierString(prop.AccessModifier);
        var modifiers = $"{access} {string.Join(' ', otherMods)}".Trim();

        var typeName = CSharpTypeConvertor.ToString(langType);
        var type = (!isPrimaryKey && langType.IsNullable) ? $"{typeName}?" : typeName;

        var getterSetter = (prop.HasGetter || prop.HasSetter)
            ? $" {{ {(prop.HasGetter ? "get;" : "")}{(prop.HasSetter ? " set;" : "")} }}"
            : "";
        var defaultVal = string.IsNullOrWhiteSpace(prop.DefaultValue)
            ? ""
            : $" = {prop.DefaultValue};";

        return $"{modifiers} {type} {prop.Name}{getterSetter}{defaultVal}";
    }

    /// <summary>
    /// Builds the property attributes for EF Core.
    /// </summary>
    private static string BuildPropertyAttributes(PropertyMap propMap, bool isPrimaryKey = false)
    {
        StringBuilder attributes = new();

        if (isPrimaryKey)
        {
            attributes.AppendLine($"    [Key]");
        }

        // A stated NOT NULL over a nullable property: EF Core reads the column's
        // nullability from the language type, so [Required] is the only carrier of the
        // claim. Where the type already agrees, the annotation would restate the target's
        // own reading (rule E4: a non-nullable property implies a non-nullable column);
        // a key column is never nullable, so the key needs no claim either.
        if (!isPrimaryKey && propMap.IsNullable == false && propMap.Property.Type is { IsNullable: true })
        {
            attributes.AppendLine("    [Required]");
        }

        if (propMap.IsVersion)
        {
            attributes.AppendLine("    [Timestamp]");
        }

        // [Timestamp] itself makes a binary column a rowversion on the target, so the
        // type and length of such a column are already stated; a TypeName would override
        // the rowversion mapping with plain varbinary and change the column.
        var typeCarriedByTimestamp = propMap.IsVersion
            && propMap.Type is DatabaseType.Binary or DatabaseType.VarBinary or DatabaseType.Blob;

        // The literal spelling of the source wins over the name derived from the family: it
        // is what the source actually claimed, and the escape path of decision 019 exists
        // precisely because the family is missing or coarser. The same rule the NHibernate
        // builder applies to sql-type, so both .NET targets answer one model the same way -
        // and a type with no family at all now reaches the annotation instead of vanishing
        // (decision 052).
        var typeText = typeCarriedByTimestamp
            ? null
            : propMap.SourceSqlType
              ?? (propMap.Type.HasValue
                  ? DatabaseTypeConvertor.ToEFCore(propMap.Type.Value, propMap.IsUnicode)
                  : null);

        if (propMap.ColumnName != null || typeText != null)
        {
            var parts = new List<string>();
            if (propMap.ColumnName != null)
            {
                parts.Add($"\"{propMap.ColumnName}\"");
            }

            if (typeText != null)
            {
                parts.Add($"TypeName=\"{typeText}\"");
            }

            attributes.AppendLine($"    [Column({string.Join(", ", parts)})]");
        }


        if (propMap.Length != null && !typeCarriedByTimestamp)
        {
            attributes.AppendLine($"    [MaxLength({propMap.Length})]");
        }

        if (propMap.Precision != null)
        {
            var args = propMap.Scale != null
                ? $"{propMap.Precision}, {propMap.Scale}"
                : $"{propMap.Precision}";

            attributes.AppendLine($"    [Precision({args})]");
        }

        return attributes.ToString();
    }

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
    private void AppendKeyStrategyAttribute(StringBuilder code, EntityMap entityMap, PrimaryKeyPart part, bool composite)
    {
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
            code.AppendLine($"    [DatabaseGenerated(DatabaseGeneratedOption.{option})]");
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